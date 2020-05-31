using System;
using System.Collections.Generic;
namespace moovit_interview
{
    public interface IMatcher : IDisposable
    {
        void Init(PollerConfig config);
        void UpdateLineEtas(string line, List<StopEta> stops);
    }
}