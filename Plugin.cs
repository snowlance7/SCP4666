using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Dawn;
using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

namespace SCP4666
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(DawnLib.PLUGIN_GUID)]
    public class Plugin : BaseUnityPlugin
    {
#pragma warning disable CS8618
        public static Plugin PluginInstance;
        public static ManualLogSource logger;
#pragma warning restore CS8618

        private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        public static PlayerControllerB localPlayer { get { return GameNetworkManager.Instance.localPlayerController; } }
        public static PlayerControllerB PlayerFromId(ulong id) { return StartOfRound.Instance.allPlayerScripts.Where(x => x.actualClientId == id).First(); }
        public static bool IsServerOrHost { get { return NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost; } }

        public static AssetBundle? ModAssets;

        private void Awake()
        {
            if (PluginInstance == null)
            {
                PluginInstance = this;
            }

            logger = PluginInstance.Logger;

            harmony.PatchAll();

            InitializeNetworkBehaviours();

            // Loading Assets
            string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "scp4666_assets.duskmod"));
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }
            logger.LogDebug($"Got AssetBundle at: {Path.Combine(sAssemblyLocation, "scp4666_assets.duskmod")}");

            // Finished
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
        }

        public static void RebuildRig(PlayerControllerB pcb)
        {
            if (pcb != null && pcb.playerBodyAnimator != null)
            {
                pcb.playerBodyAnimator.WriteDefaultValues();
                pcb.playerBodyAnimator.GetComponent<RigBuilder>()?.Build();
            }
        }

        public static void FreezePlayer(PlayerControllerB player, bool value)
        {
            player.disableInteract = value;
            player.disableLookInput = value;
            player.disableMoveInput = value;
        }

        public static void MakePlayerInvisible(PlayerControllerB player, bool value)
        {
            GameObject scavengerModel = player.gameObject.transform.Find("ScavengerModel").gameObject;
            if (scavengerModel == null) { logger.LogError("ScavengerModel not found"); return; }
            scavengerModel.transform.Find("LOD1").gameObject.SetActive(!value);
            scavengerModel.transform.Find("LOD2").gameObject.SetActive(!value);
            scavengerModel.transform.Find("LOD3").gameObject.SetActive(!value);
            player.playerBadgeMesh.gameObject.SetActive(value);
        }

        public static bool IsPlayerChild(PlayerControllerB player)
        {
            return player.thisPlayerBody.localScale.y < 1f;
        }

        public void AllowPlayerDeathAfterDelay(float delay)
        {
            IEnumerator AllowPlayerDeathAfterDelay(float delay)
            {
                yield return new WaitForSeconds(delay);
                StartOfRound.Instance.allowLocalPlayerDeath = true;
            }

            StartCoroutine(AllowPlayerDeathAfterDelay(delay));
        }

        private static void InitializeNetworkBehaviours()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
            logger.LogDebug("Finished initializing network behaviours");
        }
    }
}
