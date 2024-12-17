using GameNetcodeStuff;
using HarmonyLib;
using System;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666.Patches
{
    [HarmonyPatch(typeof(PlayerControllerB))]
    internal class PlayerControllerBPatch
    {
        [HarmonyPatch(nameof(PlayerControllerB.ConnectClientToPlayerObject))]
        [HarmonyPostfix]
        public static void ConnectClientToPlayerObjectPostfix(PlayerControllerB __instance)
        {
            try
            {
                if (__instance != localPlayer) { return; }
                PluginInstance.BlackScreenOverlay = ModAssets.LoadAsset<GameObject>("Assets/ModAssets/BlackScreenOverlay.prefab");
                PluginInstance.BlackScreenOverlay = GameObject.Instantiate(PluginInstance.BlackScreenOverlay);
                MakePlayerScreenBlack(false);
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e);
                return;
            }
        }

        [HarmonyPatch(nameof(PlayerControllerB.KillPlayer))]
        [HarmonyPostfix]
        public static void KillPlayerPostfix(PlayerControllerB __instance)
        {
            try
            {
                __instance.voiceMuffledByEnemy = false;
                MakePlayerInvisible(__instance, false);
                __instance.thisPlayerBody.localScale = new Vector3(1f, 1f, 1f);
                if (__instance != localPlayer) { return; }
                MakePlayerScreenBlack(false);
                FreezePlayer(localPlayer, false);
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e);
                return;
            }
        }
    }
}
