using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WindowsOptimizer.Models;

namespace WindowsOptimizer.Services
{
    /// <summary>
    /// bustabcc.net XML 기반 프로그램 업데이트 서비스
    /// URL: https://bustabcc.net/SWC/ups_read.php?client={clientId}
    /// </summary>
    public class ProgramUpdateService
    {
        private static readonly Lazy<ProgramUpdateService> _instance =
            new Lazy<ProgramUpdateService>(() => new ProgramUpdateService());
        public static ProgramUpdateService Instance => _instance.Value;

        private readonly HttpClient _http;
        private ProgramUpdateConfig _config;

        private ProgramUpdateService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        /// <summary>
        /// 현재 로드된 설정
        /// </summary>
        public ProgramUpdateConfig Config => _config;

        /// <summary>
        /// XML 업데이트 설정을 서버에서 다운로드
        /// </summary>
        public async Task<bool> LoadConfigAsync()
        {
            try
            {
                var url = $"{GlobalConfig.BustabccUpdateUrl}?client={Uri.EscapeDataString(GlobalConfig.Pid)}";
                LogService.Instance.Log($"[ProgramUpdate] XML 설정 다운로드: {url}");

                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    LogService.Instance.Log($"[ProgramUpdate] 설정 다운로드 실패: HTTP {(int)response.StatusCode}");
                    return false;
                }

                var xml = await response.Content.ReadAsStringAsync();
                _config = ProgramUpdateConfig.LoadFromXml(xml);

                LogService.Instance.Log($"[ProgramUpdate] 설정 로드 완료: {_config.Programs.Count}개 프로그램");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ProgramUpdate] 설정 로드 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 모든 프로그램 업데이트 확인 및 실행
        /// </summary>
        public async Task CheckAndUpdateAllAsync()
        {
            if (_config == null || _config.Programs.Count == 0)
            {
                if (!await LoadConfigAsync()) return;
            }

            foreach (var program in _config.Programs)
            {
                await ProcessProgramAsync(program);
            }
        }

        /// <summary>
        /// 특정 프로그램 ID로 업데이트 실행
        /// </summary>
        public async Task<bool> ProcessProgramByIdAsync(string programId)
        {
            if (_config == null) await LoadConfigAsync();

            var program = _config?.Programs.FirstOrDefault(p => p.Id == programId);
            if (program == null)
            {
                LogService.Instance.Log($"[ProgramUpdate] 프로그램 ID '{programId}'를 찾을 수 없음");
                return false;
            }

            return await ProcessProgramAsync(program);
        }

        /// <summary>
        /// 프로그램 처리 (다운로드, 설치, 실행)
        /// </summary>
        private async Task<bool> ProcessProgramAsync(ProgramInfo program)
        {
            try
            {
                LogService.Instance.Log($"[ProgramUpdate] 프로그램 처리 시작: {program.Title} (v{program.Version})");

                // 파일 체크 (이미 설치되어 있는지 확인)
                if (!string.IsNullOrEmpty(program.FileCheck))
                {
                    if (File.Exists(program.FileCheck))
                    {
                        LogService.Instance.Log($"[ProgramUpdate] 이미 설치됨: {program.FileCheck}");
                        return true;
                    }
                }

                // 각 파일 다운로드
                foreach (var file in program.Files)
                {
                    var downloaded = await DownloadFileAsync(file);
                    if (!downloaded)
                    {
                        LogService.Instance.Log($"[ProgramUpdate] 파일 다운로드 실패: {file.Filename}");
                        return false;
                    }
                }

                // 실행
                if (program.Execute != null)
                {
                    ExecuteProgram(program);
                }

                LogService.Instance.Log($"[ProgramUpdate] 프로그램 처리 완료: {program.Title}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ProgramUpdate] 프로그램 처리 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 파일 다운로드
        /// </summary>
        private async Task<bool> DownloadFileAsync(Models.FileInfo file)
        {
            try
            {
                if (file.Download == null || string.IsNullOrEmpty(file.Download.Url))
                {
                    LogService.Instance.Log($"[ProgramUpdate] 다운로드 URL 없음: {file.Id}");
                    return false;
                }

                var folder = file.GetResolvedFolder();
                var fullPath = file.GetFullPath();

                // 폴더 생성
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    LogService.Instance.Log($"[ProgramUpdate] 폴더 생성: {folder}");
                }

                LogService.Instance.Log($"[ProgramUpdate] 다운로드 시작: {file.Download.Url}");

                var response = await _http.GetAsync(file.Download.Url);
                if (!response.IsSuccessStatusCode)
                {
                    LogService.Instance.Log($"[ProgramUpdate] 다운로드 실패: HTTP {(int)response.StatusCode}");
                    return false;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();

                // 최소 크기 체크
                if (file.Download.MinSize > 0 && bytes.Length < file.Download.MinSize)
                {
                    LogService.Instance.Log($"[ProgramUpdate] 파일 크기 부족: {bytes.Length} < {file.Download.MinSize}");
                    return false;
                }

                // 압축 해제 여부
                if (file.Download.ShouldExtract)
                {
                    await ExtractZipAsync(bytes, folder);
                    LogService.Instance.Log($"[ProgramUpdate] 압축 해제 완료: {folder}");
                }
                else
                {
                    File.WriteAllBytes(fullPath, bytes);
                    LogService.Instance.Log($"[ProgramUpdate] 파일 저장 완료: {fullPath}");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ProgramUpdate] 다운로드 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ZIP 압축 해제
        /// </summary>
        private async Task ExtractZipAsync(byte[] zipBytes, string extractPath)
        {
            await Task.Run(() =>
            {
                using (var ms = new MemoryStream(zipBytes))
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        var destPath = Path.Combine(extractPath, entry.FullName);
                        var destDir = Path.GetDirectoryName(destPath);

                        if (!Directory.Exists(destDir))
                            Directory.CreateDirectory(destDir);

                        if (!string.IsNullOrEmpty(entry.Name))
                        {
                            entry.ExtractToFile(destPath, overwrite: true);
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 프로그램 실행
        /// </summary>
        private void ExecuteProgram(ProgramInfo program)
        {
            try
            {
                if (program.Execute == null) return;

                // fileid로 파일 찾기
                var file = program.Files.FirstOrDefault(f => f.Id == program.Execute.FileId);
                if (file == null)
                {
                    LogService.Instance.Log($"[ProgramUpdate] 실행 파일을 찾을 수 없음: {program.Execute.FileId}");
                    return;
                }

                var exePath = file.GetFullPath();
                if (!File.Exists(exePath))
                {
                    LogService.Instance.Log($"[ProgramUpdate] 실행 파일이 존재하지 않음: {exePath}");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = program.Execute.CommandLine ?? "",
                    WorkingDirectory = string.IsNullOrEmpty(program.Execute.WorkingDirectory)
                        ? file.GetResolvedFolder()
                        : program.Execute.WorkingDirectory,
                    UseShellExecute = true
                };

                LogService.Instance.Log($"[ProgramUpdate] 프로그램 실행: {exePath} {startInfo.Arguments}");
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[ProgramUpdate] 실행 오류: {ex.Message}");
            }
        }
    }
}
