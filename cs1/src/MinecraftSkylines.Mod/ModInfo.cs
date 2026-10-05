using System;
using ICities;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Entry point CS1 discovers in Addons/Mods/MinecraftSkylines/. PluginManager calls the public
    /// OnEnabled/OnDisabled methods by reflection (ColossalFramework.Plugins.PluginManager), at
    /// startup for an enabled mod and when the player toggles it.
    /// </summary>
    public sealed class ModInfo : IUserMod
    {
        /// <inheritdoc />
        public string Name
        {
            get { return "Minecraft Skylines"; }
        }

        /// <inheritdoc />
        public string Description
        {
            get { return "Play your city as a Minecraft player. In a city, Ctrl+Shift+M walks it as the Minecraft player; Esc returns."; }
        }

        /// <summary>Called by CS1 when the mod is enabled.</summary>
        public void OnEnabled()
        {
            LinkService.Start();
        }

        /// <summary>Called by CS1 when the mod is disabled.</summary>
        public void OnDisabled()
        {
            LinkService.Stop("mod disabled");
        }
    }

    /// <summary>Tracks whether a city is loaded and assigns the save identity.</summary>
    public sealed class Loading : LoadingExtensionBase
    {
        /// <inheritdoc />
        public override void OnLevelLoaded(LoadMode mode)
        {
            try { LinkService.OnLevelLoaded(mode.ToString()); }
            catch (Exception e) { LinkService.LogError("OnLevelLoaded", e); }
        }

        /// <inheritdoc />
        public override void OnLevelUnloading()
        {
            try { LinkService.OnLevelUnloading(); }
            catch (Exception e) { LinkService.LogError("OnLevelUnloading", e); }
        }
    }

    /// <summary>Stores and restores the save identity inside the city save.</summary>
    public sealed class SaveData : SerializableDataExtensionBase
    {
        /// <inheritdoc />
        public override void OnLoadData()
        {
            try
            {
                LinkService.OnLoadData(serializableDataManager);
            }
            catch (Exception e)
            {
                LinkService.LogError("OnLoadData", e);
            }
        }

        /// <inheritdoc />
        public override void OnSaveData()
        {
            try
            {
                LinkService.OnSaveData(serializableDataManager);
            }
            catch (Exception e)
            {
                LinkService.LogError("OnSaveData", e);
            }
        }
    }
}
