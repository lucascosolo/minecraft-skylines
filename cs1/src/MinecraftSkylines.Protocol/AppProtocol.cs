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
        public const ushort Minor = 21;
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
        /// <summary>Message type of <see cref="BlockAtlas"/> (guest to host, minor 2).</summary>
        public const ushort BlockAtlasType = 0x0130;
        /// <summary>Message type of <see cref="AtlasRegion"/> (guest to host, minor 2).</summary>
        public const ushort AtlasRegionType = 0x0131;
        /// <summary>Message type of <see cref="SectionMesh"/> (guest to host, minor 2).</summary>
        public const ushort SectionMeshType = 0x0132;
        /// <summary>Message type of SECTIONS_CLEAR (guest to host, minor 2); empty payload.</summary>
        public const ushort SectionsClearType = 0x0133;
        /// <summary>Message type of <see cref="BlockSelection"/> (guest to host, minor 4).</summary>
        public const ushort BlockSelectionType = 0x0134;
        /// <summary>Message type of <see cref="DebugCommand"/> (host to guest, minor 2).</summary>
        public const ushort DebugCommandType = 0x01F0;
        /// <summary>Message type of <see cref="Viewport"/> (host to guest, minor 3).</summary>
        public const ushort ViewportType = 0x0140;
        /// <summary>Message type of <see cref="OverlayOffer"/> (guest to host, minor 3).</summary>
        public const ushort OverlayOfferType = 0x0141;
        /// <summary>Message type of OVERLAY_STOP (guest to host, minor 3); empty payload.</summary>
        public const ushort OverlayStopType = 0x0142;
        /// <summary>Message type of <see cref="CityOpen"/> (host to guest, minor 5).</summary>
        public const ushort CityOpenType = 0x0150;
        /// <summary>Message type of <see cref="BlockEdits"/> (both directions, minor 5).</summary>
        public const ushort BlockEditsType = 0x0151;
        /// <summary>Message type of <see cref="CityClose"/> (host to guest, minor 5).</summary>
        public const ushort CityCloseType = 0x0152;
        /// <summary>Message type of <see cref="EditSync"/> EDIT_SYNC (host to guest, minor 5).</summary>
        public const ushort EditSyncType = 0x0153;
        /// <summary>Message type of <see cref="EditSync"/> EDIT_SYNC_ACK (guest to host, minor 5).</summary>
        public const ushort EditSyncAckType = 0x0154;
        /// <summary>Message type of <see cref="CityStateUpdate"/> CITY_STATE (guest to host, minor 5).</summary>
        public const ushort CityStateType = 0x0155;

        /// <summary>Message type of <see cref="WorldTime"/> WORLD_TIME (host to guest, minor 6).</summary>
        public const ushort WorldTimeType = 0x0160;
        /// <summary>Message type of <see cref="TimeSet"/> TIME_SET (guest to host, minor 16).</summary>
        public const ushort TimeSetType = 0x0161;

        /// <summary>Message type of <see cref="DynamicObstacles"/> DYNAMIC_OBSTACLES (host to guest, minor 7).</summary>
        public const ushort DynamicObstaclesType = 0x0170;
        /// <summary>Message type of <see cref="DynamicObstacles"/> SHAPED_OBSTACLES (host to guest, minor 17).</summary>
        public const ushort ShapedObstaclesType = 0x0171;

        /// <summary>Message type of <see cref="LightSources"/> LIGHT_SOURCES (host to guest, minor 8).</summary>
        public const ushort LightSourcesType = 0x0180;

        /// <summary>Message type of <see cref="SkyState"/> SKY_STATE (guest to host, minor 9).</summary>
        public const ushort SkyStateType = 0x0190;
        /// <summary>Message type of <see cref="SkyTextures"/> SKY_TEXTURES (guest to host, minor 9).</summary>
        public const ushort SkyTexturesType = 0x0191;

        /// <summary>Message type of <see cref="WaterSurface"/> WATER_SURFACE (host to guest, minor 10).</summary>
        public const ushort WaterSurfaceType = 0x01A0;

        /// <summary>Message type of <see cref="PlayerData"/> PLAYER_DATA (both directions, minor 11).</summary>
        public const ushort PlayerDataType = 0x01B0;
        /// <summary>Message type of <see cref="RespawnRequest"/> RESPAWN_REQUEST (guest to host, minor 11).</summary>
        public const ushort RespawnRequestType = 0x01B1;

        /// <summary>Message type of <see cref="Trees"/> TREES (host to guest, minor 12).</summary>
        public const ushort TreesType = 0x01C0;
        /// <summary>Message type of <see cref="TreeFelled"/> TREE_FELLED (guest to host, minor 12).</summary>
        public const ushort TreeFelledType = 0x01C1;

        /// <summary>Message type of <see cref="TreeGrown"/> TREE_GROWN (guest to host, minor 15).</summary>
        public const ushort TreeGrownType = 0x01C2;

        /// <summary>Message type of <see cref="CityEntities"/> CITY_ENTITIES (both directions, minor 18).</summary>
        public const ushort CityEntitiesType = 0x01D0;
        /// <summary>Message type of <see cref="CityFocus"/> CITY_FOCUS (host to guest, minor 18).</summary>
        public const ushort CityFocusType = 0x01D1;
        /// <summary>Message type of <see cref="CitizenEvents"/> CITIZEN_EVENTS (guest to host, minor 19).</summary>
        public const ushort CitizenEventsType = 0x01D2;
        /// <summary>Message type of <see cref="CityConditions"/> CITY_CONDITIONS (host to guest, minor 21).</summary>
        public const ushort CityConditionsType = 0x0210;
        /// <summary>Message type of <see cref="OreMined"/> ORE_MINED (guest to host, minor 21).</summary>
        public const ushort OreMinedType = 0x0211;

        // ---- minor 14: entities (13 is reserved for another branch)
        /// <summary>Message type of <see cref="EntityModel"/> ENTITY_MODEL (guest to host, minor 14).</summary>
        public const ushort EntityModelType = 0x01E0;
        /// <summary>Message type of <see cref="EntityTexture"/> ENTITY_TEXTURE (guest to host, minor 14).</summary>
        public const ushort EntityTextureType = 0x01E1;
        /// <summary>Message type of <see cref="EntityStates"/> ENTITY_STATES (guest to host, minor 14).</summary>
        public const ushort EntityStatesType = 0x01E2;
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
