using System;

namespace Skylines.Host.Ui
{
    /// <summary>Where a <see cref="DragGesture"/> is.</summary>
    public enum DragPhase
    {
        /// <summary>Nothing pressed.</summary>
        Idle,
        /// <summary>Pressed, not yet moved past the threshold.</summary>
        Pressed,
        /// <summary>Moved past the threshold with the button held.</summary>
        Dragging,
    }

    /// <summary>What a <see cref="DragGesture"/> call produced.</summary>
    public enum DragOutcome
    {
        /// <summary>Nothing happened.</summary>
        None,
        /// <summary>The drag began.</summary>
        Started,
        /// <summary>Released without dragging.</summary>
        Clicked,
        /// <summary>Released over a target, not over UI.</summary>
        Dropped,
        /// <summary>Cancelled, or released over UI or with no target.</summary>
        Cancelled,
    }

    /// <summary>
    /// Press, drag past a threshold, release: a click (no drag), a drop, or a cancel (Esc, right-click,
    /// release over UI or with nothing under the cursor). Unity-free; positions are screen pixels.
    /// </summary>
    public sealed class DragGesture
    {
        /// <summary>Default drag threshold in pixels.</summary>
        public const float DefaultThresholdPx = 8f;

        private readonly float _threshold;
        private float _x, _y;

        /// <summary>A gesture with <see cref="DefaultThresholdPx"/>.</summary>
        public DragGesture() : this(DefaultThresholdPx) { }

        /// <summary>A gesture that starts dragging once the cursor moved <paramref name="thresholdPx"/> (&gt; 0) from the press.</summary>
        public DragGesture(float thresholdPx)
        {
            if (!(thresholdPx > 0f)) throw new ArgumentOutOfRangeException("thresholdPx", "must be positive");
            _threshold = thresholdPx;
        }

        /// <summary>Current phase; starts <see cref="DragPhase.Idle"/>.</summary>
        public DragPhase Phase { get; private set; }

        /// <summary>True unless idle.</summary>
        public bool Active { get { return Phase != DragPhase.Idle; } }

        /// <summary>Idle: remembers the press point and returns true; otherwise ignored.</summary>
        public bool Press(float x, float y)
        {
            if (Phase != DragPhase.Idle) return false;
            Phase = DragPhase.Pressed;
            _x = x;
            _y = y;
            return true;
        }

        /// <summary>Pressed: <see cref="DragOutcome.Started"/> once at or past the threshold.</summary>
        public DragOutcome Move(float x, float y)
        {
            if (Phase != DragPhase.Pressed) return DragOutcome.None;
            float dx = x - _x, dy = y - _y;
            if (Math.Sqrt(dx * dx + dy * dy) < _threshold) return DragOutcome.None;
            Phase = DragPhase.Dragging;
            return DragOutcome.Started;
        }

        /// <summary>Ends the gesture: click, drop, or cancel (over UI or no target).</summary>
        public DragOutcome Release(bool overUi, bool hasTarget)
        {
            DragPhase was = Phase;
            Phase = DragPhase.Idle;
            if (was == DragPhase.Pressed) return DragOutcome.Clicked;
            if (was == DragPhase.Dragging) return overUi || !hasTarget ? DragOutcome.Cancelled : DragOutcome.Dropped;
            return DragOutcome.None;
        }

        /// <summary>Ends a pressed or dragging gesture as cancelled.</summary>
        public DragOutcome Cancel()
        {
            if (Phase == DragPhase.Idle) return DragOutcome.None;
            Phase = DragPhase.Idle;
            return DragOutcome.Cancelled;
        }
    }
}
