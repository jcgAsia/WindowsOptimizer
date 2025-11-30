using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using WindowsOptimizer.Models;

namespace WindowsOptimizer.Services
{
    /// <summary>
    /// PlanB 기술문서 5.1 서버 설정 파일 동기화
    /// </summary>
    public class ConfigService
    {
        private static readonly Lazy<ConfigService> _instance = new Lazy<ConfigService>(() => new ConfigService());
        public static ConfigService Instance => _instance.Value;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly string _configDir;

        /// <summary>
        /// 서버 기본 URL (예: https://server.com/planb)
        /// </summary>
        public string ServerBaseUrl { get; set; } = "https://your-server.com/planb";

        /// <summary>
        /// 로드된 매핑 설정
        /// </summary>
        public MappingConfig MappingConfig { get; private set; }

        private ConfigService()
        {
            _configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                GlobalConfig.AppFolderName, "config");

            try { Directory.CreateDirectory(_configDir); } catch { }
        }

        /// <summary>
        /// mapping.xml 로드 (서버 → 로컬)
        /// </summary>
        public async Task LoadMappingConfigAsync()
        {
            var localPath = Path.Combine(_configDir, "mapping.xml");

            // 1. 서버에서 다운로드 시도
            try
            {
                var url = $"{ServerBaseUrl}/mapping.xml";
                var xml = await _http.GetStringAsync(url);
                File.WriteAllText(localPath, xml);
                LogService.Instance.Log($"[ConfigService] 서버에서 mapping.xml 다운로드 완료");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ConfigService] 서버 다운로드 실패: {ex.Message}");
            }

            // 2. 로컬 파일 로드
            if (File.Exists(localPath))
            {
                try
                {
                    MappingConfig = MappingConfig.LoadFromFile(localPath);
                    LogService.Instance.Log($"[ConfigService] 매핑 로드: {MappingConfig?.Mappings?.Count ?? 0}개");
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log($"[ConfigService] 매핑 파싱 실패: {ex.Message}");
                }
            }

            // 3. 없으면 샘플 생성
            if (MappingConfig == null)
            {
                MappingConfig = MappingConfig.CreateSample();
                MappingConfig.SaveToFile(localPath);
                LogService.Instance.Log("[ConfigService] 샘플 매핑 생성됨");
            }
        }

        /// <summary>
        /// 매핑 설정 저장
        /// </summary>
        public void SaveMappingConfig()
        {
            if (MappingConfig == null) return;
            var path = Path.Combine(_configDir, "mapping.xml");
            MappingConfig.SaveToFile(path);
        }

        /// <summary>
        /// 설정 디렉토리 경로
        /// </summary>
        public string ConfigDirectory => _configDir;
    }
}
