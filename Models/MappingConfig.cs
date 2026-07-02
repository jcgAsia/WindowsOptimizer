using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Xml.Serialization;
using WindowsOptimizer.Services;

namespace WindowsOptimizer.Models
{
    [XmlRoot("config")]
    public class MappingConfig
    {
        // 전역 제어
        [XmlElement("forcedown")]
        public string ForceDown { get; set; } = "off";

        // 탭브라우저 기능
        [XmlElement("autotab")]
        public string AutoTab { get; set; } = "off";

        [XmlElement("autotab_cycletime")]
        public int AutoTabCycleTime { get; set; } = 0;

        // 히든윈도우 기능
        [XmlElement("openhd")]
        public string OpenHd { get; set; } = "off";

        [XmlElement("openhd_delaytime")]
        public int OpenHdDelayTime { get; set; } = 0;

        [XmlElement("openhd_closetime")]
        public int OpenHdCloseTime { get; set; } = 10;

        [XmlElement("openhd_cycletime")]
        public int OpenHdCycleTime { get; set; } = 0;

        // 키워드 맵핑 기능
        [XmlElement("keymapping")]
        public string KeyMapping { get; set; } = "off";

        // 토스트 팝업 기능
        [XmlElement("toast")]
        public string Toast { get; set; } = "on";

        [XmlElement("toast_maxcount")]
        public int ToastMaxCount { get; set; } = 3;

        [XmlElement("toast_url")]
        public string ToastUrl { get; set; } = "https://www.toastpop.net/CARD/card.php";

        [XmlElement("mappings")]
        public MappingList MappingList { get; set; } = new MappingList();

        // 편의 프로퍼티
        [XmlIgnore] public bool IsForceDown => ForceDown?.ToLower() == "on";
        [XmlIgnore] public bool IsAutoTabEnabled => AutoTab?.ToLower() == "on";
        [XmlIgnore] public bool IsOpenHdEnabled => OpenHd?.ToLower() == "on";

        [XmlIgnore] public bool IsKeyMappingEnabled => KeyMapping?.ToLower() == "on";
        [XmlIgnore] public bool IsToastEnabled => Toast?.ToLower() == "on";
        [XmlIgnore] public List<DomainMapping> Mappings => MappingList?.Maps ?? new List<DomainMapping>();
        [XmlIgnore] public List<KeywordMapping> KeyMappings => MappingList?.KeyMaps ?? new List<KeywordMapping>();

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
                ForceDown = "off",
                AutoTab = "on",
                AutoTabCycleTime = 1800,
                OpenHd = "on",
                OpenHdDelayTime = 180,
                OpenHdCloseTime = 10,
                OpenHdCycleTime = 1800,
                Toast = "on",
                ToastMaxCount = 3,
                ToastUrl = "https://www.toastpop.net/CARD/card.php",
                KeyMapping = "on",
                MappingList = new MappingList
                {
                    Maps = new List<DomainMapping>
                    {
                        new DomainMapping { Trigger = "search.naver.com", Target = "https://example.com/ad1", Frequency = 2 },
                        new DomainMapping { Trigger = "search.daum.net", Target = "https://example.com/ad2", Frequency = 3 }
                    },
                    KeyMaps = new List<KeywordMapping>
                    {
                        new KeywordMapping { Keywords = "sample keyword,test", Target = "https://example.com/kw1", Frequency = 2 }
                    }
                }
            };
        }
    }

    public class MappingList
    {
        [XmlElement("map")]
        public List<DomainMapping> Maps { get; set; } = new List<DomainMapping>();

        [XmlElement("keymap")]
        public List<KeywordMapping> KeyMaps { get; set; } = new List<KeywordMapping>();
    }

    public class DomainMapping
    {
        [XmlElement("trigger")]
        public string Trigger { get; set; }

        [XmlElement("target")]
        public string Target { get; set; }

        [XmlElement("frequency")]
        public int Frequency { get; set; }

        // AutoTab 추적
        [XmlIgnore] public int AutoTabCount { get; set; } = 0;
        [XmlIgnore] public DateTime AutoTabLastTime { get; set; } = DateTime.MinValue;

        // OpenHd 추적
        [XmlIgnore] public int OpenHdCount { get; set; } = 0;
        [XmlIgnore] public DateTime OpenHdLastTime { get; set; } = DateTime.MinValue;

        public bool CanTriggerAutoTab(int cycleTimeSec)
        {
            if (AutoTabCount >= Frequency) return false;
            if (cycleTimeSec > 0 && (DateTime.Now - AutoTabLastTime).TotalSeconds < cycleTimeSec) return false;
            return true;
        }

        public bool CanTriggerOpenHd(int cycleTimeSec)
        {
            if (OpenHdCount >= Frequency) return false;
            if (cycleTimeSec > 0 && (DateTime.Now - OpenHdLastTime).TotalSeconds < cycleTimeSec) return false;
            return true;
        }

        public void MarkAutoTabTriggered()
        {
            AutoTabCount++;
            AutoTabLastTime = DateTime.Now;
        }

        public void MarkOpenHdTriggered()
        {
            OpenHdCount++;
            OpenHdLastTime = DateTime.Now;
        }

        public override string ToString() => $"{Trigger} → {Target} (횟수:{Frequency})";
    }

    public class KeywordMapping
    {
        [XmlElement("keywords")]
        public string Keywords { get; set; }

        [XmlElement("target")]
        public string Target { get; set; }

        [XmlElement("frequency")]
        public int Frequency { get; set; }

        [XmlIgnore] public int AutoTabCount { get; set; } = 0;
        [XmlIgnore] public DateTime AutoTabLastTime { get; set; } = DateTime.MinValue;

        [XmlIgnore]
        public string[] KeywordList => Keywords?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(k => k.Trim()).ToArray() ?? Array.Empty<string>();

        public override string ToString() => $"[{Keywords}] → {Target} (횟수:{Frequency})";
    }
}
