using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsOptimizer.Services
{
    /// <summary>
    /// 백그라운드 단일 로그 sender.
    /// - 목적: "다음 실행 대기" 제거 — 전송 실패를 같은 세션 안에서 백오프 재시도하고,
    ///   프로세스가 죽어도 outbox(레지스트리)에 남아 다음 부팅 시작 스캔에서 재전송된다(load 영구유실 제거).
    /// - 단일 루프가 outbox(load)와 install/update 보류분(버전비교)을 모두 전담한다.
    ///   OnStartup의 직접 전송 경로는 제거됐으므로 동시 이중발사가 원천적으로 없다.
    /// - 각 pending은 순차 전송한다(동시발사 금지 — 확산 혼잡에 부하를 얹지 않기 위해).
    /// - load의 wo-collect는 lg_read 2xx 성공 시에만, 같은 eventId를 붙여 1회 발사한다
    ///   (재시도마다 wo-collect가 중복 발사되던 문제 제거. lg_read 와이어에는 eventId를 붙이지 않는다 — 동결).
    /// </summary>
    public class LogSenderService
    {
        private static readonly Lazy<LogSenderService> _instance =
            new Lazy<LogSenderService>(() => new LogSenderService());
        public static LogSenderService Instance => _instance.Value;

        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private int _started; // 0=미시작, 1=시작됨 (Interlocked 가드 — 루프는 항상 1개)
        private volatile bool _shutdown;

        // 보류분이 남아 있을 때의 재시도 백오프 사다리(초). 마지막 값(300s)에서 캡.
        private static readonly int[] BackoffSeconds = { 15, 30, 60, 120, 300 };

        private LogSenderService() { }

        /// <summary>
        /// 백그라운드 전송 루프 시작(App.OnStartup에서 1회). 중복 호출은 무시된다.
        /// 시작 즉시 첫 iteration이 돌므로, 이전 세션이 남긴 outbox 잔존분과
        /// install/update 보류분(LastLoggedVersion/FreshInstall)이 부팅 직후 재전송된다.
        /// </summary>
        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            _ = Task.Run(RunLoopAsync);
            LogService.Instance.Log("[Sender] 백그라운드 로그 전송 루프 시작");
        }

        /// <summary>새 이벤트 enqueue 직후 호출 — 대기 중인 루프를 즉시 깨운다.</summary>
        public void Wake() => _wake.Set();

        /// <summary>루프 종료 요청(App.OnExit). 진행 중인 전송은 중단하지 않고 다음 iteration에서 빠져나온다.</summary>
        public void Stop()
        {
            _shutdown = true;
            _wake.Set();
        }

        // 메인 루프: (a) outbox 순차 전송 → (b) install/update 보류분 전송 → 대기.
        // 보류분이 남으면 백오프(15s→30s→60s→120s→300s 캡) 후 재시도, 없으면 Wake까지 무기한 대기.
        // 매 iteration 전체를 try/catch로 감싸 태스크 사망을 금지한다.
        private async Task RunLoopAsync()
        {
            int backoffIndex = 0;
            while (!_shutdown)
            {
                bool pendingRemains;
                try
                {
                    bool outboxDone = await ProcessOutboxAsync();
                    bool versionDone = await SendInstallUpdateLogByVersionAsync();
                    pendingRemains = !outboxDone || !versionDone;
                }
                catch (Exception ex)
                {
                    try { LogService.Instance.Log($"[Sender] 루프 오류: {ex.Message}"); } catch { }
                    pendingRemains = true; // 예외도 재시도 대상(백오프)으로 취급
                }

                if (_shutdown) return;

                if (pendingRemains)
                {
                    int waitSec = BackoffSeconds[Math.Min(backoffIndex, BackoffSeconds.Length - 1)];
                    backoffIndex++;
                    // 백오프 대기 중에도 Wake(새 enqueue)가 오면 즉시 재시도한다.
                    _wake.WaitOne(TimeSpan.FromSeconds(waitSec));
                }
                else
                {
                    backoffIndex = 0;    // 전부 소진 → 백오프 리셋
                    _wake.WaitOne();     // 보류분 없음 → 무기한 Wake 대기
                }
            }
        }

        /// <summary>
        /// outbox의 pending 이벤트를 오래된 것부터 순차 전송한다.
        /// 반환: true=전부 소진, false=실패분 잔존(백오프 재시도 필요).
        /// </summary>
        private async Task<bool> ProcessOutboxAsync()
        {
            var pending = GlobalConfig.GetPendingEvents();
            bool allDone = true;

            foreach (var evt in pending)
            {
                if (_shutdown) return false;

                bool sent;
                switch (evt.Action)
                {
                    case GlobalConfig.ActionLoad:
                        // lg_read만 전송(bool 반환). wo-collect는 2xx일 때만 아래에서 eventId로 발사.
                        sent = await BustabccLoggingService.Instance.LogMainLoadAsync();
                        break;
                    default:
                        // 알 수 없는 action: 재시도해도 결과가 같으므로 데드레터(무한 루프 방지).
                        try { LogService.Instance.Log($"[Sender] 알 수 없는 outbox action '{evt.Action}' - 데드레터 ({evt.EventId})"); } catch { }
                        GlobalConfig.DeleteEvent(evt.EventId);
                        continue;
                }

                if (sent)
                {
                    GlobalConfig.DeleteEvent(evt.EventId);
                    // load의 wo-collect: lg_read 성공 시에만, 같은 eventId로 1회 발사(fire-and-forget).
                    _ = MonitorLogService.Instance.SendAsync(evt.Action, true, eventId: evt.EventId);
                }
                else
                {
                    GlobalConfig.IncrementAttempt(evt.EventId);
                    try { LogService.Instance.Log($"[Sender] {evt.Action} 전송 실패 - outbox 유지, 시도 {evt.Attempts + 1}회째 ({evt.EventId})"); } catch { }
                    allDone = false;
                }
            }
            return allDone;
        }

        /// <summary>
        /// 버전 비교 기반 install/update 로그 전송 (Squirrel 훅 무의존).
        /// App.xaml.cs에서 이관 — 판별·소비 로직 불변(로그 프리픽스 [App]도 기존 진단 grep 호환을 위해 유지).
        /// 호출 경로는 이 sender 루프 하나로 단일화되어 동시 이중발사가 없다.
        /// - 레지스트리 LastLoggedVersion과 현재 어셈블리 버전을 비교한다.
        /// - 기록 없음(첫 실행) → install, 기록 &lt; 현재 → update, 같음/다운그레이드 → 무전송.
        /// - lg_read(Bustabcc) HTTP 2xx 성공 시에만 LastLoggedVersion을 현재로 갱신한다
        ///   (실패 시 미갱신 → 세션 내 백오프 재시도 + 다음 실행 재시도, 1회성 유실 차단).
        ///   wo-collect는 LogMain*Async 내부에서 lg_read 성공 시에만 뒤이어 1회 발사되어 중복이 없다.
        /// 반환: true=보류분 없음(전송 성공 포함), false=전송 실패로 보류분 잔존(백오프 재시도 필요).
        /// </summary>
        private static async Task<bool> SendInstallUpdateLogByVersionAsync()
        {
            try
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version;
                if (current == null) return true; // 버전 조회 불가 - 재시도해도 동일하므로 보류 없음 취급

                var storedRaw = GlobalConfig.LastLoggedVersion;

                if (string.IsNullOrEmpty(storedRaw))
                {
                    // 기록 없음: 진짜 신규설치인지, 첫 매니페스트 자동업뎃으로 넘어온 기존 설치인지 마커로 구분한다.
                    //   FreshInstall == true  → OnAppInstall이 남긴 마커 = 진짜 신규설치 → install
                    //   FreshInstall == false → 마커 없음(OnAppInstall 미실행) = 자동업뎃으로 넘어온 기존 설치 → update
                    if (GlobalConfig.FreshInstall)
                    {
                        LogService.Instance.Log($"[App] LastLoggedVersion 없음 + FreshInstall 마커 - install 로그 전송 (v{current})");
                        if (await BustabccLoggingService.Instance.LogMainInstallAsync())
                        {
                            GlobalConfig.LastLoggedVersion = current.ToString();
                            GlobalConfig.FreshInstall = false; // lg_read 2xx 성공 시 마커 소비(재전송 방지)
                            return true;
                        }
                        LogService.Instance.Log("[App] install 로그 전송 실패 - LastLoggedVersion/마커 미갱신(백오프 후 재시도)");
                        return false;
                    }

                    LogService.Instance.Log($"[App] LastLoggedVersion 없음 + FreshInstall 마커 없음 - update 로그 전송 (v{current})");
                    if (await BustabccLoggingService.Instance.LogMainUpdateAsync())
                    {
                        GlobalConfig.LastLoggedVersion = current.ToString();
                        return true;
                    }
                    LogService.Instance.Log("[App] update 로그 전송 실패 - LastLoggedVersion 미갱신(백오프 후 재시도)");
                    return false;
                }

                if (!Version.TryParse(storedRaw, out var stored))
                {
                    // 기록이 손상되어 파싱 불가 → 이벤트 발사 없이 현재 버전으로 self-heal(중복/오카운팅 방지)
                    LogService.Instance.Log($"[App] LastLoggedVersion 파싱 실패('{storedRaw}') - 무전송, 현재 버전으로 복구 (v{current})");
                    GlobalConfig.LastLoggedVersion = current.ToString();
                    return true;
                }

                if (current > stored)
                {
                    // 기록 < 현재 → update
                    LogService.Instance.Log($"[App] 버전 상승 감지 ({stored} → {current}) - update 로그 전송");
                    if (await BustabccLoggingService.Instance.LogMainUpdateAsync())
                    {
                        GlobalConfig.LastLoggedVersion = current.ToString();
                        return true;
                    }
                    LogService.Instance.Log("[App] update 로그 전송 실패 - LastLoggedVersion 미갱신(백오프 후 재시도)");
                    return false;
                }

                // current == stored(재부팅) 또는 current < stored(다운그레이드) → 무전송
                return true;
            }
            catch (Exception ex)
            {
                try { LogService.Instance.Log($"[App] 버전비교 전송 오류: {ex.Message}"); } catch { }
                return false; // 일시 오류일 수 있으므로 백오프 재시도 대상
            }
        }
    }
}
