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
                NetworkHandlerSCP4666.Instance.blackScreenOverlay.SetActive(false);
                Utils.FreezePlayer(localPlayer, false);
            }
            catch (Exception e)
            {
                logger.LogError(e);
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
                logger.LogDebug("Ship trying to leave automatically.");
                ChildSackBehavior sack = GameObject.FindObjectsOfType<ChildSackBehavior>().Where(x => x.isInShipRoom).FirstOrDefault();
                if (sack == null) { return true; }

                logger.LogDebug("Sack found, attempting to stop ship leave and activating");
                StartOfRound.Instance.allPlayersDead = false;

                if (!IsServerOrHost) { return false; } // TODO: Test this

                sack.Activate();

                return false;
            }
            catch (Exception e)
            {
                logger.LogError(e);
                return true;
            }
        }
    }
}
