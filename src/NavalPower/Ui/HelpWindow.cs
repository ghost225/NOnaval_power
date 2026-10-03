using UnityEngine;
using NOrders;

namespace NavalPower
{
    // The quick guide: the "?" on the command bar. A tutorial in sections --
    // where each thing is, what each option does, and the keys -- readable
    // one section at a time or all on one scrolling page. Body text at full
    // brightness, wrapped; keys and labels in the accent colour.
    internal sealed partial class CommandUi
    {
        private static string Key(string keys) => UiKit.Tint(keys, Theme.Accent);

        // A paragraph of plain explanation.
        private static void P(Surface s, string text) => s.Paragraph(text, Theme.Text, Theme.CaptionSize + 1);

        // A key, button or label, then what it does.
        private static void K(Surface s, string keys, string what) => s.Paragraph(Key(keys) + "   " + what, Theme.Text, Theme.CaptionSize + 1);

        // A sub-heading inside a section.
        private static void H(Surface s, string text) => s.Info(UiKit.Tint("<b>" + text + "</b>", Theme.Accent), Theme.Text);

        private static readonly (string title, string label)[] HelpSections =
        {
            ("FIRST STEPS", "First steps  ·  a first sortie, start to finish"),
            ("TAKING COMMAND", "Taking command  ·  ships, airfields, leaving"),
            ("THE COMMAND BAR", "The command bar  ·  what every button opens"),
            ("THE MAP AND MENUS", "The map and menus  ·  every click and combination"),
            ("STEERING, WEAPONS, SENSORS", "Steering, weapons, sensors  ·  NAV, WPN, SNS"),
            ("RULES OF ENGAGEMENT", "Rules of engagement  ·  ships and aircraft compared"),
            ("DAMAGE CONTROL AND SUPPLY", "Damage control and supply  ·  DMG, RPL"),
            ("TASK FORCES", "Task forces  ·  formations, guide, editor"),
            ("LAUNCHING AIRCRAFT", "Launching aircraft  ·  AIR, deck, loadout"),
            ("TASKING FLIGHTS", "Tasking flights  ·  the flight window"),
            ("STRIKE PLANNER", "Strike planner  ·  many targets, saturation"),
            ("JAMMING AND DEFENCE", "Jamming and defence  ·  pods, ECM, flares"),
            ("CARGO AND AIRDROPS", "Cargo and airdrops  ·  zones, lines, supply runs"),
            ("CAMERAS", "Cameras  ·  feeds, deck view, flying it yourself"),
            ("RECOVERY", "Recovery  ·  marshal, landing, turnaround"),
            ("AMPHIBIOUS", "Amphibious  ·  the well deck"),
            ("MAP TOOLS", "Map tools  ·  ruler, docked map, compass"),
            ("SETTINGS", "Settings  ·  every option explained"),
        };

        private System.Action<Surface> HelpBodyFor(int i)
        {
            switch (i)
            {
                case 0: return HelpFirst;
                case 1: return HelpCommand;
                case 2: return HelpBar;
                case 3: return HelpMap;
                case 4: return HelpShip;
                case 5: return HelpRoe;
                case 6: return HelpDamage;
                case 7: return HelpForces;
                case 8: return HelpLaunch;
                case 9: return HelpTasking;
                case 10: return HelpStrikes;
                case 11: return HelpJamming;
                case 12: return HelpCargo;
                case 13: return HelpCameras;
                case 14: return HelpRecovery;
                case 15: return HelpAmphib;
                case 16: return HelpTools;
                default: return HelpSettingsBody;
            }
        }

        private void HelpPage(Surface s)
        {
            s.Title("NAVAL POWER  ·  QUICK GUIDE");
            P(s, "Read it straight through, or open one section. The mouse wheel scrolls any long page. Settings are in the " +
                "BepInEx Configuration Manager (F1 by default) under Naval Power, and explained in the last section.");
            s.Row("READ THE WHOLE GUIDE  ·  every section on one page  ▸", () => s.Show(HelpWhole)).image.color = Theme.AccentFill;
            for (int i = 0; i < HelpSections.Length; i++)
            {
                int index = i;
                s.Row(HelpSections[i].label + "  ▸", () => s.Show(x => { HelpBack(x, HelpSections[index].title); HelpBodyFor(index)(x); HelpFoot(x); }));
            }
        }

        private void HelpBack(Surface s, string title)
        {
            s.Title("QUICK GUIDE  ·  " + title);
            s.Row("◀  All topics", () => s.Show(HelpPage));
        }

        private void HelpFoot(Surface s) => s.Row("◀  All topics", () => s.Show(HelpPage));

        private void HelpWhole(Surface s)
        {
            HelpBack(s, "THE WHOLE GUIDE");
            for (int i = 0; i < HelpSections.Length; i++)
            {
                s.Info(UiKit.Tint("<b>" + HelpSections[i].title + "</b>", Theme.Accent), Theme.Text);
                HelpBodyFor(i)(s);
            }
            HelpFoot(s);
        }

        private void HelpFirst(Surface s)
        {
            P(s, "A first sortie from a carrier, to see the main pieces working together.");
            K(s, "1  Take command", "Click one of your ships on the map (or let the camera follow it). The command bar appears along the bottom of the screen with the ship's name, speed and course, and a row of buttons on the right.");
            K(s, "2  Steer", "Right-click the map: the ship sails there. Shift + right-click adds more legs to the route. NAV sets the speed.");
            K(s, "3  Launch", "AIR → LAUNCH AN AIRCRAFT… → pick an airframe → set each pylon, fuel and how many (1–4 make a wing) → LAUNCH. Aircraft queue for the deck and take off in turn.");
            K(s, "4  Task the flight", "Click its row in AIR. Its window opens and, while it is open, the map gives orders to that flight instead of the ship: right-click the map to give it a task area to patrol.");
            K(s, "5  Attack", "With the flight's window open, right-click an enemy: it strikes it now. Shift + right-click several enemies instead to build a strike plan, then press AUTHORIZE STRIKE.");
            K(s, "6  Come home", "Return to base in the flight window. Jets stack in the marshal astern of the carrier and land one at a time; the crew climb out and the aircraft goes back to the reserve.");
            K(s, "7  Help", "The ? button on the command bar opens this guide any time.");
        }

        private void HelpCommand(Surface s)
        {
            H(s, "A SHIP");
            P(s, "Click one of your ships on the map, or switch the camera to it any other way: command starts at once and the command bar appears. " +
                "Clicking another of your ships while commanding switches command to it. If High Command (or another NOrders mod) holds that ship, " +
                "Naval Power asks for it and takes command when it is handed over; if nothing comes back in 8 seconds you are told.");
            K(s, "F10  (Resume command)", "re-enters command on the ship the camera is following, or the last airfield. While you are flying an aircraft yourself, the same key hands it back to its task.");
            K(s, "Auto resume", "on by default (setting): after a pause menu, command comes back by itself on the ship or field you were on.");
            K(s, "[  and  ]", "previous / next ship in the task force; the camera glides to it and command follows.");
            K(s, "Right-click a friendly ship → Take command of it", "the same, from the map.");
            H(s, "AN AIRFIELD");
            P(s, "Shift + click a land airbase's icon on the map (a plain click is the game's own \"fly from here\"), or AIR → Command an airfield… and pick one. " +
                "The view becomes a free camera over the field, flown with the movement keys; AIR launches from it. A carrier's deck is commanded through its ship, not this.");
            H(s, "LEAVING");
            K(s, "EXIT", "on the command bar leaves command. It stays left until you click that ship again, press F10, or move the camera elsewhere first.");
        }

        private void HelpBar(Surface s)
        {
            P(s, "The bar along the bottom shows the ship's name and class, actual and ordered speed, course, and a status line: order confirmations, what is being tasked, EMCON, ROE and the damage-control reserve. " +
                "The buttons on the right each open a window just above themselves; drag a window by its title to move it (it remembers where), fold it with _ and close it with ×.");
            K(s, "NAV", "speed, astern, the route.");
            K(s, "WPN", "manual fire: arm a weapon and its salvo, each weapon's own ROE, CEASE FIRE.");
            K(s, "SNS", "sensors and emissions control (EMCON).");
            K(s, "ROE", "the ship's rules of engagement for automatic fire.");
            K(s, "DMG", "damage control: flooding, priorities, sealing compartments.");
            K(s, "TF", "the task force: formation, spacing, guide, the whole force's ROE and EMCON.");
            K(s, "AMPH", "the well deck (only on ships that have one): landing craft and vehicles.");
            K(s, "AIR", "air operations: launching, every flight, deck traffic. Its label shows how many are up, tinted when one needs attention.");
            K(s, "CAM", "the target camera feed.");
            K(s, "RPL", "replenishment: supply helicopter, rearming alongside.");
            K(s, "MAP", "the map, full screen (DOCK MAP shrinks it into a window).");
            K(s, "?", "this guide.   EXIT: leave command.");
            P(s, "Commanding an airfield, the ship-only buttons (NAV, WPN, SNS, ROE, DMG, TF, AMPH, RPL) are hidden.");
        }

        private void HelpMap(Surface s)
        {
            P(s, "All map orders work on the full-screen map, the docked map, and the world view. What a right-click does depends on what is under the cursor, the keys held, and whether a flight's window is open.");
            H(s, "COMMANDING A SHIP");
            K(s, "Right-click the map", "the ship sails there (the route is replaced).");
            K(s, "Shift + right-click the map", "adds a leg to the end of the route (up to 64).");
            K(s, "Ctrl + right-click the map", "on empty map, a menu for that point instead of sailing there: Steer here, Add a leg here, shots down that bearing, Send a flight to work this area…, Land / Airdrop cargo here….");
            K(s, "Right-click a contact", "opens its menu (below). With a weapon armed in WPN, right-clicking a hostile fires it instead; Shift queues it behind what that mount is already firing.");
            H(s, "WITH A FLIGHT'S WINDOW OPEN");
            K(s, "Right-click the map", "sets the flight's task area (or moves its delivery zone, if it is delivering cargo).");
            K(s, "Shift + right-click the map", "adds route legs for the flight.");
            K(s, "Right-click a hostile", "strikes it now (told so if it carries nothing that can hurt it).");
            K(s, "Shift + right-click hostiles", "adds each to the strike plan; nothing flies until AUTHORIZE STRIKE.");
            P(s, "Closing the flight's window gives the map back to the ship.");
            H(s, "CONTACT MENUS");
            K(s, "Your own ship", "Speed…, Hold position, Clear the route, Arm a weapon for a manual shot…, Rules of engagement…, Go silent / Radiate, Launch an aircraft…, formation rows, Rename…, camera, CEASE FIRE.");
            K(s, "A friendly ship", "Take command of it, Cover it with a flight…, formation rows, Rename…, camera.");
            K(s, "One of our flights", "Orders… (its window), Hold here, Weapons free, Return to base, Take the controls, camera.");
            K(s, "A hostile", "Engage with…, Strike with flights…, Jam it… (with a jammer up), Close / Open the range, camera.");
            K(s, "A lost track", "Send a flight to search…, Steer toward where it was. No firing or camera on a stale track.");
            K(s, "A bearing line (passive ESM)", "Steer toward the emitter, Add a leg toward it, anti-radiation shots down the bearing, Send a flight to investigate….");
            H(s, "THE MOUSE");
            K(s, "Mouse wheel", "zooms whatever is under the cursor: a camera feed, the map, or the main view.");
            K(s, "Middle click", "resets the zoom of the feed under the cursor, or of the main view.");
        }

        private void HelpShip(Surface s)
        {
            H(s, "NAV  ·  NAVIGATION");
            P(s, "The actual and ordered speed and the legs queued, a speed slider, presets Stop, 1/3, 2/3, Full and Flank, and Astern (back at a third of full). " +
                "Clear the route empties it. Waypoints come from the map: right-click, Shift for more legs.");
            H(s, "WPN  ·  MANUAL FIRE");
            P(s, "One row per weapon type. Click a row to arm it, pick the SALVO (Single, ×2, ×4, ×8, ×16), then right-click a contact to fire. " +
                "A plain order on the same weapon and target replaces the unfired rounds; Shift + right-click queues it behind what that mount is still firing (up to 16 orders; one not completed in 3 minutes lapses). " +
                "Nuclear weapons show NOT AUTHORISED until released. CEASE FIRE clears every order.");
            K(s, "The small button on each weapon's row", "that weapon's own rules: · follows the ship, F free, T tight, H hold. It overrides the ship's ROE for that weapon only.");
            H(s, "SNS  ·  SENSORS AND EMCON");
            P(s, "EMCON · all silent / Radiate · all on switches every emitter (radar). Passive sensors are never switched off: it would blind the ship without hiding it. " +
                "Click a sensor's row to switch that one. Colours: green silent, amber radiating, red jammed, blue passive, faint out of action. " +
                "Silent, the ship's picture comes from datalink only, and ESM bearing lines show emitters it can hear.");
        }

        private void HelpRoe(Surface s)
        {
            P(s, "Ships and aircraft both have Weapons Free, Tight and Hold, but they decide different things. A ship's ROE decides what its turrets may shoot at by themselves. " +
                "An aircraft's ROE decides what the game's own combat pilot may go after when it is not on an order of yours. Direct orders (a strike, a weapon fired from WPN) always go through.");
            H(s, "SHIPS  ·  ROE button, TF whole force, or the ship's menu");
            K(s, "Weapons Free", "every turret engages whatever it chooses, automatically.");
            K(s, "Weapons Tight", "turrets may only engage weapons inbound at the ship, and units that have actually fired on it.");
            K(s, "Weapons Hold", "point defence only: incoming missiles, nothing else.");
            P(s, "A turret refused its pick is held on manual until the rules allow it. A mount carrying out one of your WPN orders is exempt. A weapon's own setting on the WPN page (F, T, H) overrides the ship's for that weapon.");
            H(s, "AIRCRAFT  ·  flight window → Rules & weapons");
            K(s, "Weapons Hold", "never starts a fight. It still evades and defends against anything fired at it.");
            K(s, "Weapons Tight", "fights back only against aircraft that fired on it in the last 90 seconds, then returns to its task. It will not wander off after ground units that happen to shoot at it.");
            K(s, "Weapons Free", "picks up any hostile in its weapons' reach (inside its task area, unless set to fight anywhere), engages, then resumes its task.");
            P(s, "Also on Rules & weapons: Guided rounds per target (Auto, 1–4: how many missiles or glide bombs may be closing on one target at once), " +
                "and Fights only inside its task area / Fights anywhere in reach. The quick order Weapons free (a mode, not the ROE) hands the aircraft to the game's AI to hunt on its own.");
        }

        private void HelpDamage(Surface s)
        {
            H(s, "DMG  ·  DAMAGE CONTROL");
            P(s, "The damage-control reserve, list and trim, flooding compartments worst first with hull, flooding and leak figures. " +
                "Click a compartment to prioritise it (crews are pulled from the others); Work the whole ship clears the priorities. " +
                "Shift + click seals a compartment off for good: it stops drawing on the reserve, and stops being repaired. " +
                "Crews work faster than the game's own (setting), from one finite reserve that resupply restocks.");
            H(s, "RPL  ·  REPLENISHMENT");
            P(s, "Shows weapon stations below capacity and damage-control stores. Send supply helicopter launches the nearest base's helicopter with a naval supply container, paid like any launch; " +
                "the ship asks for its rearm when the helicopter is 6 km out. Alongside a supply ship or port in range, Rearm from … alongside asks directly. " +
                "A ship only takes a rearm below 25 knots.");
        }

        private void HelpForces(Surface s)
        {
            P(s, "TF on the command bar. With no force yet: Form a task force on this ship…, then Add ships… (friendly ships within 60 km, nearest first; ones held by another mod are asked for).");
            K(s, "Screen · Column · Abreast · Box", "the formation; Spacing scales it 0.5–3×.");
            K(s, "Stations turn with the guide's course / fixed to north", "whether the formation rotates with the guide.");
            K(s, "Pickets face the nearest known threat / lead on the guide's course", "in Screen formation, where the pickets go.");
            K(s, "Speed: Stop · 1/3 · 2/3 · Full", "the formation's speed, capped by its slowest ship.");
            K(s, "Ship rows", "the guide first, then each escort with its bearing, range and state; click one to take command of it.");
            K(s, "Make … the guide / Change the guide…", "the guide leads; the rest keep station on it. Steer the guide and the force follows.");
            K(s, "Return N detached ship(s) to formation", "a ship given its own orders leaves formation until returned.");
            K(s, "WHOLE FORCE", "ROE and EMCON for every ship at once, and CEASE FIRE, all ships.");
            K(s, "Edit formation…", "the formation editor: drag each ship's dot to its station on the plot (it sails there when dropped); a preset starts over. The plot range slider zooms the plot; the force can be renamed here.");
            K(s, "Leave the task force / Disband", "as they say.   [ and ] step through the force's ships.");
        }

        private void HelpLaunch(Surface s)
        {
            P(s, "AIR on the command bar, from a ship with a deck or an airfield you command.");
            K(s, "LAUNCH AN AIRCRAFT…", "the FLIGHT DECK: every airframe you can launch, its cost (free if one is in the reserve), and whether a hangar is free.");
            K(s, "FUNDING", "you pay from your allocation for airframes not in the reserve, or the faction pays for everything.");
            K(s, "The loadout page", "each pylon set one at a time (pylons that block each other show which); Fuel 25–100%; Aircraft 1–4 (two or more make a wing, named -1, -2…); Callsign (suggested for your side and what the wing carries -- a Boscali ground-attack wing is a Hammer, a Primeva fighter an Aetos -- with Another suggestion to draw again; a name you type or take stays put); Livery; a takeoff estimate that warns when it is too heavy to get off; LAUNCH.");
            K(s, "WAITING FOR A HANGAR", "launches queued for a free lift or hangar; they go in turn. Cancel the queued launches drops them.");
            K(s, "Flight rows in AIR", "grouped by home, then wing. Click a row to open that flight's window; the joystick button takes the controls of it; the eye button pins a camera on it.");
            K(s, "All flights recover", "sends every flight home.   DECK TRAFFIC lists what is launching and landing.");
        }

        private void HelpTasking(Surface s)
        {
            P(s, "Open a flight's window by clicking its row in AIR or its icon on the map. While it is open, the map gives orders to that flight; close it to give the map back to the ship.");
            K(s, "Hold here · Return to base · Weapons free", "the orders used most. Weapons free hands it to the game's AI to hunt by itself.");
            K(s, "Home: park / Home: rearm & go back", "at a land airfield, park and return to the reserve, or rearm, refuel and go back out to its task. A ship's deck always parks.");
            K(s, "Tasking", "Hold here, Station on its home ship (as an offboard sensor), Cover a task force…, Escort a flight or wing…, Cargo…, the task area's radius (2–45 km), Clear the route, and jamming station rows.");
            K(s, "Height", "100 m to 6000 m; under 400 m it follows the terrain.");
            K(s, "Rules & weapons", "ROE, guided rounds per target, fight only inside the task area or anywhere in reach, stores and countermeasures (see Rules of engagement).");
            K(s, "Wing", "its members (orders to one go to the whole wing), rename the wing, detach this aircraft, or join another wing.");
            K(s, "Aircraft", "rename (single aircraft), pin a camera, type and stores.");
            K(s, "TAKE THE CONTROLS", "fly it yourself (see Cameras).");
            P(s, "The status on each flight says what it is doing: FORMING UP, JOINING, RUNNING IN, LAUNCHING, CRANKING, EGRESSING, DEFENDING, EVADING, MARSHAL, LANDING and so on. A flight needing attention (low fuel, under fire, out of what it was sent with) is tinted.");
            P(s, "After an air-to-air shot with radar missiles, the shooter CRANKS: it turns until its targets sit at the edge of its radar's cone and eases down as its height and speed allow (up to 7°, levelling off approaching 1,500 m over the ground, not at all when slow or near top speed, and no more than 3 km in all), keeping them tracked for its missiles -- a semi-active round all the way in, an active one until its own seeker locks, and none once our side loses track of the target. Then it goes cold for 30 s (EGRESSING) and picks its task up again. Being shot at still comes first.");
        }

        private void HelpStrikes(Surface s)
        {
            P(s, "For attacking several targets in one go. Open the flight's window, then Shift + right-click each enemy: they collect in the wing lead's STRIKE PLAN on that window.");
            K(s, "STRIKE PLAN header", "how many targets, the guided rounds per target (with Per target: auto / 1 / 2 / 3 / 4 right there), and rounds wanted against rounds carried.");
            K(s, "Looks possible in one pass / About N passes", "with the reasons: bombs and guns take a pass each; targets outside one launch point's reach take another.");
            K(s, "CAN'T COMPLETE", "a weapon runs out before the list does at this rate; the skipped targets are marked NO ROUNDS LEFT, and Set 1 per target is offered when that would cover them all.");
            K(s, "Click a planned target", "choose its weapon: One weapon (or best available), or SATURATION.");
            K(s, "SATURATION", "every round of the ticked weapons, from every aircraft in the wing that carries them, at this one target (none ticked: every guided anti-surface weapon). Launch together holds them at launch range until the whole wing is in range (90 s at most), then all fire; glide bombs release on their own cue.");
            K(s, "✕ on a row", "removes that target.   Clear the plan drops it all.");
            K(s, "AUTHORIZE STRIKE", "shares the targets over the wing by what each aircraft carries (a target wanting more rounds than one aircraft has goes to several) and sends them.");
            P(s, "In flight, each aircraft works down its list: targets already covered by enough missiles in the air are skipped, a missile salvo spreads over every listed target in reach, and it reports strike list complete when done.");
        }

        private void HelpJamming(Surface s)
        {
            P(s, "Any flight carrying a jamming pod is a jammer. Right-click a hostile → Jam it… lists each jammer flight in two groups.");
            K(s, "Jam it, keep the task", "adds the target to that flight's jam list without changing its task or where it flies. Shown as (on list / capacity); click again to stop jamming it.");
            K(s, "Jam at standoff distance", "moves the flight off its task to the best station for jamming its whole list, as far out as its pods reach.");
            K(s, "Capacity", "one target per pod. With two or more pods, one is held back for self-defence against missiles; a single-pod aircraft turns its pod on a missile when it must.");
            P(s, "A jam list carries on through whatever else the flight is ordered to do, and stops only when cleared or when the flight is sent home. " +
                "When a radar-guided missile comes at it, a pod is switched onto the missile (the spare first, otherwise borrowed from the list) and goes back the moment it is over.");
            H(s, "DEFENCE");
            K(s, "Evading", "a radar missile is evaded by the game's own pilot -- notch, chaff, ECM, last-second pull -- but its dive stops at 70% of the height the shot found it at (never under 250 m), within the G and speed limits. A heat-seeker is ours: idle (never airbrakes), the shot turned onto the beam, flares in bursts from 3 km. Helicopters notch radar shots too. The setting Own radar evasion flies radar shots with our own beam instead.");
            K(s, "Built-in ECM", "aircraft with their own ECM use it automatically against radar missiles inside 6 km.");
            K(s, "Hold the task when covered", "on by default (setting): a flight with a pod on every radar missile coming at it, and flares for any heat-seeker, keeps to its orders (status DEFENDING) instead of evading. A radar missile inside 2.5 km is evaded regardless.");
            K(s, "Flares", "fired automatically against heat-seekers; the countermeasure readout shows what is left, tinted when low.");
        }

        private void HelpCargo(Surface s)
        {
            P(s, "Flight window → Tasking → Cargo (only on aircraft that can carry it).");
            K(s, "LAND AT A POINT", "then right-click the map: it lands and unloads everything (helicopters).");
            K(s, "AIRDROP AT A POINT", "then right-click the map: it drops its whole load on one pass by parachute.");
            K(s, "AIRDROP ALONG A LINE", "right-click the start of the line, then the end: the load is spread evenly along it, the aircraft running in along the line.");
            K(s, "Deliver at its home / Cancel the delivery", "as they say.");
            P(s, "Fixed-wing transports (with a fixed-wing airdrop mod such as the Aryx Chimera) can only airdrop. Deliveries are never unloaded onto a ship's deck they were not sent to. " +
                "Ctrl + right-click the map also offers Land / Airdrop cargo here… for any flight with cargo. Supply runs to ships are on RPL.");
        }

        private void HelpCameras(Surface s)
        {
            H(s, "FEEDS");
            K(s, "CAM", "the target feed: a camera on whatever your ships and aircraft are engaging, or one of your weapons in flight (◀ older / newer ▶ steps between them), held a few seconds on a kill. Drag the picture to swing the camera; double-click recentres.");
            K(s, "Pin a camera feed on it", "from any unit's menu, or the eye button on a flight's row: a window of its own (up to three). Close the window to unpin.");
            K(s, "NV", "night vision for that feed.   Mouse wheel over a feed zooms it; middle click resets the zoom.");
            H(s, "DECK VIEW");
            P(s, "While commanding a ship, the Deck view button (bottom left, above the event line) puts the camera on it. Deck view is a free camera that rides with the ship: it keeps its place relative to the ship through turns, with the horizon level. The game's own Switch View cycle is left as it is. " +
                "Fly it with the movement keys (walking pace near the deck, faster further out, Shift for 4×; it stops the moment you let go), look with free look, zoom with the wheel. It can go down to head height on the deck (and inside the ship with Snap to deck off), and remembers where you left it for each class of ship, between sessions. " +
                "The same button (DECK VIEW · leave), Switch View or Center returns to orbit. The setting Deck view turns the button off.");
            K(s, "Lock height", "beside it while in deck view: movement stays level so you can look up without climbing.");
            K(s, "Snap to deck", "beside it, on by default: the camera stays at head height over the ship's surfaces. Off, it passes through the ship, so you can go inside the bridge.");
            H(s, "FLYING IT YOURSELF");
            P(s, "TAKE THE CONTROLS in a flight's window, Take the controls on its menu, or the joystick button on its row puts you in the cockpit. The bar along the bottom then offers " +
                "RETURN CONTROL (back to its task) and DROP CONTROL (recover to base); F10 does the same as RETURN CONTROL.");
        }

        private void HelpRecovery(Surface s)
        {
            P(s, "Return to base, or low fuel, sends a flight home. Fixed-wing aircraft recover in order; helicopters land straight onto their own pads.");
            K(s, "RETURNING · distance", "flying home.");
            K(s, "MARSHAL · #2 to land", "in the stack within 14 km of home: overhead a land field, or 7 km astern of a ship. The next to land holds lowest (600 m), each after it 150 m higher, with the reason it is waiting.");
            K(s, "Cleared", "only the one at the head of the stack is cleared, and goes straight to the game's own approach from the stack.");
            K(s, "LANDING · phase", "the game's own landing: joining the pattern, turning final, on final, touched down, going round.");
            P(s, "The next is cleared once the one ahead is down and the deck has had a few seconds to clear, or has gone round. A turning ship, or any other aircraft on the approach, holds the queue. Under 10% fuel jumps the queue; under 4% is cleared at once. " +
                "On a ship, brakes are held on from the turn to final, the aircraft stops where it lands (no taxiing), the crew climb out, and it returns to the reserve a few seconds later.");
            K(s, "Home: rearm & go back", "at a land airfield the flight parks (TURNING ROUND, with a countdown), is rearmed (paid per round) and refuelled, and goes back out to its task. Decks always park.");
        }

        private void HelpAmphib(Surface s)
        {
            H(s, "AMPH  ·  THE WELL DECK");
            K(s, "Buy for the hold…", "landing craft and the vehicles your faction fields that fit one, from your allocation.");
            K(s, "Load a landing craft for the wave…", "click vehicles to add them (up to the craft's capacity), then ADD TO THE WAVE. Click a readied craft to stand it down.");
            K(s, "Landing point", "then right-click the map where they should land; it shows where they will really come ashore and warns about a poor beach.");
            K(s, "LAUNCH THE WAVE", "craft leave the well deck in turn, form up abreast astern, and go in together, each on its own lane up the beach. AT SEA lists them; open one to recall it.");
        }

        private void HelpTools(Surface s)
        {
            K(s, "Ruler", "↔ RULER on the full-screen map, or ↔ on the docked map's title: click to start, move for distance and bearing, click to fix; right-click or Esc puts it away. While it is out, map clicks measure.");
            K(s, "DOCK MAP", "on the full-screen map: shrinks it into a window beside the world, resizable from its corner; ⛶ makes it full screen again. Orders work the same on it.");
            K(s, "Compass", "the tape across the top reads the camera's bearing, with markers for the ship's heading, its next waypoint, the selected flight and the contact under the cursor (setting Show compass).");
            K(s, "Window controls", "drag a title to move a window; _ folds it to its title; × closes it.");
            K(s, "F6 – F9", "diagnostic keys, off unless Harness keys is switched on in the settings.");
        }

        // Every setting, from the settings file itself: section by section,
        // its name, current value and default, and what it does -- never out
        // of step with the code. Changed in the Configuration Manager (F1).
        private void HelpSettingsBody(Surface s)
        {
            s.Paragraph("Open the BepInEx Configuration Manager (F1 by default) and find Naval Power to change these. Most apply at once; " +
                "the file is BepInEx/config, if you prefer editing it.", Theme.Text);
            if (Settings.File == null) { s.Info("Settings not loaded yet.", Theme.Text); return; }
            var entries = new System.Collections.Generic.List<BepInEx.Configuration.ConfigEntryBase>();
            foreach (var pair in Settings.File) entries.Add(pair.Value);
            entries.Sort((a, b) =>
            {
                int bySection = string.CompareOrdinal(a.Definition.Section, b.Definition.Section);
                return bySection != 0 ? bySection : string.CompareOrdinal(a.Definition.Key, b.Definition.Key);
            });
            string section = null;
            foreach (BepInEx.Configuration.ConfigEntryBase entry in entries)
            {
                if (entry.Definition.Section != section)
                {
                    section = entry.Definition.Section;
                    s.Info(UiKit.Tint("<b>" + section.ToUpperInvariant() + "</b>", Theme.Accent), Theme.Text);
                }
                string value = entry.BoxedValue?.ToString() ?? "";
                string fallback = entry.DefaultValue?.ToString() ?? "";
                s.Paragraph(UiKit.Tint("<b>" + entry.Definition.Key + "</b>", Theme.Accent) + "  ·  " + value +
                    (value != fallback ? UiKit.Tint("  (default " + fallback + ")", Theme.TextMuted) : "") +
                    (string.IsNullOrEmpty(entry.Description?.Description) ? "" : "\n" + entry.Description.Description), Theme.Text, Theme.CaptionSize);
            }
        }
    }
}
