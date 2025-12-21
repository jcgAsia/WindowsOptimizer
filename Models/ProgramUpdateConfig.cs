using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace WindowsOptimizer.Models
{
    /// <summary>
    /// XML 업데이트 설정 파일의 루트 요소
    /// URL: https://bustabcc.net/SWC/ups_read.php?client={clientId}
    /// </summary>
    [XmlRoot("programs")]
    public class ProgramUpdateConfig
    {
        [XmlElement("program")]
        public List<ProgramInfo> Programs { get; set; } = new List<ProgramInfo>();

        public static ProgramUpdateConfig LoadFromXml(string xml)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(ProgramUpdateConfig));
                using (var reader = new StringReader(xml))
                {
                    return (ProgramUpdateConfig)serializer.Deserialize(reader);
                }
            }
            catch
            {
                return new ProgramUpdateConfig();
            }
        }
    }

    /// <summary>
    /// 프로그램 정보
    /// <program id="etc" title="etc" version="2001" filecheck="">
    /// </summary>
    public class ProgramInfo
    {
        [XmlAttribute("id")]
        public string Id { get; set; }

        [XmlAttribute("title")]
        public string Title { get; set; }

        [XmlAttribute("version")]
        public string Version { get; set; }

        [XmlAttribute("filecheck")]
        public string FileCheck { get; set; }

        [XmlElement("file")]
        public List<FileInfo> Files { get; set; } = new List<FileInfo>();

        [XmlElement("execute")]
        public ExecuteInfo Execute { get; set; }
    }

    /// <summary>
    /// 파일 다운로드 정보
    /// <file id="f_etc" folder="%PROGRAMFILES/Windows NFC" filename="gomtest.exe">
    ///     <down url="exe다운로드url" extract="0" minsize="0"/>
    /// </file>
    /// </summary>
    public class FileInfo
    {
        [XmlAttribute("id")]
        public string Id { get; set; }

        [XmlAttribute("folder")]
        public string Folder { get; set; }

        [XmlAttribute("filename")]
        public string Filename { get; set; }

        [XmlElement("down")]
        public DownloadInfo Download { get; set; }

        /// <summary>
        /// 폴더 경로의 환경 변수를 실제 경로로 변환
        /// %PROGRAMFILES -> C:\Program Files
        /// </summary>
        public string GetResolvedFolder()
        {
            if (string.IsNullOrEmpty(Folder)) return string.Empty;

            var resolved = Folder.ToUpperInvariant();
            var original = Folder;

            // %PROGRAMFILES(X86) 처리 (먼저 처리해야 함)
            if (resolved.Contains("%PROGRAMFILES(X86)"))
            {
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                int idx = resolved.IndexOf("%PROGRAMFILES(X86)");
                original = original.Substring(0, idx) + programFilesX86 + original.Substring(idx + 18);
                resolved = original.ToUpperInvariant();
            }

            // %PROGRAMFILES 처리
            if (resolved.Contains("%PROGRAMFILES"))
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                int idx = resolved.IndexOf("%PROGRAMFILES");
                original = original.Substring(0, idx) + programFiles + original.Substring(idx + 13);
                resolved = original.ToUpperInvariant();
            }

            // %LOCALAPPDATA 처리
            if (resolved.Contains("%LOCALAPPDATA"))
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                int idx = resolved.IndexOf("%LOCALAPPDATA");
                original = original.Substring(0, idx) + localAppData + original.Substring(idx + 13);
                resolved = original.ToUpperInvariant();
            }

            // %APPDATA 처리
            if (resolved.Contains("%APPDATA"))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                int idx = resolved.IndexOf("%APPDATA");
                original = original.Substring(0, idx) + appData + original.Substring(idx + 8);
            }
            else
            {
                original = resolved.Contains("%") ? original : original;
            }

            // 슬래시를 백슬래시로 변환
            original = original.Replace("/", "\\");

            return original;
        }

        /// <summary>
        /// 전체 파일 경로 반환
        /// </summary>
        public string GetFullPath()
        {
            return Path.Combine(GetResolvedFolder(), Filename ?? string.Empty);
        }
    }

    /// <summary>
    /// 다운로드 정보
    /// <down url="exe다운로드url" extract="0" minsize="0"/>
    /// </summary>
    public class DownloadInfo
    {
        [XmlAttribute("url")]
        public string Url { get; set; }

        /// <summary>
        /// extract: 0=압축 해제 안함, 1=압축 해제
        /// </summary>
        [XmlAttribute("extract")]
        public int Extract { get; set; }

        /// <summary>
        /// 최소 파일 크기 (0=체크 안함)
        /// </summary>
        [XmlAttribute("minsize")]
        public long MinSize { get; set; }

        [XmlIgnore]
        public bool ShouldExtract => Extract == 1;
    }

    /// <summary>
    /// 실행 정보
    /// <execute fileid="f_etc" path="" cmd="/silent" workdir="" />
    /// </summary>
    public class ExecuteInfo
    {
        [XmlAttribute("fileid")]
        public string FileId { get; set; }

        [XmlAttribute("path")]
        public string Path { get; set; }

        [XmlAttribute("cmd")]
        public string CommandLine { get; set; }

        [XmlAttribute("workdir")]
        public string WorkingDirectory { get; set; }
    }
}
