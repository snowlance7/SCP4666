using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666
{
    public class NetworkHandlerSCP4666 : NetworkBehaviour
    {
#pragma warning disable CS8618
        public static NetworkHandlerSCP4666 Instance { get; private set; }
        public GameObject blackScreenOverlay;
#pragma warning restore CS8618

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

        public void Update()
        {
            if (blackScreenOverlay.activeSelf)
            {
                if (!localPlayer.isPlayerControlled || !SCP4666AI.Instances.Any(x => x.inSpecialAnimationWithPlayer == localPlayer))
                {
                    blackScreenOverlay.SetActive(false);
                    Utils.FreezePlayer(localPlayer, false);
                }
            }
        }

        [ClientRpc]
        private void ChangePlayerSizeClientRpc(ulong clientId, float size)
        {
            PlayerControllerB player = PlayerFromId(clientId);
            player.thisPlayerBody.localScale = new Vector3(size, size, size);
            Utils.RebuildRig(player);
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

        [HarmonyPostfix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.Start))]
        public static void Init()
        {
            if (networkPrefab != null)
                return;

            AssetBundle networkHandlerBundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), "scp4666_networkhandler"));

            if (networkHandlerBundle == null) { logger.LogError("Couldnt get assets to create network handler"); return; }
            networkPrefab = (GameObject)networkHandlerBundle.LoadAsset("Assets/ModAssets/NetworkHandlerSCP4666.prefab");

            NetworkManager.Singleton.AddNetworkPrefab(networkPrefab);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Awake))]
        static void SpawnNetworkHandler()
        {
            if (!IsServerOrHost || networkPrefab == null) { return; }

            var networkHandlerHost = UnityEngine.Object.Instantiate(networkPrefab, Vector3.zero, Quaternion.identity);
            networkHandlerHost!.GetComponent<NetworkObject>().Spawn();
            logger.LogDebug("Spawned NetworkHandlerSCP4666");
        }
    }
}