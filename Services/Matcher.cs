using System.Collections.Concurrent;
using System.Threading;
using System.Linq;
using System;
using System.Collections.Generic;
namespace moovit_interview
{
    public class Matcher : IMatcher
    {
        const int FinalStop = -1;

        private INextBusProvider _provider;
        // Holds mappings: line -> {stopId: Seconds}
        private IDictionary<string, IDictionary<int, int>> _stopsIntervalByLineDictionary;
        // Holds mappings: line -> list of stop intervals
        private IDictionary<string, List<StopInterval>> _stopsIntervalByLineList;
        // Holds mappings: line -> trips
        private IDictionary<string, List<Trip>> _tripsByLine;
        // Holds mappings: line -> next sequence
        private IDictionary<string, uint> _tripsSequenceByLine;
        private Thread _handlerThread;
        private BlockingCollection<Tuple<string, List<StopEta>>> _blockingCollection;
        private object _locker;

        public Matcher(INextBusProvider provider)
        {
            _provider = provider;
            _stopsIntervalByLineDictionary = new Dictionary<string, IDictionary<int, int>>();
            _stopsIntervalByLineList = new Dictionary<string, List<StopInterval>>();
            _tripsSequenceByLine = new Dictionary<string, uint>();
            _tripsByLine = new Dictionary<string, List<Trip>>();
            _blockingCollection = new BlockingCollection<Tuple<string, List<StopEta>>>();
            _locker = new object();
        }

        /**
        * Init fetches for each bus line its stops intervals and
        * saves this data in memory
        */
        public void Init(PollerConfig config)
        {
            foreach (var line in config.LineNumbers)
            {
                _tripsSequenceByLine[line] = 1;

                var stopsInterval = _provider.GetLineIntervals(line);
                _stopsIntervalByLineList[line] = stopsInterval;

                if (!_stopsIntervalByLineDictionary.ContainsKey(line))
                {
                    _stopsIntervalByLineDictionary[line] = new Dictionary<int, int>();
                }

                foreach (var stop in stopsInterval)
                {
                    _stopsIntervalByLineDictionary[line][stop.ToStopId] = stop.IntervalSeconds;
                }
            }

            SpawnHandlerThread();
        }

        /**
        * Adds the data to the blocking queue to be processed by the ruuning thread
        */
        public void UpdateLineEtas(string line, List<StopEta> stops)
        {
            _blockingCollection.Add(Tuple.Create(line, stops));
        }

        /**
        * Stops the thread
        */
        public void Dispose()
        {
            _blockingCollection.CompleteAdding();
        }

        /**
        * Spawn a thread to handle line etas
        */
        private void SpawnHandlerThread()
        {
            _handlerThread = new Thread(LineEtasHandler);
            _handlerThread.Start();
        }

        /**
        * this method handles line etas updates.
        * for each line update, we update all the trips of that lines and log
        * the updates to CSV
        */
        private void LineEtasHandler()
        {
            while (!_blockingCollection.IsCompleted)
            {
                Tuple<string, List<StopEta>> lineEtas;
                try
                {
                    // Take next line eta
                    lineEtas = _blockingCollection.Take();
                }
                catch (OperationCanceledException) // Blocking collection marked as completed. This thread's job is done
                {
                    return;
                }

                lock (_locker)  // Currently it isn't needed, but if there were multiple
                                // Handler we would need to synchronize them
                {
                    // Advance existing trips to the next stop if necessary
                    AdvanceTrips(lineEtas.Item1, lineEtas.Item2);
                    // Create new trips if necessary
                    CreateTrips(lineEtas.Item1, lineEtas.Item2);
                }
            }
        }

        /**
        * Advances the trips of the given line to their next stop if necessary.
        * If a trip has reached its detinstation, it is logged and removed 
        * from the trips of the given line
        */
        private void AdvanceTrips(string line, List<StopEta> eta)
        {
            if (!_tripsByLine.ContainsKey(line))
            {
                _tripsByLine[line] = new List<Trip>();
                return;
            }

            var updatedTrips = new List<Trip>();
            // iterate over the trips and advance each trip if necessary
            foreach (var trip in _tripsByLine[line])
            {
                // Make a clone of the current trip, if we changed the trip without
                // cloning it, there could be some side effects affecting this logic
                var tripCopy = new Trip(trip);

                var allAvailableStops = _stopsIntervalByLineList[line];
                // allAvailableStops gives an ordered list of the trip stops.
                // we retrieve the previously sampled stop of the trip and place it into
                // 'tripStopIndex' variable
                var tripStopIndex = allAvailableStops.FindIndex(stop => stop.ToStopId == trip.NextStopId);

                // If there're no trips heading to the current trip's stop, 
                // this means that this trip has already reached this stop
                // and needs to be advanced to the next stop
                //
                // see https://imgur.com/a/SGUd40I for more details
                if (!TripExistsHeadingToStop(eta, trip.NextStopId, _stopsIntervalByLineDictionary[line][trip.NextStopId]))
                {
                    if (AdvancedTripToNextStop(tripCopy, allAvailableStops, line))
                    {
                        updatedTrips.Add(tripCopy);
                    }

                    continue;
                }

                // If there're no trips heading to the stop following this trip's stop, that means this trip
                // is still heading to its current stop
                //
                // see https://imgur.com/a/rGhJg1y for more details
                if (tripStopIndex != allAvailableStops.Count - 1 &&
                 !TripExistsHeadingToStop(eta, tripStopIndex + 1, _stopsIntervalByLineDictionary[line][allAvailableStops[tripStopIndex + 1].ToStopId]))
                {
                    var now = DateTime.Now;
                    // Subtract expected seconds to reach station
                    tripCopy.ExpectedSecondsToArriveAtNextStop -= now.Subtract(trip.LastSampling).Seconds;
                    tripCopy.LastSampling = now;
                    updatedTrips.Add(tripCopy);

                    continue;
                }

                // if got here that means:
                // 1. there's a trip heading to the current trip's stop
                // 2. there's a trip heading to the stop following the current trip's stop
                // we will advance this trip to the next stop only if the following conditions hold:
                // 1. this is the closest trip to the current stop (there could be multiple trips heading to the stop)
                // 2. its expected seconds to the next stop are below zero
                var closest = !_tripsByLine[line].Any(t => t.NextStopId == trip.NextStopId
                    && t.ExpectedSecondsToArriveAtNextStop < trip.ExpectedSecondsToArriveAtNextStop);

                // if the conditions hold, advance the trip
                if (closest && trip.ExpectedSecondsToArriveAtNextStop <= 0)
                {
                    if (AdvancedTripToNextStop(tripCopy, allAvailableStops, line))
                    {
                        updatedTrips.Add(tripCopy);
                    }
                    continue;
                }

                // the trip will not be advanced
                updatedTrips.Add(tripCopy);
            }

            _tripsByLine[line] = updatedTrips;
        }

        /**
        * Advances a trip to the next stop
        * return true if the next stop is not the final one, otherwise returns false
        */
        private bool AdvancedTripToNextStop(Trip trip, List<StopInterval> intervals, string line)
        {
            var stopIndex = intervals.FindIndex(stop => stop.ToStopId == trip.NextStopId);
            stopIndex++;
            if (stopIndex == intervals.Count) // This trip has finished
            {
                LogTrip(line, FinalStop, DateTime.Now, trip.Id);
                return false;
            }

            // Set the trip's next stop
            trip.NextStopId = intervals[stopIndex].ToStopId;
            // Set the trip's expected number of seconds to reach the stop
            trip.ExpectedSecondsToArriveAtNextStop = _stopsIntervalByLineDictionary[line][trip.NextStopId];
            trip.LastSampling = DateTime.Now;
            LogTrip(line, trip.NextStopId, DateTime.Now, trip.Id);
            return true;
        }

        /**
        * This function iterates over all the stops of a given line.
        * for each one it checks if a trip is headed to it, and there isn't a known trip headed to it. 
        * If so, a new trip will be created and logged
        */
        private void CreateTrips(string line, List<StopEta> stops)
        {
            for (var i = 0; i < stops.Count; i++)
            {
                var stopInterval = _stopsIntervalByLineDictionary[line][stops[i].StopId];
                // no trip is headed to this stop
                if (!TripExistsHeadingToStop(stops, stops[i].StopId, stopInterval))
                {
                    continue;
                }

                // If got here, there're trips headed to this stop
                var trips = _tripsByLine[line].Where(trip => trip.NextStopId == stops[i].StopId).ToList();

                // No unknown trips
                if (trips.Count != 0) continue;

                // unknown trip is headed to this stop, we need to create it
                var trip = new Trip(_tripsSequenceByLine[line]++, stops[i].StopId, stopInterval, DateTime.Now);
                _tripsByLine[line].Add(trip);
                LogTrip(line, stops[i].StopId, stops[i].Eta, trip.Id);
            }
        }

        /**
        * a trip is heading to the given stop if the diff between the current stop's ETA and
        * previous one is different than the expected interval
        */
        private bool TripExistsHeadingToStop(List<StopEta> stops, int stopId, int interval)
        {
            var stopIndex = stops.FindIndex(stop => stop.StopId == stopId);
            return stopIndex == 0 || stops[stopIndex].Eta.Subtract(stops[stopIndex - 1].Eta).Seconds != interval;
        }

        private void LogTrip(string lineNumber, int stopId, DateTime eta, uint tripId)
        {
            Console.WriteLine($"{DateTime.Now},{lineNumber},{stopId},{eta},{tripId}"); // Print CSV line
        }
    }
}