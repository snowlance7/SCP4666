using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666
{
    public class NetworkHandlerSCP4666 : NetworkBehaviour
    {
        private static ManualLogSource logger = Plugin.LoggerInstance;

        public static NetworkHandlerSCP4666? Instance { get; private set; }

        public override void OnNetworkSpawn()
        {
            if (IsServerOrHost)
            {
                if (Instance != null && Instance != this)
                {
                    Instance.gameObject.GetComponent<NetworkObject>().Despawn();
                    logger.LogDebug("Despawned network object");
                }
            }

            hideFlags = HideFlags.HideAndDontSave;
            Instance = this;
            logger.LogDebug("set instance to this");
            base.OnNetworkSpawn();
            logger.LogDebug("base.OnNetworkSpawn");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        [ClientRpc]
        private void ChangePlayerSizeClientRpc(ulong clientId, float size)
        {
            logger.LogDebug("ChangePlayerSizeClientRpc() called");
            PlayerControllerB player = PlayerFromId(clientId);
            player.thisPlayerBody.localScale = new Vector3(size, size, size);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangePlayerSizeServerRpc(ulong clientId, float size)
        {
            logger.LogDebug("ChangePlayerSizeServerRpc() called");
            if (!IsServerOrHost) { return; }
            ChangePlayerSizeClientRpc(clientId, size);
        }

        public IEnumerator AllowPlayerDeathAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            StartOfRound.Instance.allowLocalPlayerDeath = true;
        }
    }

    [HarmonyPatch]
    public class NetworkObjectManager
    {
        static GameObject networkPrefab;
        private static ManualLogSource logger = Plugin.LoggerInstance;

        [HarmonyPostfix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.Start))]
        public static void Init()
        {
            logger.LogDebug("Initializing network prefab...");
            if (networkPrefab != null)
                return;

            if (ModAssets == null) { logger.LogError("Couldnt get ModAssets to create network handler"); return; }
            networkPrefab = (GameObject)ModAssets.LoadAsset("Assets/ModAssets/NetworkHandlerSCP4666.prefab");
            logger.LogDebug("Got networkPrefab");
            //networkPrefab.AddComponent<NetworkHandlerSCP4666>();
            //logger.LogDebug("Added component");

            NetworkManager.Singleton.AddNetworkPrefab(networkPrefab);
            logger.LogDebug("Added networkPrefab");
        }

        [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Awake))]
        static void SpawnNetworkHandler()
        {
            if (IsServerOrHost)
            {
                var networkHandlerHost = UnityEngine.Object.Instantiate(networkPrefab, Vector3.zero, Quaternion.identity);
                logger.LogDebug("Instantiated networkHandlerHost");
                networkHandlerHost.GetComponent<NetworkObject>().Spawn();
                logger.LogDebug("Spawned network object");
            }
        }
    }
}