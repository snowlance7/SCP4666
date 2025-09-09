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
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

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

        public GameObject BlackScreenOverlay;

        // Configs

        // SCP-4666 Configs
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public static ConfigEntry<bool> configEnableExtendedLogging;

        public static ConfigEntry<bool> configEnableSCP4666;
        public static ConfigEntry<string> config4666LevelRarities;
        public static ConfigEntry<string> config4666CustomLevelRarities;

        public static ConfigEntry<int> configMinPresentCount;
        public static ConfigEntry<int> configMaxPresentCount;
        public static ConfigEntry<float> configTeleportCooldown;
        public static ConfigEntry<float> configKnifeThrowCooldown;
        public static ConfigEntry<float> configKnifeReturnCooldown;
        public static ConfigEntry<float> configKnifeThrowMinDistance;
        public static ConfigEntry<float> configKnifeThrowMaxDistance;
        public static ConfigEntry<float> configTeleportDistance;
        //public static ConfigEntry<float> configDistanceToPickUpKnife;
        public static ConfigEntry<int> configSliceDamage;
        public static ConfigEntry<int> configSlapDamage;
        public static ConfigEntry<int> configHitAmountToDropPlayer;
        public static ConfigEntry<bool> configMakeScreenBlackAbduct;

        // Enchanted Knife Configs
        //public static ConfigEntry<string> configKnifeLevelRarities;
        //public static ConfigEntry<string> configKnifeCustomLevelRarities;
        public static ConfigEntry<int> configKnifeMinValue;
        public static ConfigEntry<int> configKnifeMaxValue;

        public static ConfigEntry<float> configChargeTime;
        public static ConfigEntry<int> configKnifeHitForce;
        public static ConfigEntry<float> configThrowForce;
        public static ConfigEntry<int> configKnifeHitForceYuleman;

        // Sack Configs
        public static ConfigEntry<int> configSackMinValue;
        public static ConfigEntry<int> configSackMaxValue;
        public static ConfigEntry<bool> configRandomSack;
        public static ConfigEntry<bool> configMakePlayersChildOnRevive;
        public static ConfigEntry<float> configChildMinSize;
        public static ConfigEntry<float> configChildMaxSize;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

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

            configEnableExtendedLogging = Config.Bind("Debugging", "Enable Extended Logging", false, "Shows more logging in the console for debugging/testing");

            // SCP-4666 Configs
            configEnableSCP4666 = Config.Bind("SCP-4666", "Enable SCP-4666", true, "Set to false to disable spawning SCP-4666. Use this in combination with configKnifeLevelRarities and configKnifeCustomLevelRarities if you just want to use the knife.");
            config4666LevelRarities = Config.Bind("SCP-4666 Rarities", "Level Rarities", "ExperimentationLevel:5, AssuranceLevel:6, VowLevel:9, OffenseLevel:10, AdamanceLevel:20, MarchLevel:10, RendLevel:100, DineLevel:100, TitanLevel:75, ArtificeLevel:20, EmbrionLevel:25", "Rarities for each level. See default for formatting.");
            config4666CustomLevelRarities = Config.Bind("SCP-4666 Rarities", "Custom Level Rarities", "Secret LabsLevel:100, CrestLevel:100, Winter LodgeLevel:100, SummitLevel:100, PolarusLevel:100, FilitriosLevel:100, MotraLevel:100", "Rarities for modded levels. Same formatting as level rarities.");

            configMinPresentCount = Config.Bind("SCP-4666", "Minimum Present Count", 3, "Minimum number of presents spawned by SCP-4666.");
            configMaxPresentCount = Config.Bind("SCP-4666", "Maximum Present Count", 5, "Maximum number of presents spawned by SCP-4666.");
            configTeleportCooldown = Config.Bind("SCP-4666", "Teleport Cooldown", 15f, "Cooldown (in seconds) between SCP-4666 teleport actions.");
            configKnifeThrowCooldown = Config.Bind("SCP-4666", "Knife Throw Cooldown", 10f, "Cooldown (in seconds) before SCP-4666 can throw the knife again.");
            configKnifeReturnCooldown = Config.Bind("SCP-4666", "Knife Return Cooldown", 5f, "Cooldown (in seconds) before SCP-4666 can call the knife back.");
            configKnifeThrowMinDistance = Config.Bind("SCP-4666", "Knife Throw Minimum Distance", 5f, "Minimum distance SCP-4666 can throw the knife.");
            configKnifeThrowMaxDistance = Config.Bind("SCP-4666", "Knife Throw Maximum Distance", 10f, "Maximum distance SCP-4666 can throw the knife.");
            configTeleportDistance = Config.Bind("SCP-4666", "Teleport Distance", 15f, "Distance SCP-4666 needs to be from the player to allow him to teleport.");
            //configDistanceToPickUpKnife = Config.Bind("SCP-4666", "Distance to Pick Up Knife", 15f, "Distance within which SCP-4666 can retrieve the knife if it is dropped by the player");
            configSliceDamage = Config.Bind("SCP-4666", "Slice Damage", 25, "Damage dealt by SCP-4666's knife slice.");
            configSlapDamage = Config.Bind("SCP-4666", "Slap Damage", 10, "Damage dealt by SCP-4666's slap attack. (When his knife is stolen)");
            configHitAmountToDropPlayer = Config.Bind("SCP-4666", "Hit Amount to Drop Player", 5, "Number of hits required to make SCP-4666 drop a player.");
            configMakeScreenBlackAbduct = Config.Bind("SCP-4666", "Make Screen Black on Abduction", true, "Set to true to make the player's screen black during abduction.");

            // Enchanted Knife Configs
            //configKnifeLevelRarities = Config.Bind("Enchanted Knife", "Level Rarities", "ExperimentationLevel:0, AssuranceLevel:0, VowLevel:0, OffenseLevel:0, AdamanceLevel:0, MarchLevel:0, RendLevel:0, DineLevel:0, TitanLevel:0, ArtificeLevel:0, EmbrionLevel:0, Modded:0", "Rarities for each level. See default for formatting.");
            //configKnifeCustomLevelRarities = Config.Bind("Enchanted Knife", "Custom Level Rarities", "", "Rarities for modded levels. Same formatting as level rarities.");
            configKnifeMinValue = Config.Bind("Enchanted Knife", "Min Value", 100, "Minimum scrap value of enchanted knife.");
            configKnifeMaxValue = Config.Bind("Enchanted Knife", "Max Value", 200, "Maximum scrap value of enchanted knife.");

            configChargeTime = Config.Bind("Enchanted Knife", "Charge Time", 1f, "Time required to charge the throw attack for the knife.");
            configKnifeHitForce = Config.Bind("Enchanted Knife", "Knife Hit Force", 1, "Damage applied when the knife hits a enemy.");
            configThrowForce = Config.Bind("Enchanted Knife", "Throw Force", 100f, "How fast the knife will go when thrown");
            configKnifeHitForceYuleman = Config.Bind("Enchanted Knife", "Knife Hit Force (Player)", 25, "Damage applied when the knife hits a player. This also applies to when the yuleman throws it.");

            // Sack Configs
            configSackMinValue = Config.Bind("Child Sack", "Min Value", 100, "Minimum scrap value of the sack the yuleman drops.");
            configSackMaxValue = Config.Bind("Child Sack", "Max Value", 200, "Maximum scrap value of sack the yuleman drops.");
            configRandomSack = Config.Bind("Child Sack", "Random sack", false, "If set to true, instead of reviving all players on team wipe, it will have a 50/50 chance to either revive a player or spawn a present, with 1 player revive guranteed.");
            configMakePlayersChildOnRevive = Config.Bind("Child Sack", "Make Players Child On Revive", true, "Should the players size be changed when being revived by the child sack?");
            configChildMinSize = Config.Bind("Child Sack", "Child Min Size", 0.6f, "Min size to make the player when revived as a child. Default vanilla size of the player is 1.");
            configChildMaxSize = Config.Bind("Child Sack", "Child Max Size", 0.8f, "Max size to make the player when revived as a child. Default vanilla size of the player is 1.");

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
            //Knife.itemSpawnsOnGround = configSpawnKnifeOnGround.Value;

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(Knife.spawnPrefab);
            Utilities.FixMixerGroups(Knife.spawnPrefab);
            //LethalLib.Modules.Items.RegisterScrap(Knife, GetLevelRarities(configKnifeLevelRarities.Value), GetCustomLevelRarities(configKnifeCustomLevelRarities.Value));
            LethalLib.Modules.Items.RegisterScrap(Knife);

            Item Sack = ModAssets.LoadAsset<Item>("Assets/ModAssets/ChildSackItem.asset");
            if (Sack == null) { LoggerInstance.LogError("Error: Couldnt get ChildSackItem from assets"); return; }
            LoggerInstance.LogDebug($"Got ChildSack prefab");

            Sack.minValue = configSackMinValue.Value;
            Sack.maxValue = configSackMaxValue.Value;

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(Sack.spawnPrefab);
            Utilities.FixMixerGroups(Sack.spawnPrefab);
            LethalLib.Modules.Items.RegisterScrap(Sack);

            if (configEnableSCP4666.Value)
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
            }

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

        public static void log(string message)
        {
            if (configEnableExtendedLogging.Value)
            {
                LoggerInstance.LogDebug(message);
            }
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
            if (scavengerModel == null) { LoggerInstance.LogError("ScavengerModel not found"); return; }
            scavengerModel.transform.Find("LOD1").gameObject.SetActive(!value);
            scavengerModel.transform.Find("LOD2").gameObject.SetActive(!value);
            scavengerModel.transform.Find("LOD3").gameObject.SetActive(!value);
            player.playerBadgeMesh.gameObject.SetActive(value);
        }

        public static bool IsPlayerChild(PlayerControllerB player)
        {
            return player.thisPlayerBody.localScale.y < 1f;
        }

        public static bool CalculatePath(Vector3 fromPos, Vector3 toPos)
        {
            Vector3 from = RoundManager.Instance.GetNavMeshPosition(fromPos, RoundManager.Instance.navHit, 1.75f);
            Vector3 to = RoundManager.Instance.GetNavMeshPosition(toPos, RoundManager.Instance.navHit, 1.75f);

            NavMeshPath path = new();
            return NavMesh.CalculatePath(from, to, -1, path) && Vector3.Distance(path.corners[path.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(to, RoundManager.Instance.navHit, 2.7f)) <= 1.55f; // TODO: Test this
        }

        public static Vector3 GetPositionFrontOfPlayer(PlayerControllerB player, float distance = 1)
        {
            return player.playerEye.transform.position + player.playerEye.transform.forward * distance;
        }

        public static void GrabGrabbableObjectOnClient(GrabbableObject obj)
        {
            obj.transform.root.SetParent(null);
            obj.isHeldByEnemy = false;

            localPlayer.currentlyGrabbingObject = obj;
            localPlayer.currentlyGrabbingObject.InteractItem();
            if (localPlayer.currentlyGrabbingObject.grabbable && localPlayer.FirstEmptyItemSlot() != -1)
            {
                localPlayer.playerBodyAnimator.SetBool("GrabInvalidated", value: false);
                localPlayer.playerBodyAnimator.SetBool("GrabValidated", value: false);
                localPlayer.playerBodyAnimator.SetBool("cancelHolding", value: false);
                localPlayer.playerBodyAnimator.ResetTrigger("Throw");
                //localPlayer.SetSpecialGrabAnimationBool(setTrue: true);
                //localPlayer.isGrabbingObjectAnimation = true;
                localPlayer.cursorIcon.enabled = false;
                localPlayer.cursorTip.text = "";
                localPlayer.twoHanded = localPlayer.currentlyGrabbingObject.itemProperties.twoHanded;
                localPlayer.carryWeight = Mathf.Clamp(localPlayer.carryWeight + (localPlayer.currentlyGrabbingObject.itemProperties.weight - 1f), 1f, 10f);
                if (localPlayer.currentlyGrabbingObject.itemProperties.grabAnimationTime > 0f)
                {
                    localPlayer.grabObjectAnimationTime = localPlayer.currentlyGrabbingObject.itemProperties.grabAnimationTime;
                }
                /*else
                {
                    localPlayer.grabObjectAnimationTime = 0.4f;
                }*/

                localPlayer.GrabObjectServerRpc(obj.NetworkObject);

                if (localPlayer.grabObjectCoroutine != null)
                {
                    localPlayer.StopCoroutine(localPlayer.grabObjectCoroutine);
                }
                localPlayer.grabObjectCoroutine = localPlayer.StartCoroutine(localPlayer.GrabObject());
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
