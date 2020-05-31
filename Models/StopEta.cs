using System;
namespace moovit_interview
{
    public class StopEta
    {
        public int StopId { get; private set; }
        public DateTime Eta { get; private set; }

        public StopEta(int stopId, DateTime eta)
        {
            StopId = stopId;
            Eta = eta;
        }
    }
}