using System;
using System.Collections.Generic;

namespace Skylines.Core.Geometry
{
    public sealed class TriangleBuffer
    {
        public void Add(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, ushort flags) { throw new NotImplementedException(); }
        public int Count { get { throw new NotImplementedException(); } }
        public float[] Positions { get { throw new NotImplementedException(); } }
        public ushort[] Flags { get { throw new NotImplementedException(); } }
        public void Clear() { throw new NotImplementedException(); }
        public void ReverseWinding() { throw new NotImplementedException(); }
    }

    public static class Heightfield
    {
        public static void Triangulate(Func<float, float, float> sampleHeight, float minX, float minZ, float maxX, float maxZ, float step, ushort flags, TriangleBuffer into) { throw new NotImplementedException(); }
    }

    public struct Bezier3D
    {
        public float Ax, Ay, Az, Bx, By, Bz, Cx, Cy, Cz, Dx, Dy, Dz;
    }

    public static class Strip
    {
        public static void Road(Bezier3D curve, float halfWidth, float step, float thickness, ushort flags, TriangleBuffer into) { throw new NotImplementedException(); }
    }

    public static class Disc
    {
        public static void Fan(float cx, float y, float cz, float radius, int segments, float thickness, ushort flags, TriangleBuffer into) { throw new NotImplementedException(); }
    }
}

namespace Skylines.Core.Streaming
{
    public sealed class RegionGrid
    {
        public RegionGrid(float size) { throw new NotImplementedException(); }
        public void RegionOf(float x, float z, out int rx, out int rz) { throw new NotImplementedException(); }
        public void Bounds(int rx, int rz, out float minX, out float minZ, out float maxX, out float maxZ) { throw new NotImplementedException(); }
        public List<long> RegionsWithin(float x, float z, float radius) { throw new NotImplementedException(); }
        public static long Key(int rx, int rz) { throw new NotImplementedException(); }
        public static void Unkey(long key, out int rx, out int rz) { throw new NotImplementedException(); }
    }

    public sealed class RegionStreamPlanner
    {
        public RegionStreamPlanner(RegionGrid grid, float radius, float evictRadius) { throw new NotImplementedException(); }
        public uint Epoch { get { throw new NotImplementedException(); } }
        public List<long> Next(float x, float z, int budget) { throw new NotImplementedException(); }
        public void Invalidate(int rx, int rz) { throw new NotImplementedException(); }
        public void Reset() { throw new NotImplementedException(); }
        public bool IsSent(int rx, int rz) { throw new NotImplementedException(); }
    }
}

namespace Skylines.Core.Motion
{
    public struct Vec3d
    {
        public double X, Y, Z;
    }

    public sealed class TickInterpolator
    {
        public TickInterpolator(double extraDelayMs = 10) { throw new NotImplementedException(); }
        public void Push(uint tickSeq, Vec3d pos, float eyeHeight, double arrivalMs, float tickMs) { throw new NotImplementedException(); }
        public bool Sample(double nowMs, out Vec3d pos, out float eyeHeight) { throw new NotImplementedException(); }
        public void Reset() { throw new NotImplementedException(); }
    }
}

namespace Skylines.Core.Input
{
    public static class GlfwKeyMap
    {
        public static int FromUnityKeyCode(int unityKeyCode) { throw new NotImplementedException(); }
        public static int MouseButtonFromUnityKeyCode(int unityKeyCode) { throw new NotImplementedException(); }
    }
}
