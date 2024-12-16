using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666
{
    internal class ChildSackBehavior : PhysicsProp
    {
        public enum RespawnType
        {
            Manual,
            TeamWipe,
            ActivatedTeamWipe,
            Random
        }

        public RespawnType respawnType = RespawnType.TeamWipe;
        public bool activated;

        public void Activate()
        {
            if (!IsServerOrHost) { return; }
            if (playerHeldBy != null) { playerHeldBy.DropAllHeldItemsAndSync(); }

            if (respawnType == RespawnType.Random)
            {
                // TODO: Do random spawning
            }

            StartCoroutine(ReviveAndDespawnAfterDelay(2f));
        }

        IEnumerator ReviveAndDespawnAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);

            foreach (var player in StartOfRound.Instance.allPlayerScripts.Where(x => x.isPlayerDead))
            {
                float size = UnityEngine.Random.Range(0.6f, 0.9f);
                MakePlayerSmallClientRpc(player.actualClientId, size);
            }

            yield return new WaitForSeconds(1f);

            ReviveDeadPlayersClientRpc();

            NetworkObject.Despawn();
        }

        public void SpawnPresent()
        {
            Item giftItem = StartOfRound.Instance.allItemsList.itemsList.Where(x => x.name == "GiftBox").FirstOrDefault();
            GiftBoxItem gift = GameObject.Instantiate(giftItem.spawnPrefab, transform.position, Quaternion.identity).GetComponentInChildren<GiftBoxItem>();
            gift.NetworkObject.Spawn();
        }

        [ClientRpc]
        public void ReviveDeadPlayersClientRpc()
        {
            StartOfRound.Instance.ReviveDeadPlayers();
        }

        [ClientRpc]
        public void MakePlayerSmallClientRpc(ulong clientId, float size)
        {
            PlayerControllerB player = PlayerFromId(clientId);
            player.thisPlayerBody.localScale = new Vector3(size, size, size);
        }
    }
}
