using NOrders;
namespace NavalPower
{
    // Diagnostic logging, each kind behind its own switch under Diagnostics
    // (off by default, in the configuration manager's advanced view). The
    // plain log keeps startup, warnings, and the events a player also sees on
    // screen: enough to follow a bug report, without the internals.
    internal static class Diag
    {
        // Launch and recovery, taking the controls, cockpit displays and the
        // targeting camera.
        internal static void Deck(string line) => Tracing.Deck(line);

        // How a flight flies its tasks: run-ins, re-attacks, join-ups, flares.
        internal static void Flight(string line) => Tracing.Flight(line);

        // Ships under way and task-force station keeping.
        internal static void Nav(string line) => Tracing.Nav(line);

        // The interface: map docking, night vision, ship naming.
        internal static void Ui(string line) => Tracing.Ui(line);
    }

    // Read by name by BepInEx.ConfigurationManager, which looks for a type
    // called this among a setting's tags; only the fields used here.
#pragma warning disable 0649
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? IsAdvanced;
        public int? Order;
    }
#pragma warning restore 0649
}
