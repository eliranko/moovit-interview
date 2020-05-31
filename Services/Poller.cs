using System.Threading;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
namespace moovit_interview
{
    /**
    * Poller poll's bus etas exposes an API to access those etas
    */
    public class Poller : IPoller
    {
        private INextBusProvider _provider;
        private IMatcher _matcher;
        private IDictionary<int, IDictionary<string, DateTime>> _stops;
        private BlockingCollection<string> _blockingCollection;
        private List<Thread> _lineHandlers;
        private System.Timers.Timer _linesFetchTimer;
        private object _locker;

        public Poller(INextBusProvider provider, IMatcher matcher)
        {
            _stops = new Dictionary<int, IDictionary<string, DateTime>>();
            _blockingCollection = new BlockingCollection<string>();
            _lineHandlers = new List<Thread>();
            _locker = new object();

            _provider = provider;
            _matcher = matcher;
        }

        /**
        * Starts pulling line
        */
        public void Init(PollerConfig config)
        {
            // Spawn threads to handle line requests
            SpawnLineHandlerThreads(config.MaxConcurrency);

            // Set timer to fetch lines at specific intervals
            FetchLines(config.LineNumbers); // fetch lines immediatly, otherwise we will wait for the first tick 
                                            // which could take couple of seconds
            _linesFetchTimer = new System.Timers.Timer(config.PollIntervalSeconds * 1000);
            _linesFetchTimer.Elapsed += (sender, e) => FetchLines(config.LineNumbers);
            _linesFetchTimer.AutoReset = true;
            _linesFetchTimer.Enabled = true;

            _matcher.Init(config);
        }

        /**
        * Returns lines eta of the given stop
        */
        public List<LineEta> GetStopArrivals(int stopId)
        {
            var result = new List<LineEta>();
            if (!_stops.ContainsKey(stopId)) return result; // or throw?

            lock (_locker)
            {
                // runs in O(number of lines)
                // I assume that the number of lines of each stop is not significant
                foreach (KeyValuePair<string, DateTime> line in _stops[stopId])
                {
                    result.Add(new LineEta(line.Key, line.Value));
                }
            }

            return result;
        }

        /**
        * Releases the resources used by this instance
        */
        public void Dispose()
        {
            _blockingCollection.CompleteAdding();
            _linesFetchTimer.Stop();
            _linesFetchTimer.Dispose();
        }

        /**
        * Spawn specific number of threads to handle line requests
        */
        private void SpawnLineHandlerThreads(int number)
        {
            for (var i = 0; i < number; i++)
            {
                // we could use thread pool here for faster startup, but the handlers are blocking
                // and if their number is too big it could have bad side effect
                var thread = new Thread(LineEtaHandler);
                _lineHandlers.Add(thread);
                thread.Start();
            }
        }

        /**
        * Fetches all available lines
        */
        private void FetchLines(List<string> lines)
        {
            foreach (var line in lines)
            {
                _blockingCollection.Add(line);
            }
        }

        /**
        * Listening on line requests and handles them
        * It uses the shared blocking collection to balance the load
        */
        private void LineEtaHandler()
        {
            while (!_blockingCollection.IsCompleted)
            {
                string line;
                try
                {
                    // Take next line request
                    line = _blockingCollection.Take();
                }
                catch (OperationCanceledException) // Blocking collection marked as completed. This thread's job is done
                {
                    return;
                }

                // Get line from provider
                var stops = _provider.GetLineEta(line);
                _matcher.UpdateLineEtas(line, stops);

                lock (_locker) // Update shared stops data structure, lock to prevent inconsistencies
                {
                    foreach (var stop in stops)
                    {
                        if (!_stops.ContainsKey(stop.StopId)) // Add the current stop to the data structure if it doesn't exist
                        {
                            _stops.Add(stop.StopId, new Dictionary<string, DateTime>());
                        }

                        IDictionary<string, DateTime> linesEtaOfCurrentStop;
                        _stops.TryGetValue(stop.StopId, out linesEtaOfCurrentStop); // key exists, no need to verify existence
                        linesEtaOfCurrentStop[line] = stop.Eta; // Set the line eta of the current stop
                    }
                }
            }
        }
    }
}