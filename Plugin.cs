using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace SCP4666
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin PluginInstance;
        public static ManualLogSource LoggerInstance;
        private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        public static PlayerControllerB localPlayer { get { return GameNetworkManager.Instance.localPlayerController; } }
        public static PlayerControllerB PlayerFromId(ulong id) { return StartOfRound.Instance.allPlayerScripts.Where(x => x.actualClientId == id).First(); }
        public static bool IsServerOrHost { get { return NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost; } }

        public static AssetBundle? ModAssets;

        // Configs

        // SCP-4666 Configs
        public static ConfigEntry<bool> configEnableSCP4666;
        public static ConfigEntry<string> config4666LevelRarities;
        public static ConfigEntry<string> config4666CustomLevelRarities;

        // Enchanted Knife Configs
        public static ConfigEntry<bool> configSpawnKnifeOnGround;
        public static ConfigEntry<string> configKnifeLevelRarities;
        public static ConfigEntry<string> configKnifeCustomLevelRarities;
        public static ConfigEntry<int> configKnifeMinValue;
        public static ConfigEntry<int> configKnifeMaxValue;

        // Sack Configs
        public static ConfigEntry<int> configSackMinValue;
        public static ConfigEntry<int> configSackMaxValue;

        private void Awake()
        {
            if (PluginInstance == null)
            {
                PluginInstance = this;
            }

            LoggerInstance = PluginInstance.Logger;

            harmony.PatchAll();

            InitializeNetworkBehaviours();

            // Configs

            // SCP-4666 Configs
            configEnableSCP4666 = Config.Bind("SCP-4666", "Enable SCP-4666", true, "Set to false to disable spawning SCP-4666. Use this in combination with configKnifeLevelRarities and configKnifeCustomLevelRarities if you just want to use the knife.");
            config4666LevelRarities = Config.Bind("SCP-4666 Rarities", "Level Rarities", "ExperimentationLevel:5, AssuranceLevel:6, VowLevel:9, OffenseLevel:10, AdamanceLevel:15, MarchLevel:13, RendLevel:50, DineLevel:50, TitanLevel:20, ArtificeLevel:20, EmbrionLevel:25, Modded:15", "Rarities for each level. See default for formatting.");
            config4666CustomLevelRarities = Config.Bind("SCP-4666 Rarities", "Custom Level Rarities", "Secret LabsLevel:0", "Rarities for modded levels. Same formatting as level rarities.");

            // Enchanted Knife Configs
            configSpawnKnifeOnGround = Config.Bind("Enchanted Knife", "Spawn Knife on Ground", false, "Set to false to disable spawning enchanted knives as scrap. This means you can only get it from the Yuleman.");
            configKnifeLevelRarities = Config.Bind("Enchanted Knife", "Level Rarities", "ExperimentationLevel:0, AssuranceLevel:0, VowLevel:0, OffenseLevel:0, AdamanceLevel:0, MarchLevel:0, RendLevel:0, DineLevel:0, TitanLevel:0, ArtificeLevel:0, EmbrionLevel:0, Modded:0", "Rarities for each level. See default for formatting.");
            configKnifeCustomLevelRarities = Config.Bind("Enchanted Knife", "Custom Level Rarities", "", "Rarities for modded levels. Same formatting as level rarities.");
            configKnifeMinValue = Config.Bind("Enchanted Knife", "Min Value", 100, "Minimum scrap value of enchanted knife.");
            configKnifeMaxValue = Config.Bind("Enchanted Knife", "Max Value", 200, "Maximum scrap value of enchanted knife.");

            // Sack Configs
            configSackMinValue = Config.Bind("Child Sack", "Min Value", 100, "Minimum scrap value of the sack the yuleman drops.");
            configSackMaxValue = Config.Bind("Child Sack", "Max Value", 200, "Maximum scrap value of sack the yuleman drops.");

            // Loading Assets
            string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "scp4666_assets"));
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }
            LoggerInstance.LogDebug($"Got AssetBundle at: {Path.Combine(sAssemblyLocation, "scp4666_assets")}");

            Item Knife = ModAssets.LoadAsset<Item>("Assets/ModAssets/YulemanKnifeItem.asset");
            if (Knife == null) { LoggerInstance.LogError("Error: Couldnt get YulemanKnifeItem from assets"); return; }
            LoggerInstance.LogDebug($"Got YulemanKnife prefab");

            Knife.minValue = configKnifeMinValue.Value;
            Knife.maxValue = configKnifeMaxValue.Value;
            Knife.itemSpawnsOnGround = configSpawnKnifeOnGround.Value;

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(Knife.spawnPrefab);
            Utilities.FixMixerGroups(Knife.spawnPrefab);
            LethalLib.Modules.Items.RegisterScrap(Knife, GetLevelRarities(configKnifeLevelRarities.Value), GetCustomLevelRarities(configKnifeCustomLevelRarities.Value));

            Item Rune = ModAssets.LoadAsset<Item>("Assets/ModAssets/YulemanKnifeRuneItem.asset");
            if (Rune == null) { LoggerInstance.LogError("Error: Couldnt get YulemanKnifeRuneItem from assets"); return; }
            LoggerInstance.LogDebug($"Got YulemanKnifeRune prefab");

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(Rune.spawnPrefab);
            Utilities.FixMixerGroups(Rune.spawnPrefab);
            LethalLib.Modules.Items.RegisterItem(Rune);

            /*Item Sack = ModAssets.LoadAsset<Item>("Assets/ModAssets/ChildSackItem.asset");
            if (Sack == null) { LoggerInstance.LogError("Error: Couldnt get ChildSackItem from assets"); return; }
            LoggerInstance.LogDebug($"Got ChildSack prefab");

            Sack.minValue = configSackMinValue.Value;
            Sack.maxValue = configSackMaxValue.Value;

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(Sack.spawnPrefab);
            Utilities.FixMixerGroups(Sack.spawnPrefab);
            LethalLib.Modules.Items.RegisterScrap(Sack);*/

            /*if (configEnableSCP4666.Value)
            {
                EnemyType SCP4666 = ModAssets.LoadAsset<EnemyType>("Assets/ModAssets/SCP4666Enemy.asset");
                if (SCP4666 == null) { LoggerInstance.LogError("Error: Couldnt get SCP-4666 from assets"); return; }
                LoggerInstance.LogDebug($"Got SCP-4666 prefab");
                TerminalNode YulemanTN = ModAssets.LoadAsset<TerminalNode>("Assets/ModAssets/Bestiary/SCP4666TN.asset");
                TerminalKeyword YulemanTK = ModAssets.LoadAsset<TerminalKeyword>("Assets/ModAssets/Bestiary/SCP4666TK.asset");

                LoggerInstance.LogDebug("Registering enemy network prefab...");
                LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(SCP4666.enemyPrefab);
                LoggerInstance.LogDebug("Registering enemy...");
                Enemies.RegisterEnemy(SCP4666, GetLevelRarities(config4666LevelRarities.Value), GetCustomLevelRarities(config4666CustomLevelRarities.Value), YulemanTN, YulemanTK);
            }*/

            // Finished
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
        }

        public Dictionary<Levels.LevelTypes, int> GetLevelRarities(string levelsString)
        {
            try
            {
                Dictionary<Levels.LevelTypes, int> levelRaritiesDict = new Dictionary<Levels.LevelTypes, int>();

                if (levelsString != null && levelsString != "")
                {
                    string[] levels = levelsString.Split(',');

                    foreach (string level in levels)
                    {
                        string[] levelSplit = level.Split(':');
                        if (levelSplit.Length != 2) { continue; }
                        string levelType = levelSplit[0].Trim();
                        string levelRarity = levelSplit[1].Trim();

                        if (Enum.TryParse<Levels.LevelTypes>(levelType, out Levels.LevelTypes levelTypeEnum) && int.TryParse(levelRarity, out int levelRarityInt))
                        {
                            levelRaritiesDict.Add(levelTypeEnum, levelRarityInt);
                        }
                        else
                        {
                            LoggerInstance.LogError($"Error: Invalid level rarity: {levelType}:{levelRarity}");
                        }
                    }
                }
                return levelRaritiesDict;
            }
            catch (Exception e)
            {
                Logger.LogError($"Error: {e}");
                return null!;
            }
        }

        public Dictionary<string, int> GetCustomLevelRarities(string levelsString)
        {
            try
            {
                Dictionary<string, int> customLevelRaritiesDict = new Dictionary<string, int>();

                if (levelsString != null)
                {
                    string[] levels = levelsString.Split(',');

                    foreach (string level in levels)
                    {
                        string[] levelSplit = level.Split(':');
                        if (levelSplit.Length != 2) { continue; }
                        string levelType = levelSplit[0].Trim();
                        string levelRarity = levelSplit[1].Trim();

                        if (int.TryParse(levelRarity, out int levelRarityInt))
                        {
                            customLevelRaritiesDict.Add(levelType, levelRarityInt);
                        }
                        else
                        {
                            LoggerInstance.LogError($"Error: Invalid level rarity: {levelType}:{levelRarity}");
                        }
                    }
                }
                return customLevelRaritiesDict;
            }
            catch (Exception e)
            {
                Logger.LogError($"Error: {e}");
                return null!;
            }
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
            LoggerInstance.LogDebug("Finished initializing network behaviours");
        }
    }
}
