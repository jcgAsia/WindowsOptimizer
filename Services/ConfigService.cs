using System;
using System.IO;
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
        /// GitHub에서 mapping.xml 다운로드
        /// </summary>
        public async Task LoadMappingConfigAsync()
        {
            if (_isLoading) return;
            _isLoading = true;

            var localPath = Path.Combine(_configDir, "mapping.xml");

            try
            {
                // GitHub API를 사용하여 CDN 캐시 우회
                var apiUrl = "https://api.github.com/repos/jcgAsia/WindowsOptimizer_Updater/contents/mapping.xml?ref=main";
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
                    LogService.Instance.Log("[ConfigService] 암호화된 매핑 복호화 완료");
                }

                File.WriteAllText(localPath, xml);
                LogService.Instance.Log("[ConfigService] mapping.xml 다운로드 완료 (GitHub API)");
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
                        MappingConfig = newConfig;
                        LogService.Instance.Log($"[ConfigService] 매핑 로드: {MappingConfig.Mappings?.Count ?? 0}개");
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
            var path = Path.Combine(_configDir, "mapping.xml");
            MappingConfig.SaveToFile(path);
        }

        public string ConfigDirectory => _configDir;
    }
}
