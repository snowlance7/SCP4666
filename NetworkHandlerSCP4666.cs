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
            if (IsServer)
            {
                if (Instance != null && Instance != this)
                {
                    Instance.gameObject.GetComponent<NetworkObject>().Despawn();
                    log("Despawned network object");
                }
            }

            hideFlags = HideFlags.HideAndDontSave;
            Instance = this;
            log("set instance to this");
            base.OnNetworkSpawn();
            log("base.OnNetworkSpawn");
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
            log("ChangePlayerSizeClientRpc() called");
            PlayerControllerB player = PlayerFromId(clientId);
            player.thisPlayerBody.localScale = new Vector3(size, size, size);
            RebuildRig(player);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangePlayerSizeServerRpc(ulong clientId, float size)
        {
            log("ChangePlayerSizeServerRpc() called");
            if (!IsServer) { return; }
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
            log("Initializing network prefab...");
            if (networkPrefab != null)
                return;

            if (ModAssets == null) { logger.LogError("Couldnt get ModAssets to create network handler"); return; }
            networkPrefab = (GameObject)ModAssets.LoadAsset("Assets/ModAssets/NetworkHandlerSCP4666.prefab");
            log("Got networkPrefab");
            //networkPrefab.AddComponent<NetworkHandlerSCP4666>();
            //log("Added component");

            NetworkManager.Singleton.AddNetworkPrefab(networkPrefab);
            log("Added networkPrefab");
        }

        [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Awake))]
        static void SpawnNetworkHandler()
        {
            if (IsServerOrHost)
            {
                var networkHandlerHost = UnityEngine.Object.Instantiate(networkPrefab, Vector3.zero, Quaternion.identity);
                log("Instantiated networkHandlerHost");
                networkHandlerHost.GetComponent<NetworkObject>().Spawn();
                log("Spawned network object");
            }
        }
    }
}