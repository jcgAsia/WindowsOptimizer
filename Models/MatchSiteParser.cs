using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;

namespace WindowsOptimizer.Models
{
    public class MatchSiteInfo
    {
        public string Category { get; set; }
        public string Match { get; set; }
        public string QueryParameter { get; set; }
        public bool CheckDefault { get; set; }
        public string SiteName { get; set; }

        public override string ToString() => $"{SiteName} ({Category}) - {Match}";
    }

    public class MatchResult
    {
        public bool IsMatched { get; set; }
        public MatchSiteInfo MatchedSite { get; set; }
        public string QueryParameterName { get; set; }
        public string QueryParameterValue { get; set; }

        public MatchResult()
        {
            IsMatched = false;
        }

        public MatchResult(MatchSiteInfo site, string param, string value)
        {
            IsMatched = true;
            MatchedSite = site;
            QueryParameterName = param;
            QueryParameterValue = value;
        }
    }

    public class MatchSiteParser
    {
        private readonly List<MatchSiteInfo> _sites = new List<MatchSiteInfo>();
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public int Count => _sites.Count;

        public bool LoadFromUrl(string url)
        {
            try
            {
                var content = _http.GetStringAsync(url).Result;
                return ParseXml(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchSiteParser] 로딩 실패: {ex.Message}");
                return false;
            }
        }

        public bool ParseXml(string xml)
        {
            try
            {
                _sites.Clear();
                var doc = XDocument.Parse(xml);
                var root = doc.Element("matchsitelist");
                if (root == null) return false;

                foreach (var kw in root.Elements("keyword"))
                {
                    var site = new MatchSiteInfo
                    {
                        Category = kw.Attribute("categori")?.Value ?? "",
                        Match = kw.Attribute("match")?.Value ?? "",
                        QueryParameter = kw.Attribute("q")?.Value ?? "",
                        CheckDefault = kw.Attribute("checkdefault")?.Value?.ToLower() == "yes",
                        SiteName = kw.Value?.Trim() ?? ""
                    };

                    if (!string.IsNullOrEmpty(site.Match))
                        _sites.Add(site);
                }

                return _sites.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchSiteParser] 파싱 오류: {ex.Message}");
                return false;
            }
        }

        public MatchResult FindMatch(string url)
        {
            if (string.IsNullOrEmpty(url)) return new MatchResult();

            var normalized = url.ToLower().Trim();
            foreach (var site in _sites)
            {
                if (IsUrlMatched(normalized, site.Match.ToLower()))
                {
                    var value = ExtractQueryValue(url, site.QueryParameter);
                    return new MatchResult(site, site.QueryParameter, value);
                }
            }
            return new MatchResult();
        }

        private bool IsUrlMatched(string url, string pattern)
        {
            if (url.StartsWith(pattern)) return true;

            // http/https 변환 체크
            if (pattern.StartsWith("http://"))
            {
                var https = pattern.Replace("http://", "https://");
                if (url.StartsWith(https)) return true;
                var noScheme = pattern.Replace("http://", "");
                if (url.Contains(noScheme)) return true;
            }
            else if (pattern.StartsWith("https://"))
            {
                var http = pattern.Replace("https://", "http://");
                if (url.StartsWith(http)) return true;
            }

            // www 유무 체크
            if (pattern.Contains("://www."))
            {
                var noWww = pattern.Replace("://www.", "://");
                if (url.StartsWith(noWww)) return true;
            }
            else if (pattern.Contains("://"))
            {
                var withWww = pattern.Replace("://", "://www.");
                if (url.StartsWith(withWww)) return true;
            }

            return false;
        }

        private string ExtractQueryValue(string url, string param)
        {
            try
            {
                if (string.IsNullOrEmpty(param)) return "";
                if (!url.StartsWith("http")) url = "http://" + url;

                var uri = new Uri(url);
                var query = uri.Query.TrimStart('?');
                foreach (var pair in query.Split('&'))
                {
                    var kv = pair.Split('=');
                    if (kv.Length == 2 && kv[0].Equals(param, StringComparison.OrdinalIgnoreCase))
                        return WebUtility.UrlDecode(kv[1]);
                }
            }
            catch { }
            return "";
        }

        public List<MatchSiteInfo> GetAllSites() => new List<MatchSiteInfo>(_sites);
    }
}
