namespace Skylines.Bridge
{
    /// <summary>Delay between guest connection attempts: a fast phase, then a slow phase.</summary>
    public sealed class RetrySchedule
    {
        private readonly int _fastMs, _fastAttempts, _slowMs;

        /// <summary>The spec's schedule: 1 s for the first 10 attempts, then 5 s.</summary>
        public RetrySchedule() : this(1000, 10, 5000) { }

        /// <summary>A custom schedule (tests use short delays).</summary>
        public RetrySchedule(int fastDelayMs, int fastAttempts, int slowDelayMs)
        {
            _fastMs = fastDelayMs; _fastAttempts = fastAttempts; _slowMs = slowDelayMs;
        }

        /// <summary>Delay to wait after the attempt with the given zero-based index (reset to 0 after a connection is established).</summary>
        public int GetDelayMs(int attemptIndex)
        {
            return attemptIndex < _fastAttempts ? _fastMs : _slowMs;
        }
    }
}
