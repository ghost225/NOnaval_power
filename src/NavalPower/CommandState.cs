using UnityEngine;

namespace NavalPower
{
    // Shared selection state. The map input layer and the command bar both act
    // on the same weapon/quantity selection, so it lives in one place.
    internal static class CommandState
    {
        internal static Ship Ship;
        internal static string SelectedKey;
        internal static int Quantity = 1;

        internal static bool Active => Ship != null;
        internal static bool Armed => SelectedKey != null;

        private static string feedback = "";
        private static float feedbackUntil;

        internal static void Say(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            feedback = message;
            feedbackUntil = Time.unscaledTime + 6f;
        }

        internal static string Feedback => Time.unscaledTime < feedbackUntil ? feedback : null;

        internal static void Clear()
        {
            Ship = null;
            SelectedKey = null;
            Quantity = 1;
        }

        internal static WeaponCommandInfo SelectedWeapon()
        {
            if (Ship == null || SelectedKey == null) return null;
            foreach (WeaponCommandInfo weapon in WeaponOrders.GetWeapons(Ship))
                if (weapon.Key == SelectedKey) return weapon;
            return null;
        }
    }
}
