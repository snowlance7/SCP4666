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
            try
            {
                if (__instance != localPlayer) { return; }
                NetworkHandlerSCP4666.Instance.BlackScreenOverlay = ModAssets.LoadAsset<GameObject>("Assets/ModAssets/BlackScreenOverlay.prefab");
                NetworkHandlerSCP4666.Instance.BlackScreenOverlay = GameObject.Instantiate(NetworkHandlerSCP4666.Instance.BlackScreenOverlay);
                NetworkHandlerSCP4666.Instance.BlackScreenOverlay.SetActive(false);
            }
            catch (Exception e)
            {
                logger.LogError(e);
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

                if (__instance != localPlayer) { return; }
                NetworkHandlerSCP4666.Instance.BlackScreenOverlay.SetActive(false);
                FreezePlayer(localPlayer, false);
                if (ChildSackBehavior.localPlayerSizeChangedFromSack)
                {
                    logger.LogDebug("Players size was changed by sack, changing back to default size");
                    ChildSackBehavior.localPlayerSizeChangedFromSack = false;
                    NetworkHandlerSCP4666.Instance?.ChangePlayerSizeServerRpc(localPlayer.actualClientId, 1f);
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
