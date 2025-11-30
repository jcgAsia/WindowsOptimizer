using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowsOptimizer.Services
{
    /// <summary>
    /// 빈도 제한 관리 클래스
    /// - maxPerDay: 하루 최대 횟수
    /// - dterm: 단위시간(초)
    /// - dcount: 단위시간 내 최대 횟수
    /// - delay: 동작 간 최소 간격(초)
    /// </summary>
    public class FrequencyLimiter
    {
        public int MaxPerDay { get; }
        public int TermSeconds { get; }
        public int TermCount { get; }
        public int DelaySeconds { get; }

        private readonly List<DateTime> _history = new List<DateTime>();
        private readonly object _lock = new object();

        public FrequencyLimiter(int maxPerDay, int termSeconds, int termCount, int delaySeconds)
        {
            MaxPerDay = maxPerDay;
            TermSeconds = termSeconds;
            TermCount = termCount;
            DelaySeconds = delaySeconds;
        }

        public bool CanWork()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                CleanupOld(now);

                if (!CheckDelay(now)) return false;
                if (!CheckDailyLimit(now)) return false;
                if (!CheckTermLimit(now)) return false;

                return true;
            }
        }

        public void AddTheCount(DateTime? time = null)
        {
            lock (_lock)
            {
                _history.Add(time ?? DateTime.Now);
            }
        }

        public int GetTodayWorkCount()
        {
            lock (_lock)
            {
                var today = DateTime.Now.Date;
                return _history.Count(x => x >= today && x < today.AddDays(1));
            }
        }

        public int GetCurrentTermWorkCount()
        {
            lock (_lock)
            {
                var start = DateTime.Now.AddSeconds(-TermSeconds);
                return _history.Count(x => x > start);
            }
        }

        public DateTime GetNextAvailableTime()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                CleanupOld(now);

                if (!CheckDailyLimit(now))
                    return now.Date.AddDays(1);

                if (!CheckTermLimit(now))
                {
                    var termStart = now.AddSeconds(-TermSeconds);
                    var termRecords = _history.Where(x => x > termStart).OrderBy(x => x).ToList();
                    if (termRecords.Count >= TermCount)
                        return termRecords[0].AddSeconds(TermSeconds + 1);
                }

                return now;
            }
        }

        public void ClearHistory()
        {
            lock (_lock) { _history.Clear(); }
        }

        public string GetConfiguration()
        {
            return $"MaxPerDay:{MaxPerDay}, DTerm:{TermSeconds}s, DCount:{TermCount}, Delay:{DelaySeconds}s";
        }

        public string GetStatus(string header = "")
        {
            lock (_lock)
            {
                int todayCount = GetTodayWorkCount();
                int termCount = GetCurrentTermWorkCount();
                bool canWork = CanWork();
                var nextTime = GetNextAvailableTime();

                return $"[{header}] 오늘:{todayCount}/{MaxPerDay}, 구간({TermSeconds}s):{termCount}/{TermCount}, 가능:{canWork}, 다음:{nextTime:HH:mm:ss}";
            }
        }

        private bool CheckDailyLimit(DateTime now)
        {
            var today = now.Date;
            return _history.Count(x => x >= today && x < today.AddDays(1)) < MaxPerDay;
        }

        private bool CheckTermLimit(DateTime now)
        {
            var start = now.AddSeconds(-TermSeconds);
            return _history.Count(x => x > start) < TermCount;
        }

        private bool CheckDelay(DateTime now)
        {
            if (_history.Count == 0) return true;
            return _history.Last() < now.AddSeconds(-DelaySeconds);
        }

        private void CleanupOld(DateTime now)
        {
            _history.RemoveAll(x => x < now.AddDays(-1));
        }
    }
}
