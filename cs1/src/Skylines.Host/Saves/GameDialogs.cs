using System;
using ColossalFramework.UI;

namespace Skylines.Host.Saves
{
    /// <summary>The game's own modal dialogs. Main thread only.</summary>
    public static class GameDialogs
    {
        /// <summary>Yes/No dialog (ConfirmPanel); <paramref name="answered"/> gets true for Yes.</summary>
        public static void Confirm(string title, string message, Action<bool> answered)
        {
            ConfirmPanel.ShowModal(title, message, (c, result) => answered(result == 1));
        }

        /// <summary>An error message with an OK button (ExceptionPanel).</summary>
        public static void Error(string title, string message)
        {
            UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel").SetMessage(title, message, true);
        }
    }
}
