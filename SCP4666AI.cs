using BepInEx.Logging;
using GameNetcodeStuff;
using HandyCollections.Heap;
using HarmonyLib;
using SCP4666.YulemanKnife;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666
{
    internal class SCP4666AI : EnemyAI
    {
        private static ManualLogSource logger = LoggerInstance;
        public static SCP4666AI? Instance { get; private set; }

#pragma warning disable 0649
        public NetworkAnimator networkAnimator = null!;
        public Transform RightHandTransform = null!;
        public GameObject ChildObj = null!;
        public GameObject KnifeObj = null!;
        public YulemanKnifeBehavior KnifeScript = null!;
        public GameObject ChildSackPrefab = null!;
        public GameObject TeethObj = null!;
        public AudioClip FootstepSFX = null!;
        public AudioClip TeleportSFX = null!;
        public AudioClip LaughSFX = null!;
#pragma warning restore 0649

        List<PlayerControllerB> TargetPlayers = [];
        bool localPlayerHasSeenYuleman = false;

        float timeSinceDamagePlayer;

        // Constants

        // Config Values
        int minPresentCount;
        int maxPresentCount;

        public enum State
        {
            Spawning,
            Chasing,
            Abducting
        }

        public void SwitchToBehaviourStateCustom(State state)
        {
            logger.LogDebug("Switching to state: " + state);

            switch (state)
            {
                case State.Chasing:

                    break;
                case State.Abducting:

                    break;
                default:
                    break;
            }

            SwitchToBehaviourClientRpc((int)state);
        }

        public override void Start()
        {
            base.Start();
            logger.LogDebug("SCP-4666 Spawned");

            currentBehaviourStateIndex = (int)State.Spawning;

            if (IsServerOrHost) { StartCoroutine(DelayedStart()); }
        }

        IEnumerator DelayedStart()
        {
            yield return new WaitUntil(() => NetworkObject.IsSpawned);

            if (Instance != null && NetworkObject.IsSpawned)
            {
                logger.LogDebug("There is already a SCP-4666 in the scene. Removing this one.");
                NetworkObject.Despawn(true);
            }
            else
            {
                Instance = this;
                logger.LogDebug("Finished spawning SCP-4666");
            }
        }

        public override void Update()
        {
            base.Update();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            timeSinceDamagePlayer += Time.deltaTime;

            if (localPlayer.HasLineOfSightToPosition(transform.position))
            {
                localPlayer.IncreaseFearLevelOverTime(0.1f, 0.5f);

                if (!localPlayerHasSeenYuleman)
                {
                    localPlayerHasSeenYuleman = true;
                    AddTargetPlayerServerRpc(localPlayer.actualClientId);
                }
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f)
            {
                return;
            };

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Spawning:

                    break;

                case (int)State.Chasing:

                    if (TargetPlayers.Count == 0)
                    {
                        SwitchToBehaviourStateCustom(State.Abducting);
                        return;
                    }

                    if (!TargetClosestPlayer())
                    {
                        
                    }

                    break;

                case (int)State.Abducting:

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        public void Teleport(Vector3 position, bool _isOutside)
        {

        }

        public bool TargetClosestPlayer()
        {
            float closestDistance = 4000f;

            foreach (var player in TargetPlayers)
            {
                if (player.isPlayerDead)
                {
                    TargetPlayers.Remove(player);
                    continue;
                }
                float distance = Vector3.Distance(player.transform.position, transform.position);
                if (distance < closestDistance)
                {
                    targetPlayer = player;
                    closestDistance = distance;
                }
            }

            return targetPlayer != null;
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1)
        {
            if (!isEnemyDead && !inSpecialAnimation)
            {
                enemyHP -= force;
                if (enemyHP <= 0)
                {
                    KillEnemyOnOwnerClient();
                    return;
                }
            }
        }

        public override void HitFromExplosion(float distance)
        {
            base.HitFromExplosion(distance);
            if (distance < 2)
            {
                HitEnemy(10);
            }
            else if (distance < 3)
            {
                HitEnemy(8);
            }
            else if (distance < 5)
            {
                HitEnemy(7);
            }
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client
        {
            base.OnCollideWithPlayer(other);
            if (timeSinceDamagePlayer > 1f)
            {
                PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
                if (player != null && !player.isPlayerDead && !inSpecialAnimation && !isEnemyDead)
                {
                    timeSinceDamagePlayer = 0f;


                }
            }
        }

        // Animation Functions

        public void MakeKnifeVisible()
        {

        }

        public void MakeKnifeInvisible()
        {

        }

        public void PlayRoarSFX()
        {

        }

        public void ThrowKnife()
        {
            
        }

        public void CallKnifeBack()
        {

        }

        public void PlayFootstepSFX()
        {

        }

        public void DestroyChildObject()
        {

        }

        public void GrabPlayer()
        {

        }

        public void PutPlayerInSack()
        {
            
        }

        // RPC's

        [ServerRpc(RequireOwnership = false)]
        public void AddTargetPlayerServerRpc(ulong clientId)
        {
            if (IsServerOrHost)
            {
                PlayerControllerB player = PlayerFromId(clientId);
                if (TargetPlayers.Contains(player)) { return; }
                TargetPlayers.Add(player);
                logger.LogDebug($"Added {player.playerUsername} to targeted players");
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public new void SwitchToBehaviourServerRpc(int stateIndex)
        {
            if (IsServerOrHost)
            {
                SwitchToBehaviourStateCustom((State)stateIndex);
            }
        }

        [ClientRpc]
        void IncreaseFearOfNearbyPlayersClientRpc(float value, float distance)
        {
            if (Vector3.Distance(transform.position, localPlayer.transform.position) < distance)
            {
                localPlayer.JumpToFearLevel(value);
            }
        }

        [ClientRpc]
        private void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
        }

        [ServerRpc(RequireOwnership = false)]
        void DoAnimationServerRpc(string animationName)
        {
            if (IsServerOrHost)
            {
                networkAnimator.SetTrigger(animationName);
            }
        }

        [ClientRpc]
        void DoAnimationClientRpc(string animationName, bool value)
        {
            creatureAnimator.SetBool(animationName, value);
        }
    }

    //[HarmonyPatch]
    internal class SCP3231Patches
    {
        private static ManualLogSource logger = LoggerInstance;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SpawnEnemyFromVent))]
        public static bool SpawnEnemyFromVentPrefix(EnemyVent vent)
        {
            try
            {
                if (IsServerOrHost)
                {

                }
            }
            catch (System.Exception e)
            {
                logger.LogError(e);
                return true;
            }

            return true;
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity