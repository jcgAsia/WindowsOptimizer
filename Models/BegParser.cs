using System;
using System.Net.Http;
using System.Xml.Linq;

namespace WindowsOptimizer.Models
{
    public class BegParser
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public string MatchlistUrl { get; set; }
        public string UrlMatchlistUrl { get; set; }

        // KeyMatch 설정
        public string KeyMatchSwitch { get; set; }
        public string KeyMatchPopType { get; set; }
        public string KeyMatchFreqMaxPerDay { get; set; }
        public string KeyMatchFreqDterm { get; set; }
        public string KeyMatchFreqDelay { get; set; }
        public string KeyMatchFreqDcount { get; set; }
        public string KeyMatchQueryUrl { get; set; }
        public string KeyMatchPopDelaytime { get; set; }

        // UrlMatch 설정
        public string UrlMatchSwitch { get; set; }
        public string UrlMatchPopType { get; set; }
        public string UrlMatchFreqMaxPerDay { get; set; }
        public string UrlMatchFreqDterm { get; set; }
        public string UrlMatchFreqDelay { get; set; }
        public string UrlMatchFreqDcount { get; set; }
        public string UrlMatchQueryUrl { get; set; }
        public string UrlMatchPopDelaytime { get; set; }

        // Bacon 설정
        public string BaconSwitch { get; set; }
        public string BaconQueryUrl { get; set; }

        public bool LoadFromHttp(string xmlUrl)
        {
            try
            {
                var content = _http.GetStringAsync(xmlUrl).Result;
                return ParseXmlContent(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BegParser] 로딩 오류: {ex.Message}");
                return false;
            }
        }

        public bool LoadFromString()
        {
            string xml = @"
<info>
    <matchlist url=""http://api.weaping.co.kr/ssi/matchsitelist.php?pid=%CLIENTID&amp;cid=%MACADDR"" />
    <urlmatchlist url=""http://api.weaping.co.kr/ssi/urlmatchlist.php?pid=%CLIENTID&amp;cid=%MACADDR"" />
    
    <keymatch switch=""on"" poptype=""tab"">
        <freq maxperday=""100"" dterm=""3600"" delay=""60"" dcount=""10"" />
        <query url=""http://api.weaping.co.kr/ssi/query.php?keyword_org=%KEYWORD_ORG&amp;keyword=%KEYWORD&amp;pid=%CLIENTID&amp;s_engine=%SENGINE&amp;cid=%MACADDR"" />
        <pop delaytime=""5"" />
    </keymatch>
    
    <urlmatch switch=""on"" poptype=""new"">
        <freq maxperday=""100"" dterm=""3600"" delay=""60"" dcount=""10"" />
        <query url=""http://api.weaping.co.kr/ssi/query_urlmatch.php?url=%URL&amp;pid=%CLIENTID&amp;cid=%MACADDR"" />
        <pop delaytime=""0"" />
    </urlmatch>
    
    <bacon switch=""on"">
        <query url=""http://api.weaping.co.kr/ssi/ici.php?pid=%CLIENTID&amp;cid=%MACADDR"" />
    </bacon>
</info>";
            return ParseXmlContent(xml);
        }

        public bool ParseXmlContent(string xmlContent)
        {
            try
            {
                var doc = XDocument.Parse(xmlContent);
                var info = doc.Element("info");
                if (info == null) return false;

                MatchlistUrl = info.Element("matchlist")?.Attribute("url")?.Value;
                UrlMatchlistUrl = info.Element("urlmatchlist")?.Attribute("url")?.Value;

                // keymatch
                var keymatch = info.Element("keymatch");
                if (keymatch != null)
                {
                    KeyMatchSwitch = keymatch.Attribute("switch")?.Value;
                    KeyMatchPopType = keymatch.Attribute("poptype")?.Value ?? "tab";
                    KeyMatchFreqMaxPerDay = keymatch.Element("freq")?.Attribute("maxperday")?.Value;
                    KeyMatchFreqDterm = keymatch.Element("freq")?.Attribute("dterm")?.Value;
                    KeyMatchFreqDelay = keymatch.Element("freq")?.Attribute("delay")?.Value;
                    KeyMatchFreqDcount = keymatch.Element("freq")?.Attribute("dcount")?.Value;
                    KeyMatchQueryUrl = keymatch.Element("query")?.Attribute("url")?.Value;
                    KeyMatchPopDelaytime = keymatch.Element("pop")?.Attribute("delaytime")?.Value;
                }

                // urlmatch
                var urlmatch = info.Element("urlmatch");
                if (urlmatch != null)
                {
                    UrlMatchSwitch = urlmatch.Attribute("switch")?.Value;
                    UrlMatchPopType = urlmatch.Attribute("poptype")?.Value ?? "new";
                    UrlMatchFreqMaxPerDay = urlmatch.Element("freq")?.Attribute("maxperday")?.Value;
                    UrlMatchFreqDterm = urlmatch.Element("freq")?.Attribute("dterm")?.Value;
                    UrlMatchFreqDelay = urlmatch.Element("freq")?.Attribute("delay")?.Value;
                    UrlMatchFreqDcount = urlmatch.Element("freq")?.Attribute("dcount")?.Value;
                    UrlMatchQueryUrl = urlmatch.Element("query")?.Attribute("url")?.Value;
                    UrlMatchPopDelaytime = urlmatch.Element("pop")?.Attribute("delaytime")?.Value;
                }

                // bacon
                var bacon = info.Element("bacon");
                if (bacon != null)
                {
                    BaconSwitch = bacon.Attribute("switch")?.Value;
                    BaconQueryUrl = bacon.Element("query")?.Attribute("url")?.Value;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BegParser] 파싱 오류: {ex.Message}");
                return false;
            }
        }
    }
}
