using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace WindowsOptimizer.Services
{
    public class RegistryService
    {
        private static readonly Lazy<RegistryService> _instance = new Lazy<RegistryService>(() => new RegistryService());
        public static RegistryService Instance => _instance.Value;

        private const string AppName = "WindowsOptimizer";
        private const string DisplayName = "Windows System Optimizer";
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName;
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        private string ExePath => Assembly.GetExecutingAssembly().Location;
        private string InstallDir => Path.GetDirectoryName(ExePath);

        private RegistryService() { }

        public void RegisterStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    key?.SetValue(AppName, $"\"{ExePath}\"");
                }
                LogService.Instance.Log("시작프로그램 등록 완료");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"시작프로그램 등록 실패: {ex.Message}");
            }
        }

        public void UnregisterStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    key?.DeleteValue(AppName, false);
                }
            }
            catch { }
        }

        public void RegisterUninstaller()
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(UninstallKey))
                {
                    if (key == null) return;

                    var version = Assembly.GetExecutingAssembly().GetName().Version;
                    var fileInfo = new FileInfo(ExePath);

                    key.SetValue("DisplayName", DisplayName);
                    key.SetValue("DisplayVersion", version?.ToString() ?? "1.0.0");
                    key.SetValue("Publisher", "JCG");
                    key.SetValue("InstallLocation", InstallDir);
                    key.SetValue("UninstallString", $"\"{Path.Combine(InstallDir, "uninstall.exe")}\"");
                    key.SetValue("DisplayIcon", ExePath);
                    key.SetValue("EstimatedSize", (int)(fileInfo.Length / 1024));
                    key.SetValue("NoModify", 1);
                    key.SetValue("NoRepair", 1);
                }
                LogService.Instance.Log("제어판 등록 완료");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"제어판 등록 실패: {ex.Message}");
            }
        }

        public void UnregisterUninstaller()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKey(UninstallKey, false);
            }
            catch { }
        }

        public void SetValue(string name, object value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(GlobalConfig.RegSubKey))
                {
                    key?.SetValue(name, value);
                }
            }
            catch { }
        }

        public T GetValue<T>(string name, T defaultValue = default)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(GlobalConfig.RegSubKey))
                {
                    var value = key?.GetValue(name);
                    if (value != null) return (T)Convert.ChangeType(value, typeof(T));
                }
            }
            catch { }
            return defaultValue;
        }
    }
}
