using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skylines.Host.Camera
{
    /// <summary>
    /// Takes the city camera away from CS1's <c>CameraController</c> and gives it back exactly as it
    /// was. <see cref="Acquire"/> records every property this class changes, <see cref="Drive"/> sets
    /// the pose each frame, <see cref="Release"/> restores. Release is idempotent and never throws, so
    /// it is safe from any failure path. Main thread only.
    /// </summary>
    public sealed class CameraTakeover
    {
        // Post effects that assume the orbit camera (tilt-shift blur, depth of field). Matched by type
        // name: DepthOfField lives in Assembly-CSharp-firstpass, which the mod does not reference.
        private static readonly string[] s_effectTypes = { "TiltShiftEffect", "DepthOfField" };

        private readonly List<Behaviour> _effects = new List<Behaviour>();
        private readonly List<bool> _effectsEnabled = new List<bool>();

        private UnityEngine.Camera _camera;
        private CameraController _controller;
        private bool _controllerEnabled;
        private Vector3 _position;
        private Quaternion _rotation;
        private float _fov;
        private float _near;
        private float _far;
        private Vector3 _targetPosition;
        private Vector3 _currentPosition;
        private Vector2 _targetAngle;
        private Vector2 _currentAngle;
        private float _targetSize;
        private float _currentSize;

        /// <summary>True between a successful <see cref="Acquire"/> and <see cref="Release"/>.</summary>
        public bool Active { get; private set; }

        /// <summary>The controller's ground target (where the city camera looks), read at acquire.</summary>
        public Vector3 CityTarget { get { return _targetPosition; } }

        /// <summary>The camera's far clip plane at acquire (the city view's).</summary>
        public float CityFar { get { return _far; } }

        /// <summary>The camera's world rotation at acquire.</summary>
        public Quaternion CityRotation { get { return _rotation; } }

        /// <summary>
        /// Records the camera and disables everything that moves it. Returns null on success or a reason
        /// it refused (nothing is changed then).
        /// </summary>
        public string Acquire()
        {
            if (Active)
            {
                return null;
            }
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                return "no main camera";
            }
            CameraController controller = cam.GetComponent<CameraController>();
            if (controller == null)
            {
                return "main camera has no CameraController";
            }
            CinematicCameraController cinematic = cam.GetComponent<CinematicCameraController>();
            if (cinematic != null && cinematic.enabled)
            {
                return "cinematic camera is running";
            }

            _camera = cam;
            _controller = controller;
            _controllerEnabled = controller.enabled;
            Transform t = cam.transform;
            _position = t.position;
            _rotation = t.rotation;
            _fov = cam.fieldOfView;
            _near = cam.nearClipPlane;
            _far = cam.farClipPlane;
            _targetPosition = controller.m_targetPosition;
            _currentPosition = controller.m_currentPosition;
            _targetAngle = controller.m_targetAngle;
            _currentAngle = controller.m_currentAngle;
            _targetSize = controller.m_targetSize;
            _currentSize = controller.m_currentSize;
            _effects.Clear();
            _effectsEnabled.Clear();
            foreach (Behaviour b in cam.GetComponents<Behaviour>())
            {
                if (b != null && Array.IndexOf(s_effectTypes, b.GetType().Name) >= 0)
                {
                    _effects.Add(b);
                    _effectsEnabled.Add(b.enabled);
                }
            }

            Active = true;
            controller.enabled = false;
            foreach (Behaviour b in _effects)
            {
                b.enabled = false;
            }
            return null;
        }

        /// <summary>Sets the camera pose for this frame. No-op unless active.</summary>
        public void Drive(Vector3 position, Quaternion rotation, float fovDeg, float nearClip)
        {
            if (!Active || _camera == null)
            {
                return;
            }
            _camera.transform.position = position;
            _camera.transform.rotation = rotation;
            if (fovDeg > 1f && fovDeg < 179f)
            {
                _camera.fieldOfView = fovDeg;
            }
            _camera.nearClipPlane = nearClip;
        }

        /// <summary>Sets the clip planes for this frame; <see cref="Release"/> restores the recorded ones. No-op unless active.</summary>
        public void SetClip(float nearClip, float farClip)
        {
            if (!Active || _camera == null || farClip <= nearClip)
            {
                return;
            }
            _camera.nearClipPlane = nearClip;
            _camera.farClipPlane = farClip;
        }

        /// <summary>Restores everything <see cref="Acquire"/> recorded. Idempotent; never throws.</summary>
        /// <returns>Null, or a description of what could not be restored.</returns>
        public string Release()
        {
            if (!Active)
            {
                return null;
            }
            Active = false;
            string problems = null;
            problems = Try(problems, "camera", () =>
            {
                if (_camera == null) return;
                _camera.transform.position = _position;
                _camera.transform.rotation = _rotation;
                _camera.fieldOfView = _fov;
                _camera.nearClipPlane = _near;
                _camera.farClipPlane = _far;
            });
            for (int i = 0; i < _effects.Count; i++)
            {
                int k = i;
                problems = Try(problems, "effect", () => { if (_effects[k] != null) _effects[k].enabled = _effectsEnabled[k]; });
            }
            problems = Try(problems, "controller", () =>
            {
                if (_controller == null) return;
                _controller.m_targetPosition = _targetPosition;
                _controller.m_currentPosition = _currentPosition;
                _controller.m_targetAngle = _targetAngle;
                _controller.m_currentAngle = _currentAngle;
                _controller.m_targetSize = _targetSize;
                _controller.m_currentSize = _currentSize;
                _controller.enabled = _controllerEnabled;
            });
            _effects.Clear();
            _effectsEnabled.Clear();
            _camera = null;
            _controller = null;
            return problems;
        }

        private static string Try(string problems, string what, Action restore)
        {
            try
            {
                restore();
                return problems;
            }
            catch (Exception e)
            {
                return (problems == null ? "" : problems + "; ") + what + ": " + e.Message;
            }
        }
    }
}
