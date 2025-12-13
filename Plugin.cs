using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Dawn;
using Dawn.Utils;
using Dusk;
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
    [BepInDependency(Dusk.MyPluginInfo.PLUGIN_GUID)]
    public class Plugin : BaseUnityPlugin
    {
#pragma warning disable CS8618
        public static Plugin Instance;
        public static ManualLogSource logger;
#pragma warning restore CS8618

        public static AssetBundle? ModAssets;
        private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        public static PlayerControllerB localPlayer { get { return GameNetworkManager.Instance.localPlayerController; } }
        public static PlayerControllerB PlayerFromId(ulong id) { return StartOfRound.Instance.allPlayerScripts.Where(x => x.actualClientId == id).First(); }
        public static bool IsServerOrHost { get { return NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost; } }
        public static DuskMod Mod { get; private set; } = null!;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            logger = Instance.Logger;

            harmony.PatchAll();

            InitializeNetworkBehaviours();

            //AssetBundle mainBundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "scp4666_mainassets"));
            //AssetBundle mainBundle = AssetBundleUtils.LoadBundle(Assembly.GetExecutingAssembly(), "scp4666_mainassets");
            AssetBundle mainBundle = AssetBundleUtils.LoadBundle(Assembly.GetExecutingAssembly(), "scp4666_mainassets");
            Mod = DuskMod.RegisterMod(this, mainBundle);
            Mod.RegisterContentHandlers();

            // Load Assets
            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "Assets/scp4666_assets"));

            // Finished
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
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
