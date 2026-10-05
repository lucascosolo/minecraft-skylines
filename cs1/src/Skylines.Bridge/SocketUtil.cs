using System;
using System.Net.Sockets;

namespace Skylines.Bridge
{
    /// <summary>Best-effort socket helpers that never throw.</summary>
    internal static class SocketUtil
    {
        public static void SendAll(Socket s, byte[] data)
        {
            int sent = 0;
            while (sent < data.Length) sent += s.Send(data, sent, data.Length - sent, SocketFlags.None);
        }

        /// <summary>Shuts down the write side, optionally waits briefly for the peer to close, then closes.</summary>
        public static void CloseGracefully(Socket s, int drainMs)
        {
            try { s.Shutdown(SocketShutdown.Send); } catch (Exception) { }
            if (drainMs > 0) Drain(s, drainMs);
            try { s.Close(); } catch (Exception) { }
        }

        /// <summary>Discards incoming data until the peer closes or the time is up, so closing does not reset the connection.</summary>
        private static void Drain(Socket s, int ms)
        {
            long end = Clock.NowMs() + ms;
            var buf = new byte[1024];
            try
            {
                s.ReceiveTimeout = ms;
                while (Clock.NowMs() < end)
                    if (s.Receive(buf) == 0) break;
            }
            catch (Exception) { }
        }
    }
}
