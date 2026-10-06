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
    /// <para>The blocker also stops CS1's own UI from drawing while it is up, by disabling the UI camera
    /// (<c>UIView.uiCamera</c>) and leaving <c>UIView.enabled</c> alone. <c>UIView.Show</c> flips <c>enabled</c>,
    /// whose <c>OnEnable</c>/<c>OnDisable</c> toggle the camera and mesh renderer and force a full re-layout
    /// (<c>OnResolutionChanged</c>), so any second caller (CameraController.UpdateFreeCamera, MainToolbar,
    /// cinematic camera, other mods) made the UI flash. Nothing else in the game touches the camera's own
    /// <c>enabled</c>. The UIView keeps updating, <c>UIInput</c> keeps delivering key events to the focused
    /// modal (<c>UIInput.ProcessKeyEvent</c> does not test <c>UIView.isVisible</c>; only mouse events do).
    /// The camera state is restored on release.</para>
    /// </summary>
    public sealed class ShortcutBlocker
    {
        private SinkPanel _panel;
        private bool _releasing;
        private int _releaseFrame;
        private UnityEngine.Camera _uiCamera;
        private bool _uiCameraWasEnabled;

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
            HideUi();
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
                if (_uiCamera != null && _uiCamera.enabled) _uiCamera.enabled = false;
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
                RestoreUi();
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
            RestoreUi();
            return null;
        }

        // Records the UI camera's state once and turns it off; the view itself stays enabled.
        private void HideUi()
        {
            if (_uiCamera != null) return;
            UIView view = UIView.GetAView();
            UnityEngine.Camera cam = view == null ? null : view.uiCamera;
            if (cam == null) return;
            _uiCamera = cam;
            _uiCameraWasEnabled = cam.enabled;
            cam.enabled = false;
        }

        // Puts the recorded camera state back.
        private void RestoreUi()
        {
            if (_uiCamera == null) return;
            UnityEngine.Camera cam = _uiCamera;
            _uiCamera = null;
            cam.enabled = _uiCameraWasEnabled;
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
