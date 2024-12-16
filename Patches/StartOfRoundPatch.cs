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
                MakePlayerScreenBlack(false);
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
        public static bool ShipLeaveAutomaticallyPrefix()
        {
            try
            {
                ChildSackBehavior sack = GameObject.FindObjectsOfType<ChildSackBehavior>().Where(x => x.isInShipRoom).FirstOrDefault();
                if (sack == null) { return true; }

                switch (sack.respawnType)
                {
                    case ChildSackBehavior.RespawnType.Manual:
                        break;
                    case ChildSackBehavior.RespawnType.TeamWipe:
                        sack.Activate();
                        return false;
                    case ChildSackBehavior.RespawnType.ActivatedTeamWipe:
                        if (sack.activated)
                        {
                            sack.Activate();
                            return false;
                        }
                        break;
                    case ChildSackBehavior.RespawnType.Random:
                        sack.Activate();
                        return false;
                    default:
                        break;
                }

                return true;
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e);
                return true;
            }
        }
    }
}
