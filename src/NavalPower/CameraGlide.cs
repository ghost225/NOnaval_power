using System;
using HarmonyLib;
using UnityEngine;

namespace NavalPower
{
    // Switching ships with a glide instead of a cut.
    //
    // Command follows the camera, so a switch is still the game's own
    // SetFollowingUnit -- instant. For the next moment the orbit camera's
    // pivot is then carried from the old ship to the new one, easing in and
    // out, with the camera's offset blended from the old view to the new and
    // kept looking at the moving pivot. Under a second, start to finish.
    internal static class CameraGlide
    {
        private const float Duration = 0.8f;

        private static Unit from;
        private static Vector3 fromPivot, fromOffset;
        private static float started = -1f;

        internal static bool Active => started >= 0f;

        internal static void SwitchTo(Unit target)
        {
            var cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null || target == null || cameras.followingUnit == target) return;
            bool orbiting = cameras.currentState == cameras.orbitState && cameras.cameraPivot != null;
            if (orbiting)
            {
                from = cameras.followingUnit;
                fromPivot = cameras.cameraPivot.position;
                fromOffset = cameras.transform.position - fromPivot;
            }
            cameras.SetFollowingUnit(target);
            started = orbiting && cameras.currentState == cameras.orbitState ? Time.unscaledTime : -1f;
        }

        // After the orbit camera has placed itself for the new ship this frame.
        internal static void Apply(CameraStateManager cam)
        {
            if (started < 0f) return;
            if (cam.followingUnit == null || cam.cameraPivot == null)
            {
                started = -1f;
                return;
            }
            float t = Mathf.Clamp01((Time.unscaledTime - started) / Duration);
            Transform pivot = cam.cameraPivot;
            Transform eye = cam.transform;
            if (t >= 1f)
            {
                started = -1f;
                eye.localRotation = Quaternion.identity;        // back to the pivot's own view
                return;
            }
            float e = t * t * (3f - 2f * t);                    // ease in, ease out
            Vector3 start = from != null && !from.disabled ? from.transform.position : fromPivot;
            Vector3 targetPivot = pivot.position;
            Vector3 targetOffset = eye.position - targetPivot;
            Vector3 place = Vector3.Lerp(start, targetPivot, e);
            Vector3 offset = Vector3.Lerp(fromOffset, targetOffset, e);
            pivot.position = place;
            eye.position = place + offset;
            if (offset.sqrMagnitude > 0.01f) eye.rotation = Quaternion.LookRotation(-offset, Vector3.up);
        }
    }

    [HarmonyPatch(typeof(CameraOrbitState), "CameraMotion")]
    internal static class CameraGlidePatch
    {
        private const string Name = "Camera glide";

        private static void Postfix(CameraStateManager cam)
        {
            if (!CameraGlide.Active || !Guard.Ok(Name)) return;
            try { CameraGlide.Apply(cam); }
            catch (Exception ex) { Guard.Failed(Name, ex); }
        }
    }
}
