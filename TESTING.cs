using BepInEx.Logging;
using HarmonyLib;
using SCP4666.Doll;
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
        public static bool cameraEnabled;

        [HarmonyPostfix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.PingScan_performed))]
        public static void PingScan_performedPostFix()
        {
            if (!Utils.isBeta) { return; }
            if (!Utils.testing) { return; }
            SCP4666AI scp = SCP4666AI.Instances[0];
            cameraEnabled = !cameraEnabled;
            scp.cameraSack.enabled = cameraEnabled;
            StartOfRound.Instance.SwitchCamera(cameraEnabled ? SCP4666AI.Instances[0].cameraSack : localPlayer.gameplayCamera);
            logger.LogDebug("Camera enabled: " + cameraEnabled);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.SubmitChat_performed))]
        public static void SubmitChat_performedPrefix(HUDManager __instance)
        {
            if (!Utils.isBeta) { return; }
            if (!IsServerOrHost) { return; }
            string msg = __instance.chatTextField.text;
            string[] args = msg.Split(" ");
            Plugin.logger.LogDebug(msg);

            switch (args[0])
            {
                case "/index":
                    EvilFleshDollAI.DEBUG_bodyPartIndex = int.Parse(args[1]);
                    HUDManager.Instance.DisplayTip("BodyPartIndex", args[1]);
                    break;
                default:
                    Utils.ChatCommand(args);
                    break;
            }
        }
    }
}