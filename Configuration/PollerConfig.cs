using System.Collections.Generic;
namespace moovit_interview
{
    public class PollerConfig
    {
        public INextBusProvider Provider { get; set; }
        public List<string> LineNumbers { get; set; }
        public int PollIntervalSeconds { get; set; }
        public int MaxConcurrency { get; set; }
    }
}