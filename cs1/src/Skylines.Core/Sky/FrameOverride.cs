using System;

namespace Skylines.Core.Sky
{
    /// <summary>
    /// Overrides one value for the length of a render (e.g. a camera's clear flags): <see cref="Apply"/> saves the
    /// original once and writes the override, <see cref="Restore"/> writes the original back once.
    /// </summary>
    public sealed class FrameOverride<T>
    {
        /// <summary>True between an <see cref="Apply"/> and the next <see cref="Restore"/>.</summary>
        public bool Saved { get; private set; }

        /// <summary>The value read by the first <see cref="Apply"/> since the last restore.</summary>
        public T SavedValue { get; private set; }

        /// <summary>Saves the current value (unless already saved) and writes <paramref name="value"/>.</summary>
        public void Apply(Func<T> read, Action<T> write, T value)
        {
            if (!Saved)
            {
                SavedValue = read();
                Saved = true;
            }
            write(value);
        }

        /// <summary>Writes the saved value back; false (and nothing written) when nothing is saved.</summary>
        public bool Restore(Action<T> write)
        {
            if (!Saved) return false;
            Saved = false;
            write(SavedValue);
            return true;
        }
    }
}
