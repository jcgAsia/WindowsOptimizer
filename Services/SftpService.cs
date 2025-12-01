using System;
using System.IO;
using System.Threading.Tasks;
using Renci.SshNet;

namespace WindowsOptimizer.Services
{
    /// <summary>
    /// SFTP를 통한 서버 파일 동기화 서비스
    /// </summary>
    public class SftpService
    {
        private static readonly Lazy<SftpService> _instance = new Lazy<SftpService>(() => new SftpService());
        public static SftpService Instance => _instance.Value;

        private readonly string _localConfigDir;

        private SftpService()
        {
            _localConfigDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                GlobalConfig.AppFolderName, "config");
            try { Directory.CreateDirectory(_localConfigDir); } catch { }
        }

        /// <summary>
        /// SFTP로 mapping.xml 다운로드
        /// </summary>
        public async Task<bool> DownloadMappingAsync()
        {
            return await Task.Run(() => DownloadFile("mapping.xml"));
        }

        /// <summary>
        /// SFTP로 version.xml 다운로드
        /// </summary>
        public async Task<bool> DownloadVersionAsync()
        {
            return await Task.Run(() => DownloadFile("version.xml"));
        }

        /// <summary>
        /// 서버에서 파일 다운로드
        /// </summary>
        private bool DownloadFile(string fileName)
        {
            try
            {
                using (var client = new SftpClient(
                    GlobalConfig.SftpHost, 
                    GlobalConfig.SftpPort, 
                    GlobalConfig.SftpUser, 
                    GlobalConfig.SftpPass))
                {
                    client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                    client.Connect();

                    var remotePath = $"{GlobalConfig.SftpBasePath}/{fileName}";
                    var localPath = Path.Combine(_localConfigDir, fileName);

                    using (var fs = File.Create(localPath))
                    {
                        client.DownloadFile(remotePath, fs);
                    }

                    client.Disconnect();
                    LogService.Instance.Log($"[SFTP] {fileName} 다운로드 완료");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[SFTP] {fileName} 다운로드 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 연결 테스트
        /// </summary>
        public bool TestConnection()
        {
            try
            {
                using (var client = new SftpClient(
                    GlobalConfig.SftpHost,
                    GlobalConfig.SftpPort,
                    GlobalConfig.SftpUser,
                    GlobalConfig.SftpPass))
                {
                    client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(5);
                    client.Connect();
                    var result = client.IsConnected;
                    client.Disconnect();
                    return result;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[SFTP] 연결 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 설정 디렉토리 경로
        /// </summary>
        public string LocalConfigDir => _localConfigDir;
    }
}
