using BepInEx.Logging;
using HarmonyLib;
using SCP4666.YulemanKnife;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Diagnostics;
using static SCP4666.Plugin;

/* bodyparts
 * 0 head
 * 1 right arm
 * 2 left arm
 * 3 right leg
 * 4 left leg
 * 5 chest
 * 6 feet
 * 7 right hip
 * 8 crotch
 * 9 left shoulder
 * 10 right shoulder */

namespace SCP4666
{
    [HarmonyPatch]
    public class TESTING : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;
        public static GameObject? EvilDollPrefab;

        [HarmonyPostfix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.PingScan_performed))]
        public static void PingScan_performedPostFix()
        {
            if (!Utils.isBeta) { return; }
            if (!Utils.testing) { return; }

        }

        [HarmonyPrefix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.SubmitChat_performed))]
        public static void SubmitChat_performedPrefix(HUDManager __instance)
        {
            if (!Utils.isBeta) { return; }
            if (!IsServerOrHost) { return; }
            string msg = __instance.chatTextField.text;
            string[] args = msg.Split(" ");
            LoggerInstance.LogDebug(msg);

            switch (args[0])
            {
                case "/doll":
                    LoggerInstance.LogDebug("Spawning doll");
                    GameObject obj = GameObject.Instantiate(EvilDollPrefab, localPlayer.gameplayCamera.transform.position + localPlayer.gameplayCamera.transform.forward * 1f, localPlayer.transform.rotation);
                    obj.GetComponent<NetworkObject>().Spawn(true);
                    break;
                default:
                    Utils.ChatCommand(args);
                    break;
            }
        }
    }
}