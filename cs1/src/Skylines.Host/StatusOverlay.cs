using UnityEngine;

namespace Skylines.Host
{
    /// <summary>
    /// A small always-on-top text box drawn with Unity IMGUI, usable in the main menu and in a city.
    /// Wire <see cref="Draw"/> to <see cref="MainThreadPump.Gui"/>. A key toggles it.
    /// </summary>
    public sealed class StatusOverlay
    {
        private GUIStyle _style;

        /// <summary>Lines to show; set from the main thread.</summary>
        public string Text = "";

        /// <summary>Whether the box is drawn.</summary>
        public bool Visible = true;

        /// <summary>Key that toggles <see cref="Visible"/> (checked from OnGUI key events).</summary>
        public KeyCode ToggleKey = KeyCode.F7;

        /// <summary>Top-left corner of the box in screen pixels.</summary>
        public Vector2 Position = new Vector2(12f, 48f);

        /// <summary>Draws the box. Call from OnGUI only.</summary>
        public void Draw()
        {
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == ToggleKey)
            {
                Visible = !Visible;
            }
            if (!Visible || string.IsNullOrEmpty(Text) || e == null || e.type != EventType.Repaint)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box);
                _style.alignment = TextAnchor.UpperLeft;
                _style.fontSize = 13;
                _style.wordWrap = false;
                _style.padding = new RectOffset(8, 8, 6, 6);
                _style.normal.textColor = Color.white;
            }
            var content = new GUIContent(Text);
            Vector2 size = _style.CalcSize(content);
            GUI.Box(new Rect(Position.x, Position.y, size.x, size.y), content, _style);
        }
    }
}
