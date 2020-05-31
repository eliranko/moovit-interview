using System.Collections.Generic;
namespace moovit_interview
{
    public interface INextBusProvider
    {
        List<StopEta> GetLineEta(string lineNumber);
        List<StopInterval> GetLineIntervals(string lineNumber);
    }
}