using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Linq;
using Unity.Netcode;
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
            NetworkHandlerSCP4666.Instance.SpawnOverlay(__instance);
        }

        [HarmonyPatch(nameof(PlayerControllerB.KillPlayer))]
        [HarmonyPostfix]
        public static void KillPlayerPostfix(PlayerControllerB __instance)
        {
            try
            {
                __instance.voiceMuffledByEnemy = false;
                Utils.MakePlayerInvisible(__instance, false);

                if (__instance != localPlayer) { return; }
                NetworkHandlerSCP4666.Instance.blackScreenOverlay.SetActive(false);
                Utils.FreezePlayer(__instance, false);
                if (ChildSackBehavior.localPlayerSizeChangedFromSack)
                {
                    logger.LogDebug("Players size was changed by sack, changing back to default size");
                    ChildSackBehavior.localPlayerSizeChangedFromSack = false;
                    NetworkHandlerSCP4666.Instance?.ChangePlayerSizeServerRpc(__instance.actualClientId, 1f);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e);
                return;
            }
        }
    }
}
