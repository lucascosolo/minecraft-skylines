namespace MinecraftSkylines.Protocol
{
    /// <summary>Constants of the <c>minecraft-skylines</c> application protocol (protocol/minecraft-skylines-v1.md).</summary>
    public static class AppProtocol
    {
        /// <summary>The appProtocol string exchanged in HELLO/WELCOME.</summary>
        public const string Name = "minecraft-skylines";
        /// <summary>Application major version.</summary>
        public const ushort Major = 1;
        /// <summary>Application minor version.</summary>
        public const ushort Minor = 1;
        /// <summary>Message type of <see cref="HostStatus"/> (host to guest).</summary>
        public const ushort HostStatusType = 0x0100;
        /// <summary>Message type of <see cref="GuestStatus"/> (guest to host).</summary>
        public const ushort GuestStatusType = 0x0101;
        /// <summary>Message type of <see cref="EnterPlayerMode"/> (host to guest).</summary>
        public const ushort EnterPlayerModeType = 0x0110;
        /// <summary>Message type of <see cref="ExitPlayerMode"/> (host to guest).</summary>
        public const ushort ExitPlayerModeType = 0x0111;
        /// <summary>Message type of <see cref="Input"/> (host to guest).</summary>
        public const ushort InputType = 0x0112;
        /// <summary>Message type of <see cref="CollisionRegion"/> (host to guest).</summary>
        public const ushort CollisionRegionType = 0x0113;
        /// <summary>Message type of <see cref="CollisionReset"/> (host to guest).</summary>
        public const ushort CollisionResetType = 0x0114;
        /// <summary>Message type of <see cref="PlayerState"/> (guest to host).</summary>
        public const ushort PlayerStateType = 0x0120;
    }

    /// <summary>Bits of <see cref="HostStatus.Flags"/>.</summary>
    public static class HostStatusFlags
    {
        /// <summary>A city is loaded.</summary>
        public const uint InCity = 1u << 0;
        /// <summary>A city is loading.</summary>
        public const uint Loading = 1u << 1;
        /// <summary>The simulation is paused.</summary>
        public const uint SimPaused = 1u << 2;
        /// <summary>The host has handed the camera to the guest.</summary>
        public const uint PlayerMode = 1u << 3;
    }

    /// <summary>Bits of <see cref="GuestStatus.Flags"/>.</summary>
    public static class GuestStatusFlags
    {
        /// <summary>A world is loaded.</summary>
        public const uint InWorld = 1u << 0;
        /// <summary>A Minecraft GUI screen has input.</summary>
        public const uint ScreenOpen = 1u << 1;
    }
}
