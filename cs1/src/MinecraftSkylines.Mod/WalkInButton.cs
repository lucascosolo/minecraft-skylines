using System;
using Skylines.Host;
using Skylines.Host.Ui;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// The "drag the figure onto the city" entry into Minecraft mode: a <see cref="MapDropButton"/> that exists
    /// while a city (not an editor) is loaded and is hidden while Minecraft mode is on. A drop asks player mode to
    /// enter at that x/z through the normal gate, so an unpaired city still gets the enable dialog and backup first.
    /// </summary>
    internal sealed class WalkInButton
    {
        private const string Tooltip = "Drag onto the city to walk around as a Minecraft player";

        private readonly HostLog _log;
        private readonly PlayerMode _player;
        private MapDropButton _button;

        public WalkInButton(HostLog log, PlayerMode player)
        {
            _log = log;
            _player = player;
        }

        public void OnLevelLoaded(string mode)
        {
            if (mode != "NewGame" && mode != "LoadGame" && mode != "NewGameFromScenario") return;
            Guard("create", () =>
            {
                var spec = new MapDropSpec
                {
                    Name = "MinecraftSkylines.WalkIn",
                    IconSprites = new[] { "InfoIconPopulation", "IconCitizenVehicle" },
                    BackgroundStems = new[] { "OptionBase", "RoundBackBig", "ButtonMenu" },
                    Tooltip = Tooltip,
                };
                _button = new MapDropButton(spec, hit => _player.RequestEnterAt(hit.x, hit.z), _log);
                string problem = _button.Create();
                if (problem != null)
                {
                    _log.Warn("walk-in button not shown: " + problem);
                    _button = null;
                }
            });
        }

        /// <summary>Per frame from the pump's Update.</summary>
        public void Update()
        {
            if (_button == null) return;
            Guard("update", () =>
            {
                _button.Visible = !_player.IsOn;
                _button.Update();
            });
        }

        /// <summary>Level unload and mod disable: destroys only what <see cref="MapDropButton"/> created.</summary>
        public void Dispose()
        {
            MapDropButton b = _button;
            _button = null;
            if (b != null) Guard("dispose", b.Dispose);
        }

        private void Guard(string what, Action step)
        {
            try { step(); }
            catch (Exception e) { _log.Error("walk-in button " + what, e); }
        }
    }
}
