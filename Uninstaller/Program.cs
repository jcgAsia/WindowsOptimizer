using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace WindowsOptimizer.Uninstaller
{
    /// <summary>
    /// PlanB 기술문서 2.2 - 제거 프로그램
    /// </summary>
    class Program
    {
        private const string AppName = "WindowsOptimizer";
        private const string DisplayName = "Windows System Optimizer";
        private const string RegUninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string RegRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        static int Main(string[] args)
        {
            Console.WriteLine($"=== {DisplayName} 제거 프로그램 ===");
            Console.WriteLine();

            bool silent = Array.Exists(args, a => a.ToLower() == "/silent" || a.ToLower() == "-silent");

            if (!silent)
            {
                Console.Write("프로그램을 제거하시겠습니까? (Y/N): ");
                var key = Console.ReadKey();
                Console.WriteLine();

                if (key.Key != ConsoleKey.Y)
                {
                    Console.WriteLine("취소되었습니다.");
                    return 1;
                }
            }

            try
            {
                // 1. 프로세스 종료
                Console.WriteLine("[1/4] 실행 중인 프로세스 종료...");
                KillProcess(AppName);

                // 2. 시작프로그램 제거
                Console.WriteLine("[2/4] 시작프로그램 레지스트리 제거...");
                RemoveStartupRegistry();

                // 3. 제어판 등록 제거
                Console.WriteLine("[3/4] 제어판 등록 제거...");
                RemoveUninstallRegistry();

                // 4. 프로그램 파일 삭제
                Console.WriteLine("[4/4] 프로그램 파일 삭제...");
                DeleteProgramFiles();

                Console.WriteLine();
                Console.WriteLine("제거가 완료되었습니다.");

                if (!silent)
                {
                    Console.WriteLine("아무 키나 누르면 종료합니다...");
                    Console.ReadKey();
                }

                // 자기 자신 삭제 (배치 파일 사용)
                ScheduleSelfDelete();

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"오류: {ex.Message}");
                if (!silent)
                {
                    Console.ReadKey();
                }
                return -1;
            }
        }

        static void KillProcess(string processName)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(processName))
                {
                    proc.Kill();
                    proc.WaitForExit(5000);
                    Console.WriteLine($"  프로세스 종료: {proc.Id}");
                }
            }
            catch { }
        }

        static void RemoveStartupRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegRunPath, true))
                {
                    if (key?.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName);
                        Console.WriteLine("  시작프로그램 레지스트리 삭제됨");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  경고: {ex.Message}");
            }
        }

        static void RemoveUninstallRegistry()
        {
            try
            {
                // HKLM 시도
                using (var key = Registry.LocalMachine.OpenSubKey(RegUninstallPath, true))
                {
                    if (key?.OpenSubKey(AppName) != null)
                    {
                        key.DeleteSubKeyTree(AppName);
                        Console.WriteLine("  제어판 등록 삭제됨 (HKLM)");
                        return;
                    }
                }

                // HKCU 시도
                using (var key = Registry.CurrentUser.OpenSubKey(RegUninstallPath, true))
                {
                    if (key?.OpenSubKey(AppName) != null)
                    {
                        key.DeleteSubKeyTree(AppName);
                        Console.WriteLine("  제어판 등록 삭제됨 (HKCU)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  경고: {ex.Message}");
            }
        }

        static void DeleteProgramFiles()
        {
            try
            {
                // Program Files 경로
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var appFolder = Path.Combine(programFiles, AppName);

                if (Directory.Exists(appFolder))
                {
                    // 파일 삭제
                    foreach (var file in Directory.GetFiles(appFolder, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch { }
                    }

                    // 폴더 삭제
                    try { Directory.Delete(appFolder, true); }
                    catch { }

                    Console.WriteLine($"  프로그램 폴더 삭제: {appFolder}");
                }

                // LocalAppData 삭제
                var localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppName);

                if (Directory.Exists(localAppData))
                {
                    try { Directory.Delete(localAppData, true); }
                    catch { }
                    Console.WriteLine($"  데이터 폴더 삭제: {localAppData}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  경고: {ex.Message}");
            }
        }

        static void ScheduleSelfDelete()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule.FileName;
                var batPath = Path.Combine(Path.GetTempPath(), "uninstall_cleanup.bat");

                var batContent = $@"
@echo off
:loop
del /f /q ""{exePath}""
if exist ""{exePath}"" goto loop
rmdir /s /q ""{Path.GetDirectoryName(exePath)}""
del /f /q ""%~f0""
";
                File.WriteAllText(batPath, batContent);

                var psi = new ProcessStartInfo
                {
                    FileName = batPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch { }
        }
    }
}
