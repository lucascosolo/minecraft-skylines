namespace Skylines.Core.Streaming
{
    /// <summary>Decides when a moving focus point is worth sending again: after a minimum move and interval, or at once when its active state changes.</summary>
    public sealed class FocusThrottle
    {
        private readonly float _minMove;
        private readonly double _minIntervalSeconds;
        private bool _has;
        private bool _active;
        private float _x;
        private float _z;
        private double _at;

        /// <summary>Creates a throttle that needs <paramref name="minMove"/> metres and <paramref name="minIntervalSeconds"/> between sends.</summary>
        public FocusThrottle(float minMove, double minIntervalSeconds)
        {
            _minMove = minMove;
            _minIntervalSeconds = minIntervalSeconds;
        }

        /// <summary>True when the focus should be sent now.</summary>
        public bool ShouldSend(bool active, float x, float z, double nowSeconds)
        {
            if (!_has) return true;
            if (active != _active) return true;
            if (!active) return false;
            if (nowSeconds - _at < _minIntervalSeconds) return false;
            float dx = x - _x, dz = z - _z;
            return dx * dx + dz * dz >= _minMove * _minMove;
        }

        /// <summary>Records what was sent.</summary>
        public void Sent(bool active, float x, float z, double nowSeconds)
        {
            _has = true;
            _active = active;
            _x = x;
            _z = z;
            _at = nowSeconds;
        }

        /// <summary>Forgets the last send, so the next check is true.</summary>
        public void Reset() { _has = false; }
    }
}
