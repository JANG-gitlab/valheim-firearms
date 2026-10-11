using HarmonyLib;
using UnityEngine;

namespace ValheimFirearms
{
    [HarmonyPatch]
    internal static class Patches
    {
        /// <summary>Partie lancée (client, hôte ou serveur dédié) : RPC et systèmes du mod.</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.Start)), HarmonyPostfix]
        private static void GameStart()
        {
            Network.Register();
            Ballistics.Ensure();
            if (!ZNet.instance || !ZNet.instance.IsDedicated())
            {
                Effects.Ensure();
                WeaponController.Ensure();
            }
        }

        /// <summary>
        /// Arme en main : le clic gauche (« Attaque ») et le clic droit (« Bloquer ») partent dans notre contrôleur
        /// au lieu de déclencher l'attaque ou le blocage vanilla.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls)), HarmonyPrefix]
        private static void SetControls(Player __instance, ref bool attack, ref bool attackHold, ref bool secondaryAttack,
            ref bool secondaryAttackHold, ref bool block, ref bool blockHold)
        {
            if (__instance != Player.m_localPlayer || WeaponController.GetGun(__instance) == null) return;
            WeaponController.Ensure();
            WeaponController.Instance.SetInput(attackHold, blockHold);
            attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = false;
        }

        /// <summary>Filet de sécurité : jamais d'attaque vanilla (carreau d'arbalète) avec l'arme à feu.</summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack)), HarmonyPrefix]
        private static bool StartAttack(Humanoid __instance, ref bool __result)
        {
            if (WeaponController.GetGun(__instance) == null) return true;
            __result = false;
            return false;
        }

        /// <summary>Le personnage fait face à la visée quand on vise ou qu'on vient de tirer.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.AlwaysRotateCamera)), HarmonyPostfix]
        private static void AlwaysRotate(Player __instance, ref bool __result)
        {
            if (__result || __instance != Player.m_localPlayer || WeaponController.Instance == null) return;
            if (WeaponController.Instance.WantsFacing && WeaponController.GetGun(__instance) != null) __result = true;
        }

        /// <summary>Zoom de visée (clic droit) appliqué au champ de vision de la caméra.</summary>
        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera)), HarmonyPostfix]
        private static void CameraFov(GameCamera __instance)
        {
            var wc = WeaponController.Instance;
            if (wc == null || wc.Aim <= 0f || __instance.m_freeFly) return;
            float fov = __instance.m_fov * Mathf.Lerp(1f, FirearmsConfig.AimFov.Value, Mathf.SmoothStep(0, 1, wc.Aim));
            __instance.m_camera.fieldOfView = fov;
            __instance.m_skyCamera.fieldOfView = fov;
        }
    }
}
