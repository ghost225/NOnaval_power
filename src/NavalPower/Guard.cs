using System;
using System.Collections.Generic;

namespace NavalPower
{
    // A patch that throws does not throw once.
    //
    // Plugin.ApplyPatches already keeps a patch that cannot *attach* from
    // taking the mod down with it. This is the same principle a step later, for
    // a patch that attaches and then throws while running: on Weapon.Fire that
    // is every shot fired by anyone in the match, and on a FixedUpdateState it
    // is fifty times a second. The exception itself is rarely the worst of it.
    // The log fills, the frame rate goes, and the real fault is buried.
    //
    // So the first exception retires that one patch for the session and says so
    // once. The feature it carried is lost; nothing else is. Borrowed from
    // NOAutopilot, which does this globally -- the whole mod unpatches itself
    // on the first error. Per patch suits a mod made of many small independent
    // ones better: losing cargo missions should not cost you damage control.
    internal static class Guard
    {
        private static readonly HashSet<string> retired = new HashSet<string>();

        internal static bool Ok(string patch) => !retired.Contains(patch);

        internal static void Failed(string patch, Exception ex)
        {
            if (!retired.Add(patch)) return;
            Plugin.Log.LogError(patch + " threw and is now disabled for the rest of this session; " +
                "the rest of the mod is unaffected. " + ex);
        }

        // For call sites that are not themselves hot, where a closure costs
        // nothing worth counting.
        internal static void Run(string patch, Action body)
        {
            if (retired.Contains(patch)) return;
            try { body(); }
            catch (Exception ex) { Failed(patch, ex); }
        }
    }
}
