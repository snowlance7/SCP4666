using HarmonyLib;
using System;
using System.Linq;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666.Patches
{
    [HarmonyPatch(typeof(StartOfRound))]
    internal class StartOfRoundPatch
    {
        [HarmonyPatch(nameof(StartOfRound.ReviveDeadPlayers))]
        [HarmonyPostfix]
        public static void ReviveDeadPlayersPostfix()
        {
            try
            {
                PluginInstance.BlackScreenOverlay.SetActive(false);
                FreezePlayer(localPlayer, false);
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e);
                return;
            }
        }

        [HarmonyPatch(nameof(StartOfRound.ShipLeaveAutomatically))] // TODO: Test this
        [HarmonyPrefix]
        public static bool ShipLeaveAutomaticallyPrefix(bool leavingOnMidnight)
        {
            try
            {
                if (leavingOnMidnight) { return true; }
                LoggerInstance.LogDebug("Ship trying to leave automatically.");
                ChildSackBehavior sack = GameObject.FindObjectsOfType<ChildSackBehavior>().Where(x => x.isInShipRoom).FirstOrDefault();
                if (sack == null) { return true; }

                LoggerInstance.LogDebug("Sack found, attempting to stop ship leave and activating");
                StartOfRound.Instance.allPlayersDead = false;

                if (!IsServerOrHost) { return false; } // TODO: Test this

                sack.Activate();

                return false;
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e);
                return true;
            }
        }
    }
}
