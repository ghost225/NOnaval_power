namespace NavalPower
{
    // Nuclear weapons fitted to our ships -- an SLND's warheads come aboard
    // with the ship, not from an airbase -- answer to the same release rules
    // the game applies to an aircraft loading them: the mission's escalation
    // past the tactical threshold (strategic for a strategic weapon), and the
    // player's rank. And even when released, a nuclear weapon leaves one of
    // our ships only on an explicit order, never by the ship's automatic fire.
    internal static class NuclearRelease
    {
        internal static bool Authorised(WeaponInfo info, out string reason)
        {
            reason = null;
            if (info == null || !info.nuclear) return true;
            var mission = NetworkSceneSingleton<MissionManager>.i;
            if (mission == null) { reason = "Nuclear release is not authorised."; return false; }
            int rank = GameManager.GetLocalPlayer<NuclearOption.Networking.Player>(out var player) && player != null ? player.PlayerRank : 0;
            if (info.strategic)
            {
                if (!MissionManager.AllowStrategic())
                { reason = "Strategic release not authorised · escalation " + Level(mission) + " of " + mission.strategicThreshold.ToString("0"); return false; }
                if (rank < mission.strategicMinRank)
                { reason = "Strategic release needs rank " + mission.strategicMinRank.ToString("0"); return false; }
                return true;
            }
            if (!MissionManager.AllowTactical())
            { reason = "Nuclear release not authorised · escalation " + Level(mission) + " of " + mission.tacticalThreshold.ToString("0"); return false; }
            if (rank < mission.tacticalMinRank)
            { reason = "Nuclear release needs rank " + mission.tacticalMinRank.ToString("0"); return false; }
            return true;
        }

        private static string Level(MissionManager mission) => mission.currentEscalation.ToString("0");

        // Checked on every shot. Our faction's ships only; everyone else is
        // the game's business.
        internal static bool Allows(Weapon weapon, Unit owner)
        {
            WeaponInfo info = weapon != null ? weapon.info : null;
            if (info == null || !info.nuclear || !(owner is Ship ship)) return true;
            if (!GameManager.GetLocalHQ(out FactionHQ hq) || hq == null || ship.NetworkHQ != hq) return true;
            if (!ShipWeapons.Firing) return false;             // never automatic
            return Authorised(info, out _);
        }
    }
}
