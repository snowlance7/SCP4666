using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
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
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e);
                return;
            }
        }
    }
}
