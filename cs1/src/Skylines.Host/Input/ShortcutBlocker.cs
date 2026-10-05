using ColossalFramework.UI;
using UnityEngine;

namespace Skylines.Host.Input
{
    /// <summary>
    /// Stops CS1 from acting on its own keyboard and mouse shortcuts without patching the game: pushes
    /// an invisible modal UI component and gives it focus. CS1's checks this satisfies (decompiled):
    /// <c>GameKeyShortcuts.OnProcessKeyEvent</c>/<c>Update</c> act only when
    /// <c>!UIView.HasModalInput()</c>; <c>CameraController.UpdateTargetPosition</c> reads mouse, keys
    /// and wheel only when <c>!UIView.HasModalInput()</c>; <c>UIView.IsInsideUI()</c> is true while a
    /// modal is up, so tools ignore world clicks; <c>UIInput.ProcessKeyEvent</c> hands key events to
    /// the focused component first and skips shortcuts and <c>UIInput.eventProcessKeyEvent</c> (other
    /// mods) when it marks them used. Esc while a modal is up goes to <c>EscapeFromModal</c>, a no-op
    /// for a component that is not the unlocking panel.
    /// </summary>
    public sealed class ShortcutBlocker
    {
        private SinkPanel _panel;
        private bool _releasing;
        private int _releaseFrame;

        /// <summary>True while the modal is on CS1's modal stack.</summary>
        public bool Blocking { get { return _panel != null; } }

        /// <summary>Pushes the modal. Returns null on success or why it could not.</summary>
        public string Begin()
        {
            _releasing = false;
            if (_panel != null)
            {
                return null;
            }
            UIView view = UIView.GetAView();
            if (view == null)
            {
                return "no UIView";
            }
            var panel = (SinkPanel)view.AddUIComponent(typeof(SinkPanel));
            panel.name = "SkylinesHost.InputSink";
            panel.size = Vector2.zero;
            UIView.PushModal(panel);
            _panel = panel;
            UIView.SetFocus(panel);
            return null;
        }

        /// <summary>
        /// Asks for the modal to be removed. It stays one more frame and until Esc is up, so the key
        /// event that ended the mode is still swallowed. <see cref="Tick"/> does the removal.
        /// </summary>
        public void End()
        {
            if (_panel != null && !_releasing)
            {
                _releasing = true;
                _releaseFrame = Time.frameCount;
            }
        }

        /// <summary>Per frame: keeps focus while blocking and finishes a pending <see cref="End"/>.</summary>
        public void Tick()
        {
            if (_panel == null)
            {
                return;
            }
            if (!_releasing)
            {
                if (UIView.activeComponent != _panel)
                {
                    UIView.SetFocus(_panel);
                }
                return;
            }
            if (Time.frameCount > _releaseFrame && !UnityEngine.Input.GetKey(KeyCode.Escape))
            {
                ReleaseNow();
            }
        }

        /// <summary>
        /// Removes the modal immediately (level unloading, mod disabled). Returns null, or why it could
        /// not: another modal sits above ours, in which case <see cref="Tick"/> keeps retrying.
        /// </summary>
        public string ReleaseNow()
        {
            if (_panel == null)
            {
                _releasing = false;
                return null;
            }
            _releasing = true;
            if (UIView.GetModalComponent() != _panel)
            {
                return "another modal is above ours (" + Describe(UIView.GetModalComponent()) + ")";
            }
            UIView.PopModal();
            if (UIView.activeComponent == _panel)
            {
                UIView.SetFocus(null);
            }
            Object.Destroy(_panel.gameObject);
            _panel = null;
            _releasing = false;
            return null;
        }

        private static string Describe(UIComponent c)
        {
            return c == null ? "none" : c.name;
        }

        /// <summary>The invisible modal component; swallows every key event it is handed.</summary>
        public sealed class SinkPanel : UIPanel
        {
            /// <inheritdoc />
            protected override void OnKeyDown(UIKeyEventParameter p)
            {
                p.Use();
            }

            /// <inheritdoc />
            protected override void OnKeyUp(UIKeyEventParameter p)
            {
                p.Use();
            }
        }
    }
}
