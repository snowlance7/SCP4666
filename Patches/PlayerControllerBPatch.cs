using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Linq;
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

        /*[HarmonyPatch(nameof(PlayerControllerB.KillPlayer))]
        [HarmonyPrefix]
        public static bool KillPlayerPrefix(PlayerControllerB __instance)
        {
            try
            {
                ChildSackBehavior sack = GameObject.FindObjectsOfType<ChildSackBehavior>().Where(x => x.isInShipRoom).FirstOrDefault();
                if (sack == null) { return true; }

                StartOfRound.Instance.allPlayersDead = false;
                sack.Activate();

                

                return false;
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e);
                return;
            }
        }*/

        [HarmonyPatch(nameof(PlayerControllerB.KillPlayer))]
        [HarmonyPostfix]
        public static void KillPlayerPostfix(PlayerControllerB __instance)
        {
            try
            {
                __instance.voiceMuffledByEnemy = false;
                MakePlayerInvisible(__instance, false); // TODO: Test this

                if (__instance != localPlayer) { return; }
                MakePlayerScreenBlack(false);
                FreezePlayer(localPlayer, false);
                if (ChildSackBehavior.localPlayerSizeChangedFromSack)
                {
                    LoggerInstance.LogDebug("Players size was changed by sack, changing back to default size");
                    ChildSackBehavior.localPlayerSizeChangedFromSack = false;
                    NetworkHandlerSCP4666.Instance.ChangePlayerSizeServerRpc(localPlayer.actualClientId, 1f);
                }
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e);
                return;
            }
        }
    }
}
