using System;
using ColossalFramework;
using ColossalFramework.Math;
using Skylines.Core.Geometry;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>
    /// Trees, bushes and props overlapping a world-space rectangle as boxes (<see cref="Obstacle"/>) in CS1 coordinates, clipped to
    /// the rectangle. Sources, each placed as the game renders it: tree instances (TreeManager grid, TreeInstance.RenderInstance),
    /// prop instances (PropManager grid, PropInstance.RenderInstance), building props and trees (BuildingAI.RenderProps) and
    /// network lane props and trees (NetLane.RenderInstance, called from NetSegment.RenderInstance). Boxes come from the info's
    /// generated mesh bounds (<c>TreeInfoGen.m_size</c>, <c>PropInfoGen.m_center/m_size</c>). Main thread only.
    /// </summary>
    public sealed class ObstacleGeometry
    {
        /// <summary>Flags value of a tree or bush triangle (bit 6).</summary>
        public const ushort VegetationFlag = 1 << 6;
        /// <summary>Flags value of a prop triangle (bit 7).</summary>
        public const ushort PropFlag = 1 << 7;
        /// <summary>Most vegetation and prop triangles one region carries; beyond it the smallest are dropped.</summary>
        public const int VegetationBudget = 1500, PropBudget = 2000;
        /// <summary>How far past the region's edges triangles are kept, in metres.</summary>
        public const float ClipMargin = 0.05f;

        private const float Reach = 16f;            // widest box searched for past the region, metres
        private const float TreeCell = 32f;         // TreeManager.TREEGRID_CELL_SIZE
        private const int TreeGridSize = 540;       // TreeManager.TREEGRID_RESOLUTION
        private const float PropCell = 64f;         // PropManager.PROPGRID_CELL_SIZE
        private const int PropGridSize = 270;       // PropManager.PROPGRID_RESOLUTION
        private const float BuildingMargin = 128f;  // as BuildingGeometry
        private const float NetMargin = 256f;       // as NetGeometry
        private const ushort InstanceCreated = 1, InstanceDeleted = 2, InstanceHidden = 4, PropBlocked = 0x40;

        private readonly TriangleBuffer _veg = new TriangleBuffer();
        private readonly TriangleBuffer _props = new TriangleBuffer();
        private float _x0, _z0, _x1, _z1;

        /// <summary>Trees/bushes and props emitted (before clipping) by the last <see cref="Emit"/>.</summary>
        public int LastTreeCount { get; private set; }
        /// <summary>See <see cref="LastTreeCount"/>.</summary>
        public int LastPropCount { get; private set; }
        /// <summary>Triangles the last <see cref="Emit"/> dropped to stay within the budgets.</summary>
        public int LastDropped { get; private set; }

        /// <summary>Appends every tree, bush and prop box inside the rectangle to <paramref name="into"/>.</summary>
        public void Emit(float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            _veg.Clear();
            _props.Clear();
            LastTreeCount = LastPropCount = 0;
            _x0 = minX; _z0 = minZ; _x1 = maxX; _z1 = maxZ;
            Trees();
            Props();
            BuildingProps();
            LaneProps();
            int start = into.Count;
            Clip(_veg, into);
            LastDropped = TriangleBudget.KeepLargest(into, start, VegetationBudget);
            start = into.Count;
            Clip(_props, into);
            LastDropped += TriangleBudget.KeepLargest(into, start, PropBudget);
        }

        private void Clip(TriangleBuffer from, TriangleBuffer into)
        {
            float[] p = from.Positions;
            ushort[] f = from.Flags;
            for (int i = 0, o = 0; i < from.Count; i++, o += 9)
            {
                RectClip.Triangle(p[o], p[o + 1], p[o + 2], p[o + 3], p[o + 4], p[o + 5], p[o + 6], p[o + 7], p[o + 8], f[i],
                    _x0 - ClipMargin, _z0 - ClipMargin, _x1 + ClipMargin, _z1 + ClipMargin, into);
            }
        }

        private bool Near(Vector3 p, float r)
        {
            return p.x + r >= _x0 && p.x - r <= _x1 && p.z + r >= _z0 && p.z - r <= _z1;
        }

        private void AddTree(TreeInfo info, Vector3 p, float scale)
        {
            if (info == null || info.m_generatedInfo == null) return;
            Vector3 s = info.m_generatedInfo.m_size;
            if (!Near(p, 0.5f * Mathf.Max(s.x, s.z) * scale)) return;
            if (Obstacle.Tree(p.x, p.y, p.z, s.x, s.y, s.z, scale, VegetationFlag, _veg) > 0) LastTreeCount++;
        }

        private void AddProp(PropInfo info, Vector3 p, float angle, float scale)
        {
            // Decals and markers are flat or invisible; renderer-less props are effects only; water-map props float on water.
            if (info == null || info.m_isDecal || info.m_isMarker || !info.m_hasRenderer || info.m_requireWaterMap || info.m_generatedInfo == null) return;
            Vector3 c = info.m_generatedInfo.m_center, s = info.m_generatedInfo.m_size;
            if (!Near(p, (Mathf.Abs(c.x) + Mathf.Abs(c.z) + Mathf.Max(s.x, s.z)) * scale)) return;
            if (Obstacle.Prop(p.x, p.y, p.z, angle, c.x, c.y, c.z, s.x, s.y, s.z, scale, PropFlag, _props) > 0) LastPropCount++;
        }

        // TreeManager.InitializeTree files a tree under cell (posX + 32768) * 540 / 65536 with posX = x * 3.7925925, i.e. x / 32 + 270.
        private void Trees()
        {
            TreeManager tm = Singleton<TreeManager>.instance;
            TreeInstance[] trees = tm.m_trees.m_buffer;
            uint[] grid = tm.m_treeGrid;
            for (int cz = TCell(_z0 - Reach); cz <= TCell(_z1 + Reach); cz++)
            {
                for (int cx = TCell(_x0 - Reach); cx <= TCell(_x1 + Reach); cx++)
                {
                    uint id = grid[cz * TreeGridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < TreeManager.MAX_TREE_COUNT)
                    {
                        Tree(id, ref trees[id]);
                        id = trees[id].m_nextGridTree;
                    }
                }
            }
        }

        // TreeInstance.RenderInstance: drawn when GrowState != 0 and not Hidden; scale from Randomizer(treeID).
        private void Tree(uint id, ref TreeInstance t)
        {
            if ((t.m_flags & (InstanceCreated | InstanceDeleted | InstanceHidden)) != InstanceCreated || t.GrowState == 0) return;
            TreeInfo info = t.Info;
            if (info == null) return;
            var r = new Randomizer(id);
            AddTree(info, t.Position, info.m_minScale + r.Int32(10000u) * (info.m_maxScale - info.m_minScale) * 0.0001f);
        }

        private static int TCell(float v)
        {
            return Mathf.Clamp((int)(v / TreeCell + TreeGridSize / 2), 0, TreeGridSize - 1);
        }

        private static int PCell(float v)
        {
            return Mathf.Clamp((int)(v / PropCell + PropGridSize / 2), 0, PropGridSize - 1);
        }

        private void Props()
        {
            PropManager pm = Singleton<PropManager>.instance;
            PropInstance[] props = pm.m_props.m_buffer;
            ushort[] grid = pm.m_propGrid;
            for (int cz = PCell(_z0 - Reach); cz <= PCell(_z1 + Reach); cz++)
            {
                for (int cx = PCell(_x0 - Reach); cx <= PCell(_x1 + Reach); cx++)
                {
                    ushort id = grid[cz * PropGridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < PropManager.MAX_PROP_COUNT)
                    {
                        Prop(id, ref props[id]);
                        id = props[id].m_nextGridProp;
                    }
                }
            }
        }

        // PropInstance.RenderInstance: skipped when Hidden or Blocked (flags & 0x44); scale from Randomizer(propID); angle Angle.
        private void Prop(ushort id, ref PropInstance p)
        {
            if ((p.m_flags & (InstanceCreated | InstanceDeleted)) != InstanceCreated || (p.m_flags & (InstanceHidden | PropBlocked)) != 0) return;
            PropInfo info = p.Info;
            if (info == null) return;
            var r = new Randomizer(id);
            AddProp(info, p.Position, p.Angle, info.m_minScale + r.Int32(10000u) * (info.m_maxScale - info.m_minScale) * 0.0001f);
        }

        private void BuildingProps()
        {
            BuildingManager bm = Singleton<BuildingManager>.instance;
            Building[] buildings = bm.m_buildings.m_buffer;
            ushort[] grid = bm.m_buildingGrid;
            for (int cz = PCell(_z0 - BuildingMargin); cz <= PCell(_z1 + BuildingMargin); cz++)
            {
                for (int cx = PCell(_x0 - BuildingMargin); cx <= PCell(_x1 + BuildingMargin); cx++)
                {
                    ushort id = grid[cz * PropGridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < BuildingManager.MAX_BUILDING_COUNT)
                    {
                        BuildingProps(id, ref buildings[id], buildings);
                        id = buildings[id].m_nextGridBuilding;
                    }
                }
            }
        }

        // BuildingAI.RenderProps (BuildingAI.cs:463-640) with positions from BuildingAI.RefreshInstance (BuildingAI.cs:76-106).
        private void BuildingProps(ushort id, ref Building b, Building[] buildings)
        {
            const Building.Flags off = Building.Flags.Deleted | Building.Flags.Hidden | Building.Flags.Collapsed;
            if ((b.m_flags & Building.Flags.Created) == 0 || (b.m_flags & off) != 0) return;
            BuildingInfo info = b.Info;
            if (info == null || info.m_props == null) return;
            if (!Near(b.m_position, (b.Width + b.Length) * 4f + Reach)) return;
            Vector3 meshPosition;
            Quaternion rotation;
            b.CalculateMeshPosition(out meshPosition, out rotation);
            Matrix4x4 m = Matrix4x4.TRS(meshPosition, rotation, Vector3.one);
            DistrictManager dm = Singleton<DistrictManager>.instance;
            byte district = dm.GetDistrict(b.m_position);
            ushort parent = Building.FindParentBuilding(id);
            byte park = dm.GetPark(parent != 0 ? buildings[parent].m_position : b.m_position);
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            int length = b.Length;
            for (int i = 0; i < info.m_props.Length; i++)
            {
                BuildingInfo.Prop prop = info.m_props[i];
                var r = new Randomizer((id << 6) | prop.m_index);
                if (r.Int32(100u) >= prop.m_probability || length < prop.m_requiredLength) continue;
                if (prop.m_fixedHeight && info.m_isFloating) continue; // drawn on the water surface's tilted matrix
                Vector3 pos = m.MultiplyPoint(prop.m_position);
                if (!prop.m_fixedHeight) { if (!info.m_isFloating) pos.y = terrain.SampleDetailHeight(pos); }
                else if (info.m_requireHeightMap) pos.y = terrain.SampleDetailHeight(pos) + prop.m_position.y;
                if (prop.m_finalProp != null)
                {
                    PropInfo v = prop.m_finalProp.GetVariation(ref r, ref dm.m_districts.m_buffer[district], park);
                    AddProp(v, pos, b.m_angle + prop.m_radAngle, v.m_minScale + r.Int32(10000u) * (v.m_maxScale - v.m_minScale) * 0.0001f);
                }
                else if (prop.m_finalTree != null)
                {
                    TreeInfo v = prop.m_finalTree.GetVariation(ref r);
                    AddTree(v, pos, v.m_minScale + r.Int32(10000u) * (v.m_maxScale - v.m_minScale) * 0.0001f);
                }
            }
        }

        private void LaneProps()
        {
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segs = nm.m_segments.m_buffer;
            ushort[] grid = nm.m_segmentGrid;
            for (int cz = PCell(_z0 - NetMargin); cz <= PCell(_z1 + NetMargin); cz++)
            {
                for (int cx = PCell(_x0 - NetMargin); cx <= PCell(_x1 + NetMargin); cx++)
                {
                    ushort id = grid[cz * PropGridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < NetManager.MAX_SEGMENT_COUNT)
                    {
                        SegmentProps(id, ref segs[id], nm);
                        id = segs[id].m_nextGridSegment;
                    }
                }
            }
        }

        // NetSegment.RenderInstance (NetSegment.cs:423-484): node states, corner angles and inversion handed to each lane.
        private void SegmentProps(ushort id, ref NetSegment seg, NetManager nm)
        {
            const NetSegment.Flags off = NetSegment.Flags.Deleted | NetSegment.Flags.Collapsed;
            if ((seg.m_flags & NetSegment.Flags.Created) == 0 || (seg.m_flags & off) != 0) return;
            Bounds bb = seg.m_bounds;
            if (bb.max.x + Reach < _x0 || bb.min.x - Reach > _x1 || bb.max.z + Reach < _z0 || bb.min.z - Reach > _z1) return;
            NetInfo info = seg.Info;
            if (info == null || info.m_lanes == null || NetGeometry.Classify(info) == NetGeometry.Kind.Skip) return;
            NetNode[] nodes = nm.m_nodes.m_buffer;
            bool invert = info.m_netAI.ShowLanesInverted(ref seg);
            ushort first = invert ? seg.m_endNode : seg.m_startNode, second = invert ? seg.m_startNode : seg.m_endNode;
            if (nodes[first].Info == null || nodes[second].Info == null) return;
            NetNode.FlagsLong startFlags, endFlags;
            Color color;
            nodes[first].Info.m_netAI.GetNodeState(first, ref nodes[first], id, ref seg, out startFlags, out color);
            nodes[second].Info.m_netAI.GetNodeState(second, ref nodes[second], id, ref seg, out endFlags, out color);
            float startAngle = seg.m_cornerAngleStart * ((float)Math.PI / 128f), endAngle = seg.m_cornerAngleEnd * ((float)Math.PI / 128f);
            bool terrainY = info.m_segments == null || info.m_segments.Length == 0; // propIndex != -1: heights sampled from the terrain
            NetLane[] lanes = nm.m_lanes.m_buffer;
            uint lane = seg.m_lanes;
            for (int k = 0; k < info.m_lanes.Length && lane != 0; k++)
            {
                LaneProps(lane, ref lanes[lane], ref seg, info.m_lanes[k], startFlags, endFlags, startAngle, endAngle, invert, terrainY);
                lane = lanes[lane].m_nextLane;
            }
        }

        // NetLane.RenderInstance (NetLane.cs:221-420), reproduced for positions, angles, scales and the Randomizer sequence.
        private void LaneProps(uint laneID, ref NetLane lane, ref NetSegment seg, NetInfo.Lane laneInfo, NetNode.FlagsLong startFlags, NetNode.FlagsLong endFlags,
            float startAngle, float endAngle, bool invert, bool terrainY)
        {
            NetLaneProps laneProps = laneInfo.m_laneProps;
            if (laneProps == null || laneProps.m_props == null) return;
            bool backward = (laneInfo.m_finalDirection & NetInfo.Direction.Both) == NetInfo.Direction.Backward
                || (laneInfo.m_finalDirection & NetInfo.Direction.AvoidBoth) == NetInfo.Direction.AvoidForward;
            bool flip = backward != invert;
            if (backward)
            {
                NetNode.FlagsLong t = startFlags;
                startFlags = endFlags;
                endFlags = t;
            }
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            for (int i = 0; i < laneProps.m_props.Length; i++)
            {
                NetLaneProps.Prop prop = laneProps.m_props[i];
                if (lane.m_length < prop.m_minLength) continue;
                int n = 2;
                if (prop.m_repeatDistance > 1f) n *= Mathf.Max(1, Mathf.RoundToInt(lane.m_length / prop.m_repeatDistance));
                if (!prop.CheckFlags((NetLane.Flags)lane.m_flags, startFlags, endFlags)) continue;
                float offset = prop.m_segmentOffset * 0.5f;
                if (lane.m_length != 0f) offset = Mathf.Clamp(offset + prop.m_position.z / lane.m_length, -0.5f, 0.5f);
                if (flip) offset = -offset;
                PropInfo finalProp = prop.m_finalProp;
                if (finalProp != null)
                {
                    var r = new Randomizer((int)laneID + i);
                    for (int j = 1; j <= n; j += 2)
                    {
                        if (r.Int32(100u) >= prop.m_probability) continue;
                        float t = offset + j / (float)n;
                        PropInfo v = finalProp.GetVariation(ref r);
                        float scale = v.m_minScale + r.Int32(10000u) * (v.m_maxScale - v.m_minScale) * 0.0001f;
                        if (prop.m_colorMode == NetLaneProps.ColorMode.Default) v.GetColor(ref r);
                        Vector3 pos = lane.m_bezier.Position(t);
                        if (terrainY) pos.y = terrain.SampleDetailHeight(pos);
                        pos.y += prop.m_position.y;
                        Vector3 tan = lane.m_bezier.Tangent(t);
                        if (tan == Vector3.zero) continue;
                        if (flip) tan = -tan;
                        tan.y = 0f;
                        if (prop.m_position.x != 0f)
                        {
                            tan = Vector3.Normalize(tan);
                            pos.x += tan.z * prop.m_position.x;
                            pos.z -= tan.x * prop.m_position.x;
                        }
                        float angle = Mathf.Atan2(tan.x, -tan.z);
                        if (prop.m_cornerAngle != 0f || prop.m_position.x != 0f)
                        {
                            float d = Wrap(endAngle - startAngle);
                            d = Wrap(startAngle + d * t - angle);
                            angle += d * prop.m_cornerAngle;
                            if (d != 0f && prop.m_position.x != 0f)
                            {
                                float tn = Mathf.Tan(d);
                                pos.x += tan.x * tn * prop.m_position.x;
                                pos.z += tan.z * tn * prop.m_position.x;
                            }
                        }
                        angle += prop.m_angle * ((float)Math.PI / 180f);
                        if (!v.m_requireHeightMap) AddProp(v, pos, angle, scale);
                    }
                }
                TreeInfo tree = prop.m_finalTree;
                if (tree == null) continue;
                if (prop.m_upgradable && seg.TreeInfo != null) tree = seg.TreeInfo;
                var r2 = new Randomizer((int)laneID + i);
                for (int k = 1; k <= n; k += 2)
                {
                    if (r2.Int32(100u) >= prop.m_probability) continue;
                    float t = offset + k / (float)n;
                    TreeInfo v = tree.GetVariation(ref r2);
                    float scale = v.m_minRoadScale + r2.Int32(10000u) * (v.m_maxRoadScale - v.m_minRoadScale) * 0.0001f;
                    r2.Int32(10000u); // brightness
                    Vector3 pos = lane.m_bezier.Position(t);
                    if (terrainY) pos.y = terrain.SampleDetailHeight(pos);
                    pos.y += prop.m_position.y;
                    if (prop.m_position.x != 0f)
                    {
                        Vector3 tan = lane.m_bezier.Tangent(t);
                        if (flip) tan = -tan;
                        tan.y = 0f;
                        tan = Vector3.Normalize(tan);
                        pos.x += tan.z * prop.m_position.x;
                        pos.z -= tan.x * prop.m_position.x;
                    }
                    AddTree(v, pos, scale);
                }
            }
        }

        private static float Wrap(float a)
        {
            if (a > Math.PI) a -= 2f * (float)Math.PI;
            if (a < -Math.PI) a += 2f * (float)Math.PI;
            return a;
        }
    }
}
