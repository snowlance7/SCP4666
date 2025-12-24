using GameNetcodeStuff;
using HarmonyLib;
using SCP4666.Doll;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666
{
    [HarmonyPatch]
    internal class Patches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer))]
        public static void KillPlayerPostfix(PlayerControllerB __instance)
        {
            ChildSackBehavior.OnPlayerDeath(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ShipLeaveAutomatically))]
        public static bool ShipLeaveAutomaticallyPrefix(bool leavingOnMidnight)
        {
            try
            {
                if (leavingOnMidnight) { return true; }
                logger.LogDebug("Ship trying to leave automatically.");
                ChildSackBehavior sack = GameObject.FindObjectsOfType<ChildSackBehavior>().Where(x => x.isInShipRoom).FirstOrDefault();
                if (sack == null) { return true; }

                logger.LogDebug("Sack found, attempting to stop ship leave and revive players");
                StartOfRound.Instance.allPlayersDead = false;

                if (!IsServerOrHost) { return false; }

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
