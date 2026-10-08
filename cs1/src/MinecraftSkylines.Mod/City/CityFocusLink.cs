using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Streaming;
using UnityEngine;

namespace MinecraftSkylines.Mod.City
{
    /// <summary>
    /// Minor 18: while a city is open and player mode is off, tells the guest where the city view looks
    /// (CITY_FOCUS, throttled) and streams collision around that point so the guest can simulate the area.
    /// Main thread only.
    /// </summary>
    internal sealed class CityFocusLink
    {
        private readonly CityLink _city;
        private readonly PlayerMode _player;
        private readonly FocusThrottle _throttle = new FocusThrottle(8f, 0.25);
        private uint _seenOpenSeq;
        private bool _activeSent;

        public CityFocusLink(CityLink city, PlayerMode player)
        {
            _city = city;
            _player = player;
        }

        /// <summary>Per frame, after the city link and player mode were updated.</summary>
        public void Update(BridgeHost host, double nowSeconds)
        {
            if (host == null || host.State != BridgeState.Connected || host.NegotiatedAppMinor < 18)
            {
                Reset();
                return;
            }
            uint seq = _city.OpenSeq;
            if (seq != _seenOpenSeq)
            {
                _seenOpenSeq = seq;
                Reset();
            }
            if (!_city.IsOpen)
            {
                Reset();
                return;
            }
            if (_player.IsOn)
            {
                if (_activeSent) Send(host, false, 0f, 0f, nowSeconds);
                return;
            }
            Vector3 cs;
            if (!TryFocus(out cs)) return;
            if (_throttle.ShouldSend(true, cs.x, -cs.z, nowSeconds)) Send(host, true, cs.x, -cs.z, nowSeconds);
            _player.StreamAround(host, cs);
        }

        /// <summary>A new open, link loss or the end of the open: the guest forgets the focus on its own, so nothing is owed.</summary>
        public void Reset()
        {
            _throttle.Reset();
            _activeSent = false;
        }

        // CameraController.m_currentPosition is the orbit point the controller moves while the city view runs.
        private static bool TryFocus(out Vector3 cs)
        {
            cs = Vector3.zero;
            Camera cam = Camera.main;
            if (cam == null) return false;
            CameraController controller = cam.GetComponent<CameraController>();
            if (controller == null || !controller.enabled) return false;
            cs = controller.m_currentPosition;
            return !(float.IsNaN(cs.x) || float.IsInfinity(cs.x) || float.IsNaN(cs.z) || float.IsInfinity(cs.z));
        }

        private void Send(BridgeHost host, bool active, float x, float z, double nowSeconds)
        {
            var m = new CityFocus { X = x, Z = z, Flags = active ? CityFocus.Active : (byte)0 };
            if (!host.Send(AppProtocol.CityFocusType, m.Encode())) return;
            _throttle.Sent(active, x, z, nowSeconds);
            _activeSent = active;
        }
    }
}
