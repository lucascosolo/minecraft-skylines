using System.Collections.Generic;
using UnityEngine;

namespace Skylines.Host.Input
{
    /// <summary>Kinds of <see cref="CapturedEvent"/>.</summary>
    public enum CapturedKind
    {
        /// <summary>A watched key code went down; <see cref="CapturedEvent.Code"/> is the Unity KeyCode.</summary>
        KeyDown,
        /// <summary>A watched key code went up.</summary>
        KeyUp,
        /// <summary>Mouse wheel; <see cref="CapturedEvent.Amount"/> in notches, positive up.</summary>
        Wheel,
        /// <summary>Typed text; <see cref="CapturedEvent.Code"/> is the UTF-16 code unit.</summary>
        Text,
    }

    /// <summary>One input transition captured in a frame.</summary>
    public struct CapturedEvent
    {
        /// <summary>What happened.</summary>
        public CapturedKind Kind;
        /// <summary>Unity KeyCode (mouse buttons are KeyCode.Mouse0..6) or a character.</summary>
        public int Code;
        /// <summary>Wheel notches.</summary>
        public float Amount;

        /// <summary>Creates an event.</summary>
        public CapturedEvent(CapturedKind kind, int code, float amount)
        {
            Kind = kind;
            Code = code;
            Amount = amount;
        }
    }

    /// <summary>
    /// Per-frame raw input for a first-person integration: transitions of a fixed list of Unity key
    /// codes (mouse buttons included as KeyCode.Mouse0..6), wheel, text and mouse delta, with the
    /// cursor locked and hidden while active. Reads UnityEngine.Input directly, so it sees keys even
    /// while a modal UI component swallows them for the game. Main thread only.
    /// </summary>
    public sealed class InputCapture
    {
        private readonly int[] _codes;
        private readonly bool[] _held;
        private CursorLockMode _lockState;
        private bool _cursorVisible;

        /// <summary>Watches <paramref name="unityKeyCodes"/>.</summary>
        public InputCapture(int[] unityKeyCodes)
        {
            _codes = (int[])unityKeyCodes.Clone();
            _held = new bool[_codes.Length];
        }

        /// <summary>True between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public bool Active { get; private set; }

        /// <summary>Locks and hides the cursor (remembering its state). Keys already held count as held.</summary>
        public void Begin()
        {
            if (Active)
            {
                return;
            }
            _lockState = Cursor.lockState;
            _cursorVisible = Cursor.visible;
            for (int i = 0; i < _codes.Length; i++)
            {
                _held[i] = UnityEngine.Input.GetKey((KeyCode)_codes[i]);
            }
            Active = true;
            Lock();
        }

        /// <summary>Re-locks the cursor (focus may have come back) and returns this frame's raw mouse axis delta (Unity "Mouse X"/"Mouse Y" units; positive = right/up).</summary>
        public void ReadMouse(out float mouseDx, out float mouseDy)
        {
            if (Active && Application.isFocused)
            {
                Lock();
            }
            mouseDx = Active ? UnityEngine.Input.GetAxisRaw("Mouse X") : 0f;
            mouseDy = Active ? UnityEngine.Input.GetAxisRaw("Mouse Y") : 0f;
        }

        /// <summary>Appends the transitions since the previous call (or since <see cref="Begin"/>).</summary>
        public void Poll(List<CapturedEvent> into)
        {
            if (!Active)
            {
                return;
            }
            for (int i = 0; i < _codes.Length; i++)
            {
                bool down = UnityEngine.Input.GetKey((KeyCode)_codes[i]);
                if (down != _held[i])
                {
                    _held[i] = down;
                    into.Add(new CapturedEvent(down ? CapturedKind.KeyDown : CapturedKind.KeyUp, _codes[i], 0f));
                }
            }
            float wheel = UnityEngine.Input.mouseScrollDelta.y;
            if (wheel != 0f)
            {
                into.Add(new CapturedEvent(CapturedKind.Wheel, 0, wheel));
            }
            string text = UnityEngine.Input.inputString;
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsControl(text[i]))
                {
                    into.Add(new CapturedEvent(CapturedKind.Text, text[i], 0f));
                }
            }
        }

        /// <summary>Restores the cursor and forgets held keys. Idempotent.</summary>
        public void End()
        {
            if (!Active)
            {
                return;
            }
            Active = false;
            for (int i = 0; i < _held.Length; i++)
            {
                _held[i] = false;
            }
            Cursor.lockState = _lockState;
            Cursor.visible = _cursorVisible;
        }

        private static void Lock()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
