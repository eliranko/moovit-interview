using System;
using System.Collections.Generic;
namespace moovit_interview
{
    public interface IPoller : IDisposable
    {
        void Init(PollerConfig config);
        List<LineEta> GetStopArrivals(int stopId);
    }
}