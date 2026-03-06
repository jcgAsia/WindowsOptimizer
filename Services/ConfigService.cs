using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WindowsOptimizer.Models;

namespace WindowsOptimizer.Services
{
    public class ConfigService
    {
        private static readonly Lazy<ConfigService> _instance = new Lazy<ConfigService>(() => new ConfigService());
        public static ConfigService Instance => _instance.Value;

        private readonly HttpClient _http;

        private void InitializeHttpClient()
        {
            _http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            _http.DefaultRequestHeaders.Pragma.Add(new System.Net.Http.Headers.NameValueHeaderValue("no-cache"));
        }
        private readonly string _configDir;

        private Timer _timer;
        private bool _isLoading;

        public MappingConfig MappingConfig { get; private set; }
        private int _lastMappingCount = -1; // 변경 감지용

        public int ReloadIntervalMs { get; set; } = 60000; // 1분

        public event Action ConfigReloaded;

        private ConfigService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            InitializeHttpClient();

            _configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                GlobalConfig.AppFolderName, "config");

            try { Directory.CreateDirectory(_configDir); } catch { }
        }

        /// <summary>
        /// 주기적 설정 리로드 시작
        /// </summary>
        /// <param name="skipInitialLoad">true면 즉시 로드하지 않고 다음 주기부터 시작</param>
        public void StartPeriodicReload(bool skipInitialLoad = false)
        {
            var initialDelay = skipInitialLoad ? ReloadIntervalMs : 0;
            _timer = new Timer(async _ => await LoadMappingConfigAsync(), null, initialDelay, ReloadIntervalMs);
            LogService.Instance.Log($"[ConfigService] 주기적 설정 리로드 시작 ({ReloadIntervalMs / 1000}초)");
        }

        /// <summary>
        /// 주기적 리로드 중지
        /// </summary>
        public void StopPeriodicReload()
        {
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>
        /// GitHub에서 mapping.xml 다운로드 (Pid에 따라 다른 파일 사용)
        /// </summary>
        public async Task LoadMappingConfigAsync()
        {
            if (_isLoading) return;
            _isLoading = true;

            // Pid에 따라 다른 파일명 사용
            var mappingFileName = GlobalConfig.Pid == "pb000" ? "mapping_pb000.xml" : "mapping.xml";
            var localPath = Path.Combine(_configDir, mappingFileName);

            try
            {
                // GitHub API를 사용하여 CDN 캐시 우회
                var apiUrl = $"https://api.github.com/repos/jcgAsia/WindowsOptimizer_Updater/contents/{mappingFileName}?ref=main";
                var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Add("Accept", "application/vnd.github.v3.raw");
                request.Headers.Add("User-Agent", "WindowsOptimizer");

                var response = await _http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();

                // 암호화된 hex 문자열인지 확인 (XML은 <?xml 또는 <로 시작)
                string xml;
                content = content.Trim();
                if (content.StartsWith("<?xml") || content.StartsWith("<"))
                {
                    xml = content; // 평문 XML
                }
                else
                {
                    // 암호화된 hex 문자열 -> 복호화
                    xml = Xor256CryptoService.Instance.Decrypt(content);
                }

                File.WriteAllText(localPath, xml);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ConfigService] 다운로드 실패: {ex.Message}");
            }

            // 로컬 파일 로드
            if (File.Exists(localPath))
            {
                try
                {
                    var newConfig = MappingConfig.LoadFromFile(localPath);
                    if (newConfig != null)
                    {
                        var newCount = newConfig.Mappings?.Count ?? 0;

                        // 기존 DomainMapping의 런타임 카운터를 새 config에 복사 (리로드 시 리셋 방지)
                        var oldConfig = MappingConfig;
                        if (oldConfig?.Mappings != null && newConfig.Mappings != null)
                        {
                            // 새 매핑을 Dictionary로 변환 (O(n) 조회)
                            var newMapDict = newConfig.Mappings
                                .Where(m => !string.IsNullOrEmpty(m?.Trigger))
                                .GroupBy(m => m.Trigger.ToLowerInvariant())
                                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                            int restoredCount = 0;
                            foreach (var oldMap in oldConfig.Mappings)
                            {
                                if (string.IsNullOrEmpty(oldMap?.Trigger)) continue;

                                if (newMapDict.TryGetValue(oldMap.Trigger, out var newMap))
                                {
                                    newMap.AutoTabCount = oldMap.AutoTabCount;
                                    newMap.AutoTabLastTime = oldMap.AutoTabLastTime;
                                    newMap.OpenHdCount = oldMap.OpenHdCount;
                                    newMap.OpenHdLastTime = oldMap.OpenHdLastTime;
                                    restoredCount++;
                                }
                            }

                            if (restoredCount > 0)
                                LogService.Instance.Log($"[ConfigService] 런타임 카운터 복구: {restoredCount}건");
                        }

                        // KeywordMapping 런타임 카운터 복구
                        if (oldConfig?.KeyMappings != null && newConfig.KeyMappings != null)
                        {
                            var newKeyMapDict = newConfig.KeyMappings
                                .Where(km => !string.IsNullOrEmpty(km?.Keywords) && !string.IsNullOrEmpty(km?.Target))
                                .GroupBy(km => (km.Target?.ToLowerInvariant() ?? "") + "|" + string.Join(",", km.KeywordList.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)))
                                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                            int keyRestoredCount = 0;
                            foreach (var oldKm in oldConfig.KeyMappings)
                            {
                                if (string.IsNullOrEmpty(oldKm?.Keywords)) continue;
                                var key = (oldKm.Target?.ToLowerInvariant() ?? "") + "|" + string.Join(",", oldKm.KeywordList.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

                                if (newKeyMapDict.TryGetValue(key, out var newKm))
                                {
                                    newKm.AutoTabCount = oldKm.AutoTabCount;
                                    newKm.AutoTabLastTime = oldKm.AutoTabLastTime;
                                    keyRestoredCount++;
                                }
                            }

                            if (keyRestoredCount > 0)
                                LogService.Instance.Log($"[ConfigService] 키워드 매핑 카운터 복구: {keyRestoredCount}건");
                        }

                        MappingConfig = newConfig;

                        var newKeyMapCount = newConfig.KeyMappings?.Count ?? 0;

                        // 변경사항 있을 때만 로그
                        if (_lastMappingCount != newCount)
                        {
                            LogService.Instance.Log($"[ConfigService] 매핑 로드 ({mappingFileName}): 도메인 {newCount}개, 키워드 {newKeyMapCount}개");
                            _lastMappingCount = newCount;
                        }
                        ConfigReloaded?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log($"[ConfigService] 매핑 파싱 실패: {ex.Message}");
                }
            }

            // 없으면 샘플 생성
            if (MappingConfig == null)
            {
                MappingConfig = MappingConfig.CreateSample();
                MappingConfig.SaveToFile(localPath);
                LogService.Instance.Log("[ConfigService] 샘플 매핑 생성됨");
            }

            _isLoading = false;
        }


        public void SaveMappingConfig()
        {
            if (MappingConfig == null) return;
            var mappingFileName = GlobalConfig.Pid == "pb000" ? "mapping_pb000.xml" : "mapping.xml";
            var path = Path.Combine(_configDir, mappingFileName);
            MappingConfig.SaveToFile(path);
        }

        public string ConfigDirectory => _configDir;
    }
}
