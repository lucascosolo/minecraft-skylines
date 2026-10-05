using System;
using UnityEngine;

namespace Skylines.Host
{
    /// <summary>
    /// A persistent Unity object that calls back on the main thread every frame, in the main menu
    /// as well as in a city (ICities' ThreadingExtensionBase only runs inside a loaded city).
    /// It survives scene changes and reports application quit.
    /// </summary>
    public sealed class MainThreadPump : MonoBehaviour
    {
        /// <summary>Called once per frame from Update.</summary>
        public event Action Updated;

        /// <summary>Called from OnGUI (possibly several times per frame).</summary>
        public event Action Gui;

        /// <summary>Called once when the application is quitting.</summary>
        public event Action Quitting;

        /// <summary>Optional sink for exceptions thrown by handlers.</summary>
        public Action<string, Exception> OnHandlerError;

        /// <summary>Creates the pump on a new GameObject that is kept across scene loads.</summary>
        public static MainThreadPump Install(string name)
        {
            var go = new GameObject(name + ".MainThreadPump");
            DontDestroyOnLoad(go);
            return go.AddComponent<MainThreadPump>();
        }

        /// <summary>Removes the pump's GameObject from the scene (a Unity object, not a file).</summary>
        public void Uninstall()
        {
            Updated = null;
            Gui = null;
            Quitting = null;
            Destroy(gameObject);
        }

        private void Update()
        {
            Invoke(Updated, "Update");
        }

        private void OnGUI()
        {
            Invoke(Gui, "OnGUI");
        }

        private void OnApplicationQuit()
        {
            Invoke(Quitting, "OnApplicationQuit");
        }

        private void Invoke(Action handlers, string where)
        {
            if (handlers == null)
            {
                return;
            }
            foreach (Action h in handlers.GetInvocationList())
            {
                try
                {
                    h();
                }
                catch (Exception e)
                {
                    if (OnHandlerError != null)
                    {
                        OnHandlerError(where, e);
                    }
                }
            }
        }
    }
}
