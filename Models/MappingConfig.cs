using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Xml.Serialization;
using WindowsOptimizer.Services;

namespace WindowsOptimizer.Models
{
    [XmlRoot("mappings")]
    public class MappingConfig
    {
        [XmlElement("map")]
        public List<DomainMapping> Mappings { get; set; } = new List<DomainMapping>();

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public static MappingConfig LoadFromUrl(string url)
        {
            try
            {
                var xml = _http.GetStringAsync(url).Result;
                return LoadFromXml(xml);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[MappingConfig] 서버 로드 실패: {ex.Message}");
                return null;
            }
        }

        public static MappingConfig LoadFromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var xml = File.ReadAllText(path);
                return LoadFromXml(xml);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log($"[MappingConfig] 파일 로드 실패: {ex.Message}");
                return null;
            }
        }

        public static MappingConfig LoadFromXml(string xml)
        {
            var serializer = new XmlSerializer(typeof(MappingConfig));
            using (var reader = new StringReader(xml))
            {
                return (MappingConfig)serializer.Deserialize(reader);
            }
        }

        public void SaveToFile(string path)
        {
            var serializer = new XmlSerializer(typeof(MappingConfig));
            using (var writer = new StreamWriter(path))
            {
                serializer.Serialize(writer, this);
            }
        }

        public static MappingConfig CreateSample()
        {
            return new MappingConfig
            {
                Mappings = new List<DomainMapping>
                {
                    new DomainMapping { Trigger = "naver.com", Target = "https://example.com/ad1", Frequency = 30 },
                    new DomainMapping { Trigger = "daum.net", Target = "https://example.com/ad2", Frequency = 60 }
                }
            };
        }
    }

    public class DomainMapping
    {
        [XmlElement("trigger")]
        public string Trigger { get; set; }

        [XmlElement("target")]
        public string Target { get; set; }

        [XmlElement("frequency")]
        public int Frequency { get; set; }

        [XmlIgnore]
        public DateTime LastTriggered { get; set; } = DateTime.MinValue;

        public bool CanTrigger() => (DateTime.Now - LastTriggered).TotalMinutes >= Frequency;

        public void MarkTriggered() => LastTriggered = DateTime.Now;

        public override string ToString() => $"{Trigger} → {Target} ({Frequency}분)";
    }
}
