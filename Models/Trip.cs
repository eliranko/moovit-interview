using System;
namespace moovit_interview
{
    public class Trip
    {
        public uint Id { get; private set; }
        public int NextStopId { get; set; }
        public int ExpectedSecondsToArriveAtNextStop { get; set; }
        public DateTime LastSampling { get; set; }

        public Trip(uint id, int nextStopId, int expectedSecondsToNextStop, DateTime sampling)
        {
            Id = id;
            NextStopId = nextStopId;
            ExpectedSecondsToArriveAtNextStop = expectedSecondsToNextStop;
            LastSampling = sampling;
        }

        public Trip(Trip trip)
        {
            this.Id = trip.Id;
            this.NextStopId = trip.NextStopId;
            this.LastSampling = trip.LastSampling;
            this.ExpectedSecondsToArriveAtNextStop = trip.ExpectedSecondsToArriveAtNextStop;
        }
    }
}