# Plan: landing craft from carriers (1.0.2)

Deploy landing craft from a ship we command, send them to a beach the player
picks, and pay for what they carry out of the player's allocation, priced the
way the game prices a convoy bought from the aircraft selection screen.

## What the game already has

- **Annex Class Carrier** (amphibious assault ship, well deck) and the
  **OTB-31 landing craft** (hovercraft, up to 80 t of cargo).
- `LandingCraftAI`: launched from a ship, it runs out for 10 s, sails to its
  destination, beaches if the point is on land, unloads its vehicles, then
  sails home, docks and goes back into the carrier's inventory.
- `UnitStorage`: the carrier and the craft each hold an inventory of unit
  types (counts of definitions, not live units), loaded from the mission.
  Deploying spawns the units at the well deck. A new craft fills itself from
  the carrier's stored vehicles.
- `AssaultCarrierAI` deploys everything by itself when it nears an objective,
  but only while it is neither commanded nor holding.
- Any ship, landing craft included, can be ordered somewhere with
  `UnitCommand.SetDestination`. A landing craft ordered to a point on land
  goes into its beaching routine.
- Convoy purchase: cost is the sum of each vehicle's `value` plus the value of
  its ammunition, charged to `Player.Allocation`, with a cooldown.

## What we'd build

**An Amphibious window** (new tool, only on a ship with a well deck):

- **Inventory:** landing craft aboard, and the vehicles in the hold with
  their masses.
- **Load a craft:** choose vehicles from the hold, up to 80 t, with the load
  shown against capacity.
- **Land:** pick the beach on the map. Before confirming, show where the
  craft will actually come ashore: the game snaps the point to the nearest
  sea lane and road, and we can run the same calculation first. Refuse
  points it can't reach.
- **Highlighted beaches:** while picking, mark every landing spot the craft
  can actually reach, and snap a click to the nearest one. The game's sea
  lanes and roads are public lists (`RoadNetwork.roads` / `.nodes`), so once
  per map we can walk the coast: take road points near a sea lane, run the
  same shore linecast the craft uses, and keep hits that are low and gently
  sloped. Cluster them into beaches, ideally showing only those within reach
  of this carrier.
- **Launch:** spawn one craft at the well deck, put the chosen load in it,
  and launch it. Don't use the game's deploy-everything call.
- **Status per craft:** launching / in transit / beaching / unloading /
  returning / docked. Includes the eye camera button, and a Recall option
  that sends it home, landed cargo excepted.
- **Buy:** add vehicles, or another landing craft, to the hold. Charged to
  allocation at the convoy price. Only types the player's faction fields, and
  only what fits. No refunds, the same as convoys.

**Balance**

- Every vehicle landed was paid for, either by the mission (the carrier's
  starting hold) or by the player.
- A craft lost at sea loses its load.
- Kills by vehicles we landed are credited to the player, as we already do
  for launched aircraft.
- Cargo that never leaves returns to the hold when the craft docks (the game
  does this).

**Keeping the game's AI out of the way**

- Stop `AssaultCarrierAI` auto-deploying from the ship the player commands.
- Re-issue the craft's destination before the game's 120-second command
  expiry, so it doesn't wander off to an objective mid-run.

## Doable

- Charging allocation, and convoy-style pricing.
- Spawning and launching a single craft on demand.
- Loading chosen vehicles into it.
- Ordering it to a beach.
- Tracking its state and recalling it.
- Crediting kills.

## Not doable, or not worth it

- **Well decks on ships that don't have one.** Only ships with a
  `UnitStorage` (the Annex) can launch craft. We could fake it on a flat-top,
  but it would be a cheat.
- **Landing anywhere at all.** The game's beaching needs a sea lane and a
  road near the point. We'll show and refuse unreachable points rather than
  try to write our own beaching.
- **Ordering vehicles once they're ashore, from this feature.** They become
  ordinary faction ground units driving to objectives. Commanding ground
  units is a different feature.
- **Multiplayer clients.** Mission host only, like the rest of the mod.

## Unknown until we look at a live mission

- Which prefabs actually carry a `UnitStorage`, and their `deployableTypes`
  and `MassLimit`.
- Whether the craft's own storage enforces the 80 t limit (the game's volume
  check is loose, so we'd enforce mass ourselves).
- Whether beaching at an arbitrary valid point works reliably. The craft
  holds position if it reaches its destination without touching land first.
- How the carrier AI behaves under our command while craft are out.

## Phases

1. **Survey build.** Log every ship with a `UnitStorage`: limits, deployable
   types, stored list, and which AI it has. Load a mission with an Annex and
   read the log.
2. **Launch and land.** The Amphibious window with inventory, load, beach
   preview, launch, status and recall, using the carrier's own hold only.
3. **Buying.** Vehicles and landing craft into the hold, at the convoy price.
4. **Polish.** Map markers for craft and beach points, crediting kills, and
   suppressing carrier auto-deploy.

Phases 2 and 3 are most of the work; 1 and 4 are small.
