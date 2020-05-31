namespace moovit_interview
{
    public class StopInterval
    {
        public int ToStopId { get; private set; }
        public int IntervalSeconds { get; private set; }

        public StopInterval(int toStopId, int intervalSeconds)
        {
            ToStopId = toStopId;
            IntervalSeconds = IntervalSeconds;
        }
    }
}