using ColossalFramework;
using UnityEngine;
using UCamera = UnityEngine.Camera;
using UInput = UnityEngine.Input;

namespace Skylines.Host.Ui
{
    /// <summary>
    /// The CS1 tool that is current while something is dragged from a <see cref="MapDropButton"/>: it
    /// raycasts the city under the cursor (terrain, nets, buildings) the way vanilla tools do (ray in
    /// <c>OnToolUpdate</c>, <c>ToolBase.RayCast</c> in <c>SimulationStep</c>), draws a ground marker at
    /// the hit and the dragged figure at the cursor. It decides nothing; the button reads <see cref="TryGetHit"/>.
    /// </summary>
    public sealed class MapDropTool : ToolBase
    {
        private const float MarkerSize = 10f;
        private const float FigureHeight = 40f;

        private readonly object _sync = new object();
        private Ray _ray;
        private float _rayLength;
        private bool _rayValid;
        private Vector3 _hit;
        private bool _hasHit;

        /// <summary>Figure drawn at the cursor (feet on the hotspot); null draws nothing.</summary>
        internal Texture Figure;

        /// <summary>The figure's normalised rectangle inside <see cref="Figure"/>.</summary>
        internal Rect FigureUv;

        /// <summary>Width / height of the figure sprite.</summary>
        internal float FigureAspect = 1f;

        /// <summary>The latest city point under the cursor; false over UI, off the map or before the first raycast.</summary>
        public bool TryGetHit(out Vector3 hit)
        {
            lock (_sync)
            {
                hit = _hit;
                return _hasHit;
            }
        }

        /// <inheritdoc />
        protected override void OnToolUpdate()
        {
            UCamera cam = UCamera.main;
            bool valid = cam != null && !m_toolController.IsInsideUI;
            lock (_sync)
            {
                _rayValid = valid;
                if (!valid)
                {
                    _hasHit = false;
                    return;
                }
                _ray = cam.ScreenPointToRay(UInput.mousePosition);
                _rayLength = cam.farClipPlane;
            }
        }

        /// <inheritdoc />
        public override void SimulationStep()
        {
            Ray ray;
            float length;
            lock (_sync)
            {
                if (!_rayValid) return;
                ray = _ray;
                length = _rayLength;
            }
            var input = new RaycastInput(ray, length)
            {
                m_ignoreTerrain = false,
                m_ignoreNodeFlags = NetNode.Flags.None,
                m_ignoreSegmentFlags = NetSegment.Flags.None,
                m_ignoreBuildingFlags = Building.Flags.None,
            };
            RaycastOutput output;
            bool hit = RayCast(input, out output);
            lock (_sync)
            {
                if (!_rayValid) return;
                _hit = output.m_hitPos;
                _hasHit = hit;
            }
        }

        /// <inheritdoc />
        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            Vector3 hit;
            if (!TryGetHit(out hit)) return;
            Color color = GetToolColor(false, false);
            OverlayEffect overlay = Singleton<RenderManager>.instance.OverlayEffect;
            overlay.DrawCircle(cameraInfo, color, hit, MarkerSize, hit.y - 4f, hit.y + 4f, false, true);
            overlay.DrawCircle(cameraInfo, color, hit, MarkerSize * 0.3f, hit.y - 4f, hit.y + 4f, false, true);
        }

        /// <inheritdoc />
        protected override void OnToolGUI(Event e)
        {
            if (Figure == null || e.type != EventType.Repaint) return;
            Vector3 m = UInput.mousePosition;
            float w = FigureHeight * FigureAspect;
            GUI.DrawTextureWithTexCoords(new Rect(m.x - w / 2f, Screen.height - m.y - FigureHeight, w, FigureHeight), Figure, FigureUv);
        }

        /// <inheritdoc />
        protected override void OnDisable()
        {
            base.OnDisable();
            lock (_sync)
            {
                _rayValid = false;
                _hasHit = false;
            }
        }
    }
}
