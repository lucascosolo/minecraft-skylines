using System;

namespace Skylines.Bridge
{
    /// <summary>
    /// Raised when received bytes violate the SKBR wire format or the session rules
    /// (bad flags, oversize frame, unknown bridge type, truncated payload, invalid UTF-8, ...).
    /// </summary>
    public sealed class ProtocolException : Exception
    {
        /// <summary>Creates the exception with a human-readable description.</summary>
        public ProtocolException(string message) : base(message) { }
    }
}
