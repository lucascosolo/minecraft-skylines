using System;
using System.Text;
using ColossalFramework;
using ColossalFramework.Math;
using Skylines.Host;
using Skylines.Host.Terrain;
using UnityEngine;

namespace MinecraftSkylines.Mod.Diagnostics
{
    /// <summary>
    /// Spike T1 debug keys, active only in a loaded city: Ctrl+Shift+C clips the 3×3 detail cells
    /// (12 m × 12 m) around the terrain point under the cursor, Ctrl+Shift+U restores every clip,
    /// Ctrl+Shift+I logs the surface cells at the last clip. Runtime only; nothing is saved.
    /// </summary>
    internal sealed class TerrainClipProbe
    {
        private const float RayLength = 20000f;

        private readonly HostLog _log;
        private bool _hasLast;
        private int _lastCellX;
        private int _lastCellZ;
        // Spike T1 see-through check: a red cube 3 m below each clip and a control cube on the ground
        // beside it. Unity objects only (no files, nothing saved); removed with the clips.
        private readonly System.Collections.Generic.List<GameObject> _markers = new System.Collections.Generic.List<GameObject>();

        public TerrainClipProbe(HostLog log)
        {
            _log = log;
        }

        /// <summary>Last action, for the status overlay; empty until a key is used.</summary>
        public string Status = "";

        /// <summary>Per-frame key check; wire to MainThreadPump.Updated.</summary>
        public void Update()
        {
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                return;
            }
            bool clip = Input.GetKeyDown(KeyCode.C);
            bool restore = Input.GetKeyDown(KeyCode.U);
            bool info = Input.GetKeyDown(KeyCode.I);
            if (!(clip || restore || info) || !CityState.Capture().InCity || !TerrainManager.exists)
            {
                return;
            }
            try
            {
                if (clip) Clip();
                if (restore) Restore("key");
                if (info) Diagnose();
            }
            catch (Exception e)
            {
                _log.Error("terrain clip probe", e);
                Status = "Clip probe: ERROR " + e.Message;
            }
        }

        /// <summary>Removes every clip. Pass recompute false while the level is unloading.</summary>
        public void RestoreAll(string why, bool recompute)
        {
            if (!TerrainClipMask.Exists)
            {
                return;
            }
            try
            {
                foreach (GameObject m in _markers)
                {
                    if (m != null) UnityEngine.Object.Destroy(m);
                }
                _markers.Clear();
                int n = TerrainClipMask.Instance.Clear(recompute);
                if (n > 0)
                {
                    Report("restored " + n + " clipped area(s) (" + why + ")");
                }
                _hasLast = false;
            }
            catch (Exception e)
            {
                _log.Error("terrain clip probe restore (" + why + ")", e);
            }
        }

        private void Clip()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Report("clip: no main camera");
                return;
            }
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 hit;
            if (!TerrainManager.instance.RayCast(new Segment3(ray.origin, ray.origin + ray.direction * RayLength), out hit))
            {
                Report("clip: cursor ray hit no terrain");
                return;
            }
            TerrainClipMask mask = TerrainClipMask.Instance;
            if (mask.OnError == null)
            {
                mask.OnError = (where, e) => _log.Error("terrain clip mask " + where, e);
            }
            _lastCellX = TerrainClipMask.CellIndex(hit.x);
            _lastCellZ = TerrainClipMask.CellIndex(hit.z);
            _hasLast = true;
            float cx = TerrainClipMask.CellCentre(_lastCellX);
            float cz = TerrainClipMask.CellCentre(_lastCellZ);
            float half = 1.5f * TerrainClipMask.CellSize;
            mask.Add(cx - half, cz - half, cx + half, cz + half);
            float ground = TerrainManager.instance.SampleDetailHeightSmooth(new Vector3(cx, 0f, cz));
            GameObject below = Marker(new Vector3(cx, ground - 3f, cz));
            GameObject control = Marker(new Vector3(cx + half + 3f, ground + 1f, cz));
            Report(string.Format("markers: below-hole cube at y {0:0.0} (ground {1:0.0}), control cube beside it; layer {2}, camera culling mask 0x{3:X8}, shader '{4}'",
                ground - 3f, ground, below.layer, cam.cullingMask, control.GetComponent<Renderer>().sharedMaterial.shader.name));
            Report(string.Format("clipped x {0:0.0}..{1:0.0} z {2:0.0}..{3:0.0} around hit ({4:0.0}, {5:0.0}, {6:0.0}), cells {7}±1,{8}±1, {9}; {10} area(s)",
                cx - half, cx + half, cz - half, cz + half, hit.x, hit.y, hit.z, _lastCellX, _lastCellZ, PatchState(), mask.Count));
        }

        private GameObject Marker(Vector3 position)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "MinecraftSkylines.T1Marker";
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = position;
            go.transform.localScale = new Vector3(2f, 2f, 2f);
            go.GetComponent<Renderer>().material.color = Color.red;
            _markers.Add(go);
            return go;
        }

        private void Restore(string why)
        {
            int before = TerrainClipMask.Instance.Count;
            RestoreAll(why, true);
            if (before == 0)
            {
                Report("restore: nothing clipped");
            }
        }

        private void Diagnose()
        {
            if (!_hasLast)
            {
                Report("info: nothing clipped yet");
                return;
            }
            TerrainManager tm = TerrainManager.instance;
            var sb = new StringBuilder();
            int full = 0;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = Mathf.Clamp(_lastCellX + dx, 0, TerrainClipMask.CellsPerAxis - 1);
                    int z = Mathf.Clamp(_lastCellZ + dz, 0, TerrainClipMask.CellsPerAxis - 1);
                    TerrainManager.SurfaceCell c = tm.GetSurfaceCell(x, z);
                    if (c.m_clipped == 255) full++;
                    sb.AppendFormat(" [{0},{1}] clip {2} pavA {3} pavB {4} gravel {5} ruined {6} field {7};",
                        x, z, c.m_clipped, c.m_pavementA, c.m_pavementB, c.m_gravel, c.m_ruined, c.m_field);
                }
            }
            Report(string.Format("info at x {0:0.0} z {1:0.0}: {2}/9 cells fully clipped, {3}, detail patches {4}, mask {5} area(s);{6}",
                TerrainClipMask.CellCentre(_lastCellX), TerrainClipMask.CellCentre(_lastCellZ), full, PatchState(),
                tm.m_detailPatchCount, TerrainClipMask.Instance.Count, sb));
        }

        private string PatchState()
        {
            int px = _lastCellX / TerrainClipMask.CellsPerPatch;
            int pz = _lastCellZ / TerrainClipMask.CellsPerPatch;
            TerrainPatch p = TerrainManager.instance.m_patches[pz * 9 + px];
            return "patch (" + px + "," + pz + ") simDetail " + p.m_simDetailIndex + " tmpDetail " + p.m_tmpDetailIndex
                + " rndDetail " + p.m_rndDetailIndex + (p.m_simDetailIndex == 0 ? " (NO DETAIL: clip cannot apply here)" : "");
        }

        private void Report(string line)
        {
            _log.Info("clip probe: " + line);
            Status = "Clip probe: " + (line.Length > 110 ? line.Substring(0, 110) + "..." : line);
        }
    }
}
