using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace Skylines.Host.Citizens
{
    /// <summary>
    /// CS1's own panic and death for a walking citizen (a <c>CitizenInstance</c> drawn as a character), for anything
    /// outside the city that hurts citizens. Simulation thread only (<c>SimulationManager.AddAction</c>).
    /// Sources and reasoning: docs/CS1-API-NOTES.md, "Citizens as villagers".
    /// </summary>
    public static class CitizenFate
    {
        private static readonly MethodInfo s_die = typeof(ResidentAI).GetMethod("Die", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(uint), typeof(Citizen).MakeByRefType() }, null);

        /// <summary>True when <paramref name="instance"/> is a spawned walking citizen within <paramref name="radius"/> m of <paramref name="cs"/> horizontally.</summary>
        public static bool WalkingNear(ushort instance, Vector3 cs, float radius)
        {
            uint citizen;
            if (!Walking(instance, out citizen)) return false;
            Vector3 p = Singleton<CitizenManager>.instance.m_instances.m_buffer[instance].GetLastFramePosition();
            float dx = p.x - cs.x, dz = p.z - cs.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>
        /// Panics the citizen the way a burning target does (<c>CitizenInstance.Flags.Panicking</c>) and, for a resident
        /// with a home, sends it home through its own AI. Returns what happened, for the log.
        /// </summary>
        public static string Panic(ushort instance)
        {
            uint citizen;
            if (!Walking(instance, out citizen)) return "not a walking citizen";
            CitizenManager cm = Singleton<CitizenManager>.instance;
            CitizenInstance[] buf = cm.m_instances.m_buffer;
            ushort home = cm.m_citizens.m_buffer[citizen].m_homeBuilding;
            CitizenAI ai = buf[instance].Info.m_citizenAI;
            string what = "panicking";
            if (ai is ResidentAI && home != 0 && buf[instance].m_targetBuilding != home)
            {
                ai.SetTarget(instance, ref buf[instance], home);
                what += ", heading home to building " + home;
            }
            buf[instance].m_flags |= CitizenInstance.Flags.Panicking;
            return what;
        }

        /// <summary>
        /// Kills the citizen through the city's death path: a resident with a home dies by <c>ResidentAI.Die</c> (death
        /// statistics), its body is placed at home and its instance released, so <c>ResidentAI</c> calls a hearse there;
        /// anyone else is released as CS1 releases a dead citizen with nowhere to go. Returns what happened, for the log.
        /// </summary>
        public static string Kill(ushort instance)
        {
            uint citizen;
            if (!Walking(instance, out citizen)) return "not a walking citizen";
            CitizenManager cm = Singleton<CitizenManager>.instance;
            Citizen[] people = cm.m_citizens.m_buffer;
            if (people[citizen].Dead) return "already dead";
            CitizenAI ai = cm.m_instances.m_buffer[instance].Info.m_citizenAI;
            ushort home = people[citizen].m_homeBuilding;
            if (!(ai is ResidentAI) || s_die == null || home == 0 || (people[citizen].m_flags & Citizen.Flags.Tourist) != 0)
            {
                cm.ReleaseCitizen(citizen);
                return "citizen " + citizen + " released (no home to take the body to" + (s_die == null ? "; ResidentAI.Die not found" : "") + ")";
            }
            object[] args = { citizen, people[citizen] };
            s_die.Invoke(ai, args);
            people[citizen] = (Citizen)args[1];
            people[citizen].CurrentLocation = Citizen.Location.Home;
            cm.ReleaseCitizenInstance(instance);
            return "citizen " + citizen + " died; body at home building " + home + " for a hearse";
        }

        private static bool Walking(ushort instance, out uint citizen)
        {
            citizen = 0;
            CitizenManager cm = Singleton<CitizenManager>.instance;
            CitizenInstance[] buf = cm.m_instances.m_buffer;
            if (instance == 0 || instance >= buf.Length) return false;
            const CitizenInstance.Flags want = CitizenInstance.Flags.Created | CitizenInstance.Flags.Character;
            if ((buf[instance].m_flags & (want | CitizenInstance.Flags.Deleted)) != want || buf[instance].Info == null) return false;
            citizen = buf[instance].m_citizen;
            return citizen != 0;
        }
    }
}
