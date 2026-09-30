using System;
using System.Collections.Generic;
using UnityEngine;
using NOrders;

namespace NavalPower
{
    // Ships another mod holds, asked for on the player's behalf. The request
    // goes on the ship (Ownership.RequestHandover); the holding mod hands it
    // over on its next update, and what the player wanted done with it --
    // join a force, take command -- happens the moment it is ours. A mod too
    // old to know the request never answers: after a few seconds the request
    // is withdrawn and the player told.
    internal static class Handovers
    {
        private sealed class Pending
        {
            internal Ship Ship;
            internal string Holder;
            internal Action Then;
            internal float Since;
        }

        private static readonly List<Pending> pending = new List<Pending>();
        private const float Patience = 8f;

        internal static string HolderName(Ship ship)
        {
            string id = Ownership.OwnerOf(ship);
            if (string.IsNullOrEmpty(id)) return "another mod";
            return id == "HighCommand" ? "High Command" : id;
        }

        // Asks for the ship and does `then` once it is ours -- at once, if it
        // already is or nobody holds it.
        internal static void Request(Ship ship, Action then)
        {
            if (ship == null) return;
            string holder = HolderName(ship);
            if (Ownership.RequestHandover(ship)) { then?.Invoke(); return; }
            pending.RemoveAll(p => p.Ship == ship);
            pending.Add(new Pending { Ship = ship, Holder = holder, Then = then, Since = Time.unscaledTime });
            CommandState.Say("Asking " + holder + " for " + ShipNames.Of(ship) + "…");
            Plugin.Log.LogInfo("[own] asked " + holder + " for " + ShipNames.Of(ship));
        }

        internal static bool Waiting(Ship ship) => pending.Exists(p => p.Ship == ship);

        internal static void Tick()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending p = pending[i];
                if (p.Ship == null || p.Ship.disabled) { pending.RemoveAt(i); continue; }
                if (Ownership.Mine(p.Ship))
                {
                    pending.RemoveAt(i);
                    Plugin.Log.LogInfo("[own] " + p.Holder + " handed over " + ShipNames.Of(p.Ship));
                    CommandState.Say(p.Holder + " handed over " + ShipNames.Of(p.Ship));
                    try { p.Then?.Invoke(); } catch (Exception ex) { Plugin.Log.LogWarning("[own] after handover: " + ex.Message); }
                    continue;
                }
                if (Ownership.OwnerOf(p.Ship) == null)
                {
                    // Released rather than handed across: take it while it is free.
                    if (Ownership.Claim(p.Ship)) continue;       // picked up as ours on the next pass
                }
                if (Time.unscaledTime - p.Since > Patience)
                {
                    pending.RemoveAt(i);
                    Ownership.WithdrawRequest(p.Ship);
                    Plugin.Log.LogWarning("[own] " + p.Holder + " did not hand over " + ShipNames.Of(p.Ship));
                    CommandState.Say(p.Holder + " did not hand over " + ShipNames.Of(p.Ship) + " · it may need updating");
                }
            }
        }
    }
}
