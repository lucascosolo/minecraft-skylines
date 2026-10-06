using ColossalFramework;
using Skylines.Host;
using Skylines.Host.Saves;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Asked before entering Minecraft mode: on a paused city, a dialog says the game will be unpaused (the streets stay
    /// empty while paused; owner, 2026-10-06: "the game was paused the whole session ... pop up a warning that says the
    /// game will be unpaused for Minecraft play mode"). Yes unpauses and repeats the entry request (a dropped spot is
    /// kept); No leaves everything as it was. Main thread only.
    /// </summary>
    internal sealed class PauseGate
    {
        private readonly HostLog _log;
        private readonly PlayerMode _player;
        private bool _asking;

        public PauseGate(HostLog log, PlayerMode player)
        {
            _log = log;
            _player = player;
        }

        /// <summary>Null to proceed, else the note to show while the dialog is open.</summary>
        public string Check()
        {
            if (!SimulationManager.exists || !Singleton<SimulationManager>.instance.SimulationPaused) return null;
            if (!_asking)
            {
                _asking = true;
                _log.Info("player mode: the city is paused; asking to unpause");
                GameDialogs.Confirm("Minecraft play mode",
                    "The game is paused. Minecraft play mode runs with the city going, so the game will be unpaused. Continue?",
                    yes =>
                    {
                        _asking = false;
                        if (!yes)
                        {
                            _log.Info("player mode: stayed paused; not entering");
                            return;
                        }
                        Singleton<SimulationManager>.instance.SimulationPaused = false;
                        _log.Info("player mode: unpaused the city for Minecraft play mode");
                        _player.RequestEnter();
                    });
            }
            return "confirm in the dialog";
        }
    }
}
