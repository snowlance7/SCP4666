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
                    Instance.gameObject.GetComponent<NetworkObject>().Despawn(true);
                }
            }

            hideFlags = HideFlags.HideAndDontSave;
            Instance = this;
            base.OnNetworkSpawn();
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
            PlayerControllerB player = PlayerFromId(clientId);
            player.thisPlayerBody.localScale = new Vector3(size, size, size);
            RebuildRig(player);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangePlayerSizeServerRpc(ulong clientId, float size)
        {
            if (!IsServer) { return; }
            ChangePlayerSizeClientRpc(clientId, size);
        }
    }

    [HarmonyPatch]
    public class NetworkObjectManager
    {
        static GameObject? networkPrefab;
        private static ManualLogSource logger = Plugin.LoggerInstance;

        [HarmonyPostfix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.Start))]
        public static void Init()
        {
            if (networkPrefab != null)
                return;

            if (ModAssets == null) { logger.LogError("Couldnt get ModAssets to create network handler"); return; }
            networkPrefab = (GameObject)ModAssets.LoadAsset("Assets/ModAssets/NetworkHandlerSCP4666.prefab"); // TODO: Set this up in unity editor

            NetworkManager.Singleton.AddNetworkPrefab(networkPrefab);

            GameObject EvilDoll = ModAssets!.LoadAsset<GameObject>("Assets/ModAssets/Doll/EvilFleshDoll.prefab");
            NetworkManager.Singleton.AddNetworkPrefab(EvilDoll);

            if (Utils.isBeta)
            {
                TESTING.EvilDollPrefab = EvilDoll;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Awake))]
        static void SpawnNetworkHandler()
        {
            if (!IsServerOrHost) { return; }

            var networkHandlerHost = UnityEngine.Object.Instantiate(networkPrefab, Vector3.zero, Quaternion.identity);
            networkHandlerHost!.GetComponent<NetworkObject>().Spawn();
            logger.LogDebug("Spawned NetworkHandlerSCP4666");
        }
    }
}