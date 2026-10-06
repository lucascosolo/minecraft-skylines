using System;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace Skylines.Host.Rendering
{
    /// <summary>
    /// Makes CS1 draw underground vehicles as on the surface: <c>Vehicle.RenderInstance</c> draws an underground vehicle
    /// with <c>VehicleInfoBase.m_undergroundMaterial</c> on <c>VehicleManager.m_undergroundLayer</c> (MetroTunnels), and
    /// <c>Vehicle.RenderUndergroundLod</c> with <c>m_undergroundLodMaterial</c>. While active, every loaded vehicle
    /// (and sub-mesh) gets its surface material and LOD material in those fields and the underground layer becomes the
    /// cars' own layer, so they are depth-tested against the terrain and lit like surface cars. <see cref="End"/> puts
    /// back every recorded value, including nulls. Main thread only.
    /// </summary>
    public sealed class UndergroundVehicles
    {
        private struct Saved
        {
            public VehicleInfoBase Info;
            public Material Material, Lod;
        }

        private readonly HostLog _log;
        private readonly List<Saved> _saved = new List<Saved>();
        private readonly HashSet<VehicleInfoBase> _seen = new HashSet<VehicleInfoBase>();
        private int _savedLayer;
        private bool _active;

        /// <summary>Creates an inactive override that logs to <paramref name="log"/>.</summary>
        public UndergroundVehicles(HostLog log)
        {
            _log = log;
        }

        /// <summary>True between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public bool Active { get { return _active; } }

        /// <summary>Swaps the materials and layer. Idempotent; does nothing (logged) without a car prefab to take the layer from.</summary>
        public void Begin()
        {
            if (_active || !VehicleManager.exists) return;
            int layer = CarLayer();
            if (layer < 0)
            {
                _log.Warn("underground cars: no loaded car prefab with a layer; left as CS1 draws them");
                return;
            }
            VehicleManager vm = Singleton<VehicleManager>.instance;
            _savedLayer = vm.m_undergroundLayer;
            _active = true;
            int n = PrefabCollection<VehicleInfo>.LoadedCount();
            for (uint i = 0; i < n; i++)
            {
                VehicleInfo info = PrefabCollection<VehicleInfo>.GetLoaded(i);
                if (info == null) continue;
                Swap(info);
                if (info.m_subMeshes == null) continue;
                foreach (VehicleInfo.MeshInfo m in info.m_subMeshes)
                {
                    if (m != null && m.m_subInfo != null) Swap(m.m_subInfo);
                }
            }
            vm.m_undergroundLayer = layer;
            _log.Info("underground cars: " + _saved.Count + " vehicle meshes drawn with their surface materials on layer " + layer
                + " (was " + _savedLayer + ")");
        }

        /// <summary>Restores every recorded material and the layer. Idempotent, never throws.</summary>
        public void End()
        {
            if (!_active) return;
            _active = false;
            try
            {
                if (VehicleManager.exists) Singleton<VehicleManager>.instance.m_undergroundLayer = _savedLayer;
            }
            catch (Exception e)
            {
                _log.Error("underground cars: restore layer", e);
            }
            for (int i = _saved.Count - 1; i >= 0; i--)
            {
                try
                {
                    Saved s = _saved[i];
                    if ((object)s.Info == null) continue;
                    s.Info.m_undergroundMaterial = s.Material;
                    s.Info.m_undergroundLodMaterial = s.Lod;
                }
                catch (Exception e)
                {
                    _log.Error("underground cars: restore material", e);
                }
            }
            _saved.Clear();
            _seen.Clear();
        }

        private void Swap(VehicleInfoBase info)
        {
            if (info.m_material == null || !_seen.Add(info)) return;
            _saved.Add(new Saved { Info = info, Material = info.m_undergroundMaterial, Lod = info.m_undergroundLodMaterial });
            info.m_undergroundMaterial = info.m_material;
            if (info.m_lodMaterialCombined != null) info.m_undergroundLodMaterial = info.m_lodMaterialCombined;
        }

        private static int CarLayer()
        {
            int n = PrefabCollection<VehicleInfo>.LoadedCount();
            for (uint i = 0; i < n; i++)
            {
                VehicleInfo info = PrefabCollection<VehicleInfo>.GetLoaded(i);
                if (info != null && info.m_vehicleType == VehicleInfo.VehicleType.Car && info.m_prefabDataLayer >= 0) return info.m_prefabDataLayer;
            }
            return -1;
        }
    }
}
