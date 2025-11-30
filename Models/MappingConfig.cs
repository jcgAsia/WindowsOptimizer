using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Xml.Serialization;
using WindowsOptimizer.Services;

namespace WindowsOptimizer.Models
{
    /// <summary>
    /// PlanB 기술문서 5.2 로컬 매핑 파일 구조
    /// </summary>
    [XmlRoot("mappings")]
    public class MappingConfig
    {
        [XmlElement("map")]
        public List<DomainMapping> Mappings { get; set; } = new List<DomainMapping>();

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// 서버에서 mapping.xml 다운로드
        /// </summary>
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

        /// <summary>
        /// 로컬 파일에서 로드
        /// </summary>
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

        /// <summary>
        /// XML 문자열에서 파싱
        /// </summary>
        public static MappingConfig LoadFromXml(string xml)
        {
            var serializer = new XmlSerializer(typeof(MappingConfig));
            using (var reader = new StringReader(xml))
            {
                return (MappingConfig)serializer.Deserialize(reader);
            }
        }

        /// <summary>
        /// 파일로 저장
        /// </summary>
        public void SaveToFile(string path)
        {
            var serializer = new XmlSerializer(typeof(MappingConfig));
            using (var writer = new StreamWriter(path))
            {
                serializer.Serialize(writer, this);
            }
        }

        /// <summary>
        /// 샘플 설정 생성
        /// </summary>
        public static MappingConfig CreateSample()
        {
            return new MappingConfig
            {
                Mappings = new List<DomainMapping>
                {
                    new DomainMapping
                    {
                        Trigger = "abc.com",
                        Target = "https://ad.example.com/campaign",
                        Frequency = 30
                    },
                    new DomainMapping
                    {
                        Trigger = "xyz.com",
                        Target = "https://ad.example.com/special",
                        Frequency = 60
                    }
                }
            };
        }
    }

    /// <summary>
    /// 도메인 매핑 항목
    /// </summary>
    public class DomainMapping
    {
        /// <summary>
        /// 모니터링할 루트 도메인 (예: abc.com)
        /// </summary>
        [XmlElement("trigger")]
        public string Trigger { get; set; }

        /// <summary>
        /// 새 탭으로 열 광고 URL
        /// </summary>
        [XmlElement("target")]
        public string Target { get; set; }

        /// <summary>
        /// 동일 도메인 재작동 최소 간격 (분 단위)
        /// </summary>
        [XmlElement("frequency")]
        public int Frequency { get; set; }

        /// <summary>
        /// 마지막 트리거 시간
        /// </summary>
        [XmlIgnore]
        public DateTime LastTriggered { get; set; } = DateTime.MinValue;

        /// <summary>
        /// 트리거 가능 여부 확인
        /// </summary>
        public bool CanTrigger()
        {
            return (DateTime.Now - LastTriggered).TotalMinutes >= Frequency;
        }

        /// <summary>
        /// 트리거 기록
        /// </summary>
        public void MarkTriggered()
        {
            LastTriggered = DateTime.Now;
        }

        public override string ToString()
        {
            return $"{Trigger} → {Target} ({Frequency}분)";
        }
    }
}
