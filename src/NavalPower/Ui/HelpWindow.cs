using UnityEngine;
using NOrders;

namespace NavalPower
{
    // The quick guide: the "?" on the command bar. An index of topics, each a
    // page of the controls and combinations that do something -- the mouse
    // and key combos on the map, where the settings live, how to pin a
    // camera or task a flight. One line each, the keys in the accent colour.
    internal sealed partial class CommandUi
    {
        private static string Key(string keys, string what) => UiKit.Tint(keys, Theme.Accent) + "   " + what;

        private static readonly (string title, string label)[] HelpSections =
        {
            ("FIRST STEPS", "First steps  ·  a walk through a first sortie"),
            ("GETTING STARTED", "Getting started  ·  taking command"),
            ("THE MAP", "The map  ·  clicks and combinations"),
            ("SHIPS", "Ships  ·  the command bar's tools"),
            ("FLIGHTS", "Flights  ·  launching and tasking"),
            ("STRIKE PLANNER", "Strike planner  ·  several targets, saturation"),
            ("CAMERAS", "Cameras  ·  feeds, deck view, flying it yourself"),
            ("JAMMING, CARGO, RECOVERY", "Jamming, cargo and recovery"),
            ("SETTINGS", "Settings  ·  every option explained"),
        };

        private System.Action<Surface> HelpBodyFor(int i)
        {
            switch (i)
            {
                case 0: return HelpFirstBody;
                case 1: return HelpStartBody;
                case 2: return HelpMapBody;
                case 3: return HelpShipsBody;
                case 4: return HelpFlightsBody;
                case 5: return HelpStrikesBody;
                case 6: return HelpCamerasBody;
                case 7: return HelpMoreBody;
                default: return HelpSettingsBody;
            }
        }

        private void HelpPage(Surface s)
        {
            s.Title("NAVAL POWER  ·  QUICK GUIDE");
            s.Info("The mouse wheel scrolls any long page. Settings: BepInEx Configuration Manager (F1) → Naval Power.", Theme.TextMuted);
            s.Row("READ THE WHOLE GUIDE  ·  every section on one page  ▸", () => s.Show(HelpWhole)).image.color = Theme.AccentFill;
            for (int i = 0; i < HelpSections.Length; i++)
            {
                int index = i;
                s.Row(HelpSections[i].label + "  ▸", () => s.Show(x => { HelpBack(x, HelpSections[index].title); HelpBodyFor(index)(x); }));
            }
        }

        // Every section in order, a heading each: the tutorial read straight through.
        private void HelpWhole(Surface s)
        {
            HelpBack(s, "THE WHOLE GUIDE");
            for (int i = 0; i < HelpSections.Length; i++)
            {
                s.Info(UiKit.Tint("<b>" + HelpSections[i].title + "</b>", Theme.Accent), Theme.Text);
                HelpBodyFor(i)(s);
            }
            s.Row("◀  All topics", () => s.Show(HelpPage));
        }

        private void HelpFirstBody(Surface s)
        {
            s.Info(Key("1", "pick one of your ships on the map: the camera follows it and the command bar opens"));
            s.Info(Key("2", "right-click the map to steer there   ·   Shift + right-click to add more legs"));
            s.Info(Key("3", "open AIR and launch a flight from the deck, choosing its loadout"));
            s.Info(Key("4", "click the flight's row, then right-click the map: that is its task area"));
            s.Info(Key("5", "right-click an enemy with the flight selected to strike it"));
            s.Info(Key("6", "Return to base: it stacks in the marshal, lands, and its crew climb out"));
            s.Info(Key("7", "press the ? on the bar any time for this guide"));
        }

        private void HelpBack(Surface s, string title)
        {
            s.Title("QUICK GUIDE  ·  " + title);
            s.Row("◀  All topics", () => s.Show(HelpPage));
        }

        private void HelpStart(Surface s) { HelpBack(s, "GETTING STARTED"); HelpStartBody(s); }

        private void HelpStartBody(Surface s)
        {
            s.Info(Key("Follow a ship", "point the camera at one of your ships (pick it on the map): the command bar opens"));
            s.Info(Key("Shift + click airbase", "on the map: command a land airfield (or AIR → Command an airfield…)"));
            s.Info(Key("F10", "resume command after leaving it   ·   EXIT on the bar leaves command"));
            s.Info(Key("[  /  ]", "previous / next ship in the task force"));
            s.Info(Key("Right-click a friendly ship", "→ Take command of it"));
            s.Info("The bar's buttons open the windows; most rows open a page, and ◀ Back returns.", Theme.TextMuted);
        }

        private void HelpMap(Surface s) { HelpBack(s, "THE MAP"); HelpMapBody(s); }

        private void HelpMapBody(Surface s)
        {
            s.Info(Key("Right-click map", "steer the ship there   ·   with a flight selected: its task area"));
            s.Info(Key("Shift + right-click map", "add a leg to the route instead of starting a new one"));
            s.Info(Key("Ctrl + right-click map", "on empty map: a menu of what to do at that point"));
            s.Info(Key("Right-click a contact", "its menu: engage, strike with flights, jam, camera, investigate"));
            s.Info(Key("Right-click a bearing line", "steer toward the emitter, or send a flight to search"));
            s.Info(Key("Right-click hostile, flight selected", "strike it now"));
            s.Info(Key("Shift + right-click hostile, flight selected", "add it to the strike plan"));
            s.Info(Key("↔  (map title) / RULER", "click to measure   ·   right-click or Esc to put it away"));
            s.Info(Key("Mouse wheel", "zoom the map under the cursor, a camera feed under it, or the view"));
        }

        private void HelpShips(Surface s) { HelpBack(s, "SHIPS"); HelpShipsBody(s); }

        private void HelpShipsBody(Surface s)
        {
            s.Info(Key("NAV", "speed, course and route"));
            s.Info(Key("WPN", "pick a weapon, then right-click a contact to fire it (Shift adds rather than replaces)"));
            s.Info(Key("SNS", "radar and emissions control"));
            s.Info(Key("ROE", "rules of engagement: hold, tight or free"));
            s.Info(Key("DMG", "damage control"));
            s.Info(Key("TF", "task force: formation, guide, pickets, the formation editor"));
            s.Info(Key("AMPH", "well deck: landing craft (ships that have one)"));
            s.Info(Key("RPL", "replenishment: rearming alongside or by helicopter"));
            s.Info(Key("AIR · CAM · MAP · EXIT", "flights · live camera feed · the map · leave command"));
            s.Info(Key("Contact menu", "Arm a weapon for a manual shot… · Engage with… · CEASE FIRE"));
        }

        private void HelpFlights(Surface s) { HelpBack(s, "FLIGHTS"); HelpFlightsBody(s); }

        private void HelpFlightsBody(Surface s)
        {
            s.Info(Key("AIR", "launch from the deck or field, and the list of flights in the air"));
            s.Info(Key("Click a flight's row", "select it and open its window"));
            s.Info(Key("Joystick on a row", "take the controls of that aircraft straight away"));
            s.Info(Key("Eye on a row", "pin a camera feed on it"));
            s.Info(Key("Selected flight", "right-click the map: task area   ·   Shift: route legs"));
            s.Info(Key("Hold here · Return to base · Weapons free", "the orders used most, one click each"));
            s.Info(Key("Home: park / rearm & go back", "at an airfield, rearm and return to the task (decks always park)"));
            s.Info(Key("Rules & weapons", "ROE, guided rounds per target, fight only inside the task area"));
            s.Info(Key("Wing · Aircraft", "who it flies with   ·   camera, callsign and stores"));
        }

        private void HelpStrikes(Surface s) { HelpBack(s, "STRIKE PLANNER"); HelpStrikesBody(s); }

        private void HelpStrikesBody(Surface s)
        {
            s.Info(Key("Shift + right-click enemies", "with a flight selected: gather targets into its strike plan"));
            s.Info(Key("Click a planned target", "pick its weapon, or SATURATION and tick weapons"));
            s.Info(Key("Per target: auto 1 2 3 4", "guided rounds (missiles, glide bombs) at each target"));
            s.Info(Key("CAN'T COMPLETE", "the racks run out before the list does; skipped targets are marked"));
            s.Info(Key("SATURATION", "every ticked round from every aircraft carrying it, at one target"));
            s.Info(Key("Launch together", "they hold at launch range until the whole wing is in range"));
            s.Info(Key("AUTHORIZE STRIKE", "sends it, shared over the wing   ·   Clear the plan drops it"));
        }

        private void HelpCameras(Surface s) { HelpBack(s, "CAMERAS"); HelpCamerasBody(s); }

        private void HelpCamerasBody(Surface s)
        {
            s.Info(Key("CAM", "the target feed: a camera on whatever your ships and aircraft are engaging"));
            s.Info(Key("Eye button / Pin a camera feed on it", "pin a feed on a unit, up to three; close it to unpin"));
            s.Info(Key("NV", "night vision on that feed   ·   mouse wheel over a feed zooms it"));
            s.Info(Key("Switch View on a ship", "orbit → fly-by → deck view → orbit"));
            s.Info(Key("Deck view", "a free camera riding with the ship: movement keys, Shift faster, free look"));
            s.Info(Key("Center", "back to orbit"));
            s.Info(Key("TAKE THE CONTROLS / joystick", "fly it yourself   ·   F10 (Resume command) or the map bar hands it back"));
        }

        private void HelpMore(Surface s) { HelpBack(s, "JAMMING, CARGO, RECOVERY"); HelpMoreBody(s); }

        private void HelpMoreBody(Surface s)
        {
            s.Info(Key("Contact → Jam it…", "add to the jam list (keeps its task) or jam at standoff"));
            s.Info(Key("Cargo page", "right-click a landing or airdrop zone   ·   drop line: right-click start, then end"));
            s.Info(Key("Return to base", "jets stack in the marshal and land one at a time"));
            s.Info(Key("MARSHAL · #2 to land", "its place in the stack, and what it is waiting for"));
            s.Info(Key("At sea", "the stack holds astern; the one cleared lines up down the deck's line"));
        }
    
        // Every setting, from the settings file itself: section by section,
        // its name, current value and default, and what it does -- never out
        // of step with the code. Changed in the Configuration Manager (F1).
        private void HelpSettingsBody(Surface s)
        {
            s.Paragraph("Open the BepInEx Configuration Manager (F1 by default) and find Naval Power to change these. Most apply at once; " +
                "the file is BepInEx/config, if you prefer editing it.", Theme.TextMuted);
            if (Settings.File == null) { s.Info("Settings not loaded yet.", Theme.TextMuted); return; }
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
                s.Paragraph(UiKit.Tint(entry.Definition.Key, Theme.Text) + "  ·  " + value +
                    (value != fallback ? UiKit.Tint("  (default " + fallback + ")", Theme.TextMuted) : "") +
                    (string.IsNullOrEmpty(entry.Description?.Description) ? "" : "\n" + entry.Description.Description), Theme.TextFaint);
            }
        }
    }
}
