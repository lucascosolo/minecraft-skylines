using System;
using ColossalFramework.UI;
using UnityEngine;
using Object = UnityEngine.Object;
using UInput = UnityEngine.Input;

namespace Skylines.Host.Ui
{
    /// <summary>What a <see cref="MapDropButton"/> looks like and says.</summary>
    public sealed class MapDropSpec
    {
        /// <summary>GameObject name of the button (the hint label gets a suffix).</summary>
        public string Name;
        /// <summary>Foreground sprite candidates, first found wins; the atlas holding it is used for the button.</summary>
        public string[] IconSprites;
        /// <summary>Background sprite name stems tried in that atlas (stem, stem+"Hovered", stem+"Pressed", stem+"Focused").</summary>
        public string[] BackgroundStems;
        /// <summary>Tooltip, also shown as a hint when the button is clicked without dragging.</summary>
        public string Tooltip;
    }

    /// <summary>
    /// Google-Street-View-style "drag the little figure onto the map": a CS1 button placed above the
    /// bulldozer button. Pressing it and dragging makes <see cref="MapDropTool"/> the current tool (marker and
    /// figure); releasing over the city calls the drop callback with the hit point. Esc, right-click, a tool
    /// switch or releasing over UI cancels; the previous tool is restored either way. A click without a drag
    /// shows the tooltip as a hint. Creates only its own UI objects and tool; vanilla UI is only read for
    /// placement. Call <see cref="Update"/> every frame and <see cref="Dispose"/> on level unload.
    /// </summary>
    public sealed class MapDropButton : IDisposable
    {
        private const float Size = 46f;
        private const float Gap = 6f;
        private const float HintSeconds = 3f;

        private readonly MapDropSpec _spec;
        private readonly Action<Vector3> _dropped;
        private readonly HostLog _log;
        private readonly DragGesture _gesture = new DragGesture();
        private UIButton _button;
        private UILabel _hint;
        private MapDropTool _tool;
        private ToolBase _previous;
        private Texture _figure;
        private Rect _figureUv;
        private float _figureAspect = 1f;
        private float _hintUntil;
        private bool _visible = true;

        /// <summary><paramref name="dropped"/> gets the city point under the cursor at a successful drop (main thread).</summary>
        public MapDropButton(MapDropSpec spec, Action<Vector3> dropped, HostLog log)
        {
            _spec = spec;
            _dropped = dropped;
            _log = log;
        }

        /// <summary>Creates the button; null on success, otherwise why not (nothing is left behind).</summary>
        public string Create()
        {
            if (_button != null) return null;
            UIView view = UIView.GetAView();
            if (view == null) return "no UIView";
            string iconName;
            UITextureAtlas atlas = FindAtlas(_spec.IconSprites, out iconName);
            if (atlas == null) return "no atlas holds any of " + string.Join(", ", _spec.IconSprites);
            UITextureAtlas.SpriteInfo icon = atlas[iconName];
            _figure = atlas.texture;
            _figureUv = icon.region;
            _figureAspect = icon.height > 0f ? icon.width / icon.height : 1f;

            _button = (UIButton)view.AddUIComponent(typeof(UIButton));
            _button.name = _spec.Name;
            _button.atlas = atlas;
            _button.size = new Vector2(Size, Size);
            _button.normalFgSprite = iconName;
            _button.hoveredFgSprite = Variant(atlas, iconName, "Hovered");
            _button.pressedFgSprite = Variant(atlas, iconName, "Pressed");
            _button.focusedFgSprite = iconName;
            _button.foregroundSpriteMode = UIForegroundSpriteMode.Scale;
            _button.scaleFactor = 0.8f;
            string bg = Background(atlas);
            if (bg != null)
            {
                _button.normalBgSprite = bg;
                _button.hoveredBgSprite = Variant(atlas, bg, "Hovered");
                _button.pressedBgSprite = Variant(atlas, bg, "Pressed");
                _button.focusedBgSprite = bg;
            }
            _button.tooltip = _spec.Tooltip;
            _button.playAudioEvents = true;
            _button.canFocus = true;
            _button.eventMouseDown += OnMouseDown;
            _button.eventKeyDown += OnKeyDown;

            _hint = (UILabel)view.AddUIComponent(typeof(UILabel));
            _hint.name = _spec.Name + ".Hint";
            _hint.text = _spec.Tooltip;
            _hint.textScale = 0.8f;
            _hint.padding = new RectOffset(8, 8, 6, 6);
            _hint.isInteractive = false;
            string panel = FirstSprite(atlas, "GenericPanel", "SubcategoriesPanel");
            if (panel != null)
            {
                _hint.atlas = atlas;
                _hint.backgroundSprite = panel;
            }
            _hint.isVisible = false;
            Place();
            _log.Info("drop button: icon '" + iconName + "', background '" + (bg ?? "none") + "' from atlas '" + atlas.name + "'");
            return null;
        }

        /// <summary>Shows or hides the button; hiding cancels a drag in progress.</summary>
        public bool Visible
        {
            set
            {
                if (value == _visible) return;
                _visible = value;
                if (!value) End(_gesture.Cancel());
                if (_button != null) _button.isVisible = value;
                if (!value && _hint != null) _hint.isVisible = false;
            }
        }

        /// <summary>Per frame on the main thread.</summary>
        public void Update()
        {
            if (_button == null) return;
            if (_visible) Place();
            if (_hint.isVisible && Time.realtimeSinceStartup > _hintUntil) _hint.isVisible = false;
            if (!_gesture.Active) return;
            if (!UInput.GetMouseButton(0))
            {
                Vector3 hit = Vector3.zero;
                bool hasHit = _tool != null && _tool.TryGetHit(out hit);
                bool overUi = ToolsModifierControl.toolController == null || ToolsModifierControl.toolController.IsInsideUI;
                End(_gesture.Release(overUi, hasHit), hit);
                return;
            }
            // Esc normally arrives through OnKeyDown (the focused button), which keeps it from the pause menu.
            bool escape = !_button.hasFocus && UInput.GetKeyDown(KeyCode.Escape);
            if (escape || UInput.GetMouseButtonDown(1) || (_gesture.Phase == DragPhase.Dragging && !ToolIsCurrent()))
            {
                End(_gesture.Cancel());
                return;
            }
            Vector3 m = UInput.mousePosition;
            if (_gesture.Move(m.x, m.y) == DragOutcome.Started) BeginTool();
        }

        /// <summary>Cancels a drag, restores the tool, destroys the button, hint and tool component.</summary>
        public void Dispose()
        {
            End(_gesture.Cancel());
            if (_button != null)
            {
                _button.eventMouseDown -= OnMouseDown;
                _button.eventKeyDown -= OnKeyDown;
                Object.Destroy(_button.gameObject);
                _button = null;
            }
            if (_hint != null)
            {
                Object.Destroy(_hint.gameObject);
                _hint = null;
            }
            if (_tool != null)
            {
                ToolController tc = ToolsModifierControl.toolController;
                if (tc != null && tc.CurrentTool == _tool) ToolsModifierControl.SetTool<DefaultTool>();
                Object.Destroy(_tool);
                _tool = null;
            }
        }

        private void OnMouseDown(UIComponent c, UIMouseEventParameter p)
        {
            if ((p.buttons & UIMouseButton.Left) == 0 || !_visible) return;
            Vector3 m = UInput.mousePosition;
            if (_gesture.Press(m.x, m.y))
            {
                _hint.isVisible = false;
                _button.Focus();
            }
        }

        private void OnKeyDown(UIComponent c, UIKeyEventParameter p)
        {
            if (p.keycode != KeyCode.Escape || !_gesture.Active) return;
            p.Use();
            End(_gesture.Cancel());
        }

        private void BeginTool()
        {
            ToolController tc = ToolsModifierControl.toolController;
            if (tc == null)
            {
                End(_gesture.Cancel());
                return;
            }
            _previous = tc.CurrentTool;
            if (_tool == null)
            {
                // ToolBase.OnEnable makes a freshly added tool the current one.
                _tool = tc.gameObject.AddComponent<MapDropTool>();
            }
            _tool.Figure = _figure;
            _tool.FigureUv = _figureUv;
            _tool.FigureAspect = _figureAspect;
            tc.CurrentTool = _tool;
        }

        private void End(DragOutcome outcome)
        {
            End(outcome, Vector3.zero);
        }

        private void End(DragOutcome outcome, Vector3 hit)
        {
            if (outcome == DragOutcome.None || outcome == DragOutcome.Started) return;
            if (_button != null) _button.Unfocus();
            RestoreTool();
            if (outcome == DragOutcome.Clicked) ShowHint();
            if (outcome != DragOutcome.Dropped) return;
            _log.Info("drop button: dropped at (" + hit.x.ToString("0.0") + ", " + hit.y.ToString("0.0") + ", " + hit.z.ToString("0.0") + ")");
            try { _dropped(hit); }
            catch (Exception e) { _log.Error("drop callback", e); }
        }

        private void RestoreTool()
        {
            ToolBase previous = _previous;
            _previous = null;
            if (!ToolIsCurrent()) return;
            if (previous != null && previous != _tool) ToolsModifierControl.toolController.CurrentTool = previous;
            else ToolsModifierControl.SetTool<DefaultTool>();
        }

        private bool ToolIsCurrent()
        {
            ToolController tc = ToolsModifierControl.toolController;
            return _tool != null && tc != null && tc.CurrentTool == _tool;
        }

        private void ShowHint()
        {
            ShowHint(_spec.Tooltip);
        }

        /// <summary>Shows <paramref name="text"/> beside the button for a few seconds (e.g. why a drop was refused).</summary>
        public void ShowHint(string text)
        {
            if (_hint == null) return;
            _hint.text = text;
            _hint.isVisible = true;
            _hint.BringToFront();
            _hintUntil = Time.realtimeSinceStartup + HintSeconds;
            Place();
        }

        // Above the bulldozer button (and above its bar while bulldozing), right-aligned with it; bottom
        // right of the screen when it cannot be found. Vanilla components are only read.
        private void Place()
        {
            UIComponent anchor = UIView.Find("BulldozerButton");
            Vector2 pos;
            if (anchor != null && anchor.isVisible)
            {
                float top = anchor.absolutePosition.y;
                UIComponent bar = UIView.Find("BulldozerBar");
                if (bar != null && bar.isVisible) top = Mathf.Min(top, bar.absolutePosition.y);
                pos = new Vector2(anchor.absolutePosition.x + anchor.width - Size, top - Size - Gap);
            }
            else
            {
                Vector2 screen = UIView.GetAView().GetScreenResolution();
                pos = new Vector2(screen.x - Size - 12f, screen.y - Size - 140f);
            }
            _button.absolutePosition = pos;
            if (_hint.isVisible)
                _hint.absolutePosition = new Vector2(pos.x + Size - _hint.width, pos.y - _hint.height - Gap);
        }

        private string Background(UITextureAtlas atlas)
        {
            foreach (string stem in _spec.BackgroundStems ?? new string[0])
                if (atlas[stem] != null) return stem;
            return null;
        }

        private static string Variant(UITextureAtlas atlas, string sprite, string suffix)
        {
            return atlas[sprite + suffix] != null ? sprite + suffix : sprite;
        }

        private static string FirstSprite(UITextureAtlas atlas, params string[] names)
        {
            foreach (string n in names)
                if (atlas[n] != null) return n;
            return null;
        }

        // The view's default atlas first, then every loaded atlas.
        private static UITextureAtlas FindAtlas(string[] sprites, out string found)
        {
            UITextureAtlas def = UIView.GetAView().defaultAtlas;
            foreach (string s in sprites)
            {
                found = s;
                if (def != null && def[s] != null) return def;
                foreach (UITextureAtlas a in Resources.FindObjectsOfTypeAll<UITextureAtlas>())
                    if (a != null && a[s] != null) return a;
            }
            found = null;
            return null;
        }
    }
}
