using System;
using MinecraftSkylines.Protocol;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// The player's look direction in Unity euler degrees, integrated locally from mouse deltas (CS1
    /// owns look; Minecraft copies it). Pure: no Unity, so it is unit-tested on .NET 10.
    /// </summary>
    internal sealed class PlayerLook
    {
        public const double MaxPitch = 90.0;

        public PlayerLook(double yaw, double pitch)
        {
            Set(yaw, pitch);
        }

        /// <summary>Unity euler Y in [0, 360).</summary>
        public double Yaw { get; private set; }

        /// <summary>Unity euler X in [-90, 90], positive looks down.</summary>
        public double Pitch { get; private set; }

        public void Apply(double mouseDx, double mouseDy, double degreesPerUnit)
        {
            Set(Yaw + mouseDx * degreesPerUnit, Pitch - mouseDy * degreesPerUnit);
        }

        public McLook ToMc()
        {
            return MinecraftFrame.UnityEulerToMc(Pitch, Yaw);
        }

        private void Set(double yaw, double pitch)
        {
            Yaw = MinecraftFrame.Wrap360(yaw);
            Pitch = Math.Max(-MaxPitch, Math.Min(MaxPitch, pitch));
        }
    }

    /// <summary>Position conversions the player mode needs, on top of <see cref="MinecraftFrame"/>.</summary>
    internal static class PlayerPose
    {
        public static Vec3d FeetCsToMc(Vec3d csFeet)
        {
            return MinecraftFrame.CsToMc(csFeet);
        }

        public static Vec3d McToCs(Vec3d mc)
        {
            return MinecraftFrame.McToCs(mc);
        }

        public static Vec3d EyeCs(Vec3d csFeet, double eyeHeight)
        {
            return new Vec3d(csFeet.X, csFeet.Y + eyeHeight, csFeet.Z);
        }
    }
}
