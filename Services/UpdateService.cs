using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WindowsOptimizer.Services
{
    public class UpdateService
    {
        private static readonly Lazy<UpdateService> _instance = new Lazy<UpdateService>(() => new UpdateService());
        public static UpdateService Instance => _instance.Value;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public string ServerBaseUrl { get; set; } = "https://your-server.com/planb";
        public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        private UpdateService() { }

        public async Task<bool> CheckAndUpdateAsync()
        {
            try
            {
                LogService.Instance.Log("업데이트 확인 중...");

                var xml = await _http.GetStringAsync($"{ServerBaseUrl}/version.xml");
                var doc = XDocument.Parse(xml);
                var root = doc.Element("version");

                var serverVersion = root?.Element("number")?.Value;
                var downloadUrl = root?.Element("url")?.Value;
                var checksum = root?.Element("checksum")?.Value;

                if (IsNewerVersion(serverVersion))
                {
                    LogService.Instance.Log($"새 버전 발견: {serverVersion}");
                    return await DownloadAndApplyAsync(downloadUrl, checksum);
                }
                else
                {
                    LogService.Instance.Log("최신 버전입니다.");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"업데이트 확인 실패: {ex.Message}");
            }
            return false;
        }

        private bool IsNewerVersion(string serverVersion)
        {
            try
            {
                var current = new Version(CurrentVersion);
                var server = new Version(serverVersion);
                return server > current;
            }
            catch { return false; }
        }

        private async Task<bool> DownloadAndApplyAsync(string url, string checksum)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "WindowsOptimizer_update.exe");

            try
            {
                var bytes = await _http.GetByteArrayAsync(url);
                File.WriteAllBytes(tempPath, bytes);

                // 체크섬 확인
                if (!string.IsNullOrEmpty(checksum))
                {
                    using (var sha = SHA256.Create())
                    {
                        var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                        if (!hash.Equals(checksum, StringComparison.OrdinalIgnoreCase))
                        {
                            LogService.Instance.Log("체크섬 불일치");
                            return false;
                        }
                    }
                }

                // 업데이트 스크립트 생성
                var currentExe = Assembly.GetExecutingAssembly().Location;
                var script = Path.Combine(Path.GetTempPath(), "update.bat");

                File.WriteAllText(script,
                    "@echo off\r\n" +
                    "timeout /t 2 /nobreak > nul\r\n" +
                    $"copy /Y \"{tempPath}\" \"{currentExe}\"\r\n" +
                    $"start \"\" \"{currentExe}\"\r\n" +
                    "del \"%~f0\"\r\n");

                Process.Start(new ProcessStartInfo
                {
                    FileName = script,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                // 레지스트리에 업데이트 완료 표시
                RegistryService.Instance.SetValue("update_done", 1);

                LogService.Instance.Log("업데이트 적용 중...");
                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"업데이트 실패: {ex.Message}");
                return false;
            }
        }
    }
}
