using System;

namespace MinecraftSkylines.Protocol
{
    /// <summary>A position in metres (CS1) or blocks (Minecraft).</summary>
    public struct Vec3d
    {
        /// <summary>X component.</summary>
        public double X;
        /// <summary>Y component (up).</summary>
        public double Y;
        /// <summary>Z component.</summary>
        public double Z;

        /// <summary>Creates a vector.</summary>
        public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>A Minecraft look direction in degrees; positive pitch looks down.</summary>
    public struct McLook
    {
        /// <summary>Yaw in [-180, 180).</summary>
        public double Yaw;
        /// <summary>Pitch in [-180, 180).</summary>
        public double Pitch;

        /// <summary>Creates a look direction.</summary>
        public McLook(double yaw, double pitch) { Yaw = yaw; Pitch = pitch; }
    }

    /// <summary>
    /// Conversion between Cities: Skylines (Unity, left-handed, metres) and Minecraft (right-handed, blocks)
    /// coordinate frames, per protocol/minecraft-skylines-v1.md. One block is one metre.
    /// </summary>
    public static class MinecraftFrame
    {
        /// <summary>Vertical offset added when going CS1 to Minecraft (0 in v1).</summary>
        public const double YOffset = 0.0;

        /// <summary>CS1 world position to Minecraft: (x, y + YOffset, -z).</summary>
        public static Vec3d CsToMc(Vec3d cs)
        {
            return new Vec3d(cs.X, cs.Y + YOffset, -cs.Z);
        }

        /// <summary>Minecraft position to CS1: (x, y - YOffset, -z).</summary>
        public static Vec3d McToCs(Vec3d mc)
        {
            return new Vec3d(mc.X, mc.Y - YOffset, -mc.Z);
        }

        /// <summary>Unity euler angles (degrees) to Minecraft yaw/pitch: yaw = wrap180(eulerY + 180), pitch = wrap180(eulerX).</summary>
        public static McLook UnityEulerToMc(double eulerX, double eulerY)
        {
            return new McLook(Wrap180(eulerY + 180.0), Wrap180(eulerX));
        }

        /// <summary>Minecraft yaw/pitch back to Unity euler angles, each in [0, 360).</summary>
        public static void McToUnityEuler(McLook look, out double eulerX, out double eulerY)
        {
            eulerX = Wrap360(look.Pitch);
            eulerY = Wrap360(look.Yaw - 180.0);
        }

        /// <summary>Wraps an angle in degrees into [-180, 180).</summary>
        public static double Wrap180(double degrees)
        {
            return degrees - 360.0 * Math.Floor((degrees + 180.0) / 360.0);
        }

        /// <summary>Wraps an angle in degrees into [0, 360).</summary>
        public static double Wrap360(double degrees)
        {
            return degrees - 360.0 * Math.Floor(degrees / 360.0);
        }
    }
}
