using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Xml.Linq;
using WindowsOptimizer.Services;

namespace WindowsOptimizer.Models
{
    public class UrlMatchItem
    {
        public string Pattern { get; set; }
        public string ExtraDelay { get; set; }
        public string MaxDayCount { get; set; }
        public FrequencyLimiter Limiter { get; private set; }

        public UrlMatchItem(string pattern, string extraDelay = "", string maxDayCount = "")
        {
            Pattern = pattern ?? "";
            ExtraDelay = extraDelay ?? "";
            MaxDayCount = maxDayCount ?? "";

            if (!string.IsNullOrWhiteSpace(ExtraDelay) && !string.IsNullOrWhiteSpace(MaxDayCount))
            {
                int.TryParse(MaxDayCount, out int max);
                Limiter = new FrequencyLimiter(max > 0 ? max : 10, 10, 1, 10);
            }
        }

        public bool CanExtra() => Limiter?.CanWork() ?? false;
        public void ExtraCount() => Limiter?.AddTheCount();
        public int GetExtraTodayCount() => Limiter?.GetTodayWorkCount() ?? 0;
    }

    public class UrlMatchParser
    {
        private readonly List<UrlMatchItem> _items = new List<UrlMatchItem>();
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public int Count => _items.Count;

        public bool LoadFromUrl(string url)
        {
            try
            {
                var content = _http.GetStringAsync(url).Result;
                return LoadFromXml(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UrlMatchParser] 로딩 실패: {ex.Message}");
                return false;
            }
        }

        public bool LoadFromXml(string xml)
        {
            try
            {
                _items.Clear();
                var doc = XDocument.Parse(xml);
                var root = doc.Element("urlmatchlist");
                if (root == null) return false;

                foreach (var el in root.Elements("u"))
                {
                    var m = el.Attribute("m")?.Value;
                    if (string.IsNullOrWhiteSpace(m)) continue;

                    var item = new UrlMatchItem(
                        m.Trim(),
                        el.Attribute("extradelay")?.Value?.Trim(),
                        el.Attribute("maxdaycount")?.Value?.Trim()
                    );

                    if (!_items.Any(x => x.Pattern == item.Pattern))
                        _items.Add(item);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UrlMatchParser] 파싱 오류: {ex.Message}");
                return false;
            }
        }

        public bool IsMatch(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return _items.Any(item => url.Contains(item.Pattern));
        }

        public List<UrlMatchItem> GetMatchedItems(string url)
        {
            if (string.IsNullOrEmpty(url)) return new List<UrlMatchItem>();
            return _items.Where(item => url.Contains(item.Pattern)).ToList();
        }

        public List<UrlMatchItem> GetAllItems() => new List<UrlMatchItem>(_items);
    }
}
