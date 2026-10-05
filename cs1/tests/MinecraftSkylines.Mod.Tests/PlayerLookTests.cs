using MinecraftSkylines.Mod;
using MinecraftSkylines.Protocol;
using Xunit;

namespace MinecraftSkylines.Mod.Tests
{
    public class PlayerLookTests
    {
        private const double Tol = 1e-9;

        [Theory]
        [InlineData(-90, 270)]
        [InlineData(725, 5)]
        [InlineData(360, 0)]
        [InlineData(0, 0)]
        public void Constructor_wraps_yaw(double input, double expected)
        {
            Assert.Equal(expected, new PlayerLook(input, 0).Yaw, Tol);
        }

        [Theory]
        [InlineData(120, 90)]
        [InlineData(-200, -90)]
        [InlineData(45, 45)]
        public void Constructor_clamps_pitch(double input, double expected)
        {
            Assert.Equal(expected, new PlayerLook(0, input).Pitch, Tol);
        }

        [Fact]
        public void Apply_mouse_right_increases_yaw()
        {
            var look = new PlayerLook(10, 0);
            look.Apply(5, 0, 1);
            Assert.Equal(15, look.Yaw, Tol);
        }

        [Fact]
        public void Apply_mouse_up_decreases_pitch()
        {
            var look = new PlayerLook(0, 0);
            look.Apply(0, 10, 1);
            Assert.Equal(-10, look.Pitch, Tol);
            look.Apply(0, -25, 1);
            Assert.Equal(15, look.Pitch, Tol);
        }

        [Fact]
        public void Apply_scales_by_degrees_per_unit()
        {
            var look = new PlayerLook(0, 0);
            look.Apply(4, -2, 0.25);
            Assert.Equal(1, look.Yaw, Tol);
            Assert.Equal(0.5, look.Pitch, Tol);
        }

        [Fact]
        public void Apply_clamps_pitch_at_exactly_limits()
        {
            var look = new PlayerLook(0, 0);
            look.Apply(0, -100000, 1);
            Assert.Equal(90, look.Pitch);
            look.Apply(0, 100000, 1);
            Assert.Equal(-90, look.Pitch);
        }

        [Fact]
        public void Apply_pitch_recovers_after_clamp()
        {
            var look = new PlayerLook(0, 0);
            look.Apply(0, -500, 1);
            Assert.Equal(90, look.Pitch, Tol);
            look.Apply(0, 10, 1);
            Assert.Equal(80, look.Pitch, Tol);
        }

        [Fact]
        public void Apply_wraps_yaw_forward_across_360()
        {
            var look = new PlayerLook(350, 0);
            look.Apply(20, 0, 1);
            Assert.Equal(10, look.Yaw, Tol);
        }

        [Fact]
        public void Apply_wraps_yaw_backward_across_0()
        {
            var look = new PlayerLook(10, 0);
            look.Apply(-20, 0, 1);
            Assert.Equal(350, look.Yaw, Tol);
        }

        [Theory]
        [InlineData(0, 0, -180, 0)]
        [InlineData(90, 0, -90, 0)]
        [InlineData(0, 30, -180, 30)]
        [InlineData(0, -45, -180, -45)]
        public void ToMc_converts_unity_euler(double yaw, double pitch, double mcYaw, double mcPitch)
        {
            McLook mc = new PlayerLook(yaw, pitch).ToMc();
            Assert.Equal(mcYaw, mc.Yaw, Tol);
            Assert.Equal(mcPitch, mc.Pitch, Tol);
        }
    }

    public class PlayerPoseTests
    {
        private const double Tol = 1e-9;

        [Fact]
        public void FeetCsToMc_negates_z_only()
        {
            Vec3d mc = PlayerPose.FeetCsToMc(new Vec3d(1, 2, 3));
            Assert.Equal(1, mc.X, Tol);
            Assert.Equal(2, mc.Y, Tol);
            Assert.Equal(-3, mc.Z, Tol);
        }

        [Fact]
        public void Feet_and_McToCs_round_trip()
        {
            var cs = new Vec3d(-12.5, 60.25, 700.125);
            Vec3d back = PlayerPose.McToCs(PlayerPose.FeetCsToMc(cs));
            Assert.Equal(cs.X, back.X, Tol);
            Assert.Equal(cs.Y, back.Y, Tol);
            Assert.Equal(cs.Z, back.Z, Tol);
        }

        [Fact]
        public void EyeCs_adds_height_to_y_only()
        {
            Vec3d eye = PlayerPose.EyeCs(new Vec3d(4, 10, -6), 1.62);
            Assert.Equal(4, eye.X, Tol);
            Assert.Equal(11.62, eye.Y, Tol);
            Assert.Equal(-6, eye.Z, Tol);
        }
    }
}
