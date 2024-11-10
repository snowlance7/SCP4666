using BepInEx.Logging;
using GameNetcodeStuff;
using HandyCollections.Heap;
using HarmonyLib;
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
        public Transform HandTransform = null!;

#pragma warning restore 0649

        float timeSinceDamagePlayer;
        float timeSinceSeenPlayer;
        float timeSpawned;

        // Constants

        // Config Values


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
                case State.Spawning:

                    break;
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

            RoundManager.Instance.SpawnedEnemies.Add(this);
            //SetOutsideOrInside();

            timeSinceSeenPlayer = Mathf.Infinity;

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
            timeSinceSeenPlayer += Time.deltaTime;
            timeSpawned += Time.deltaTime;

            if (localPlayer.HasLineOfSightToPosition(transform.position, 50f))
            {
                localPlayer.IncreaseFearLevelOverTime(0.1f, 0.5f);
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

                    break;

                case (int)State.Abducting:

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        bool FoundClosestPlayerInRange(float range, float senseRange)
        {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        bool TargetClosestPlayerInAnyCase()
        {
            mostOptimalDistance = 2000f;
            targetPlayer = null;
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (!PlayerIsTargetable(player)) { continue; }
                tempDist = Vector3.Distance(transform.position, player.transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = player;
                }
            }

            return targetPlayer != null;
        }

        public void SetOutsideOrInside()
        {
            GameObject closestOutsideNode = GetClosestAINode(GameObject.FindGameObjectsWithTag("OutsideAINode").ToList());
            GameObject closestInsideNode = GetClosestAINode(GameObject.FindGameObjectsWithTag("AINode").ToList());

            if (Vector3.Distance(transform.position, closestOutsideNode.transform.position) < Vector3.Distance(transform.position, closestInsideNode.transform.position))
            {
                logger.LogDebug("Setting enemy outside");
                SetEnemyOutsideClientRpc(true);
                return;
            }
            logger.LogDebug("Setting enemy inside");
        }

        public GameObject GetClosestAINode(List<GameObject> nodes)
        {
            float closestDistance = Mathf.Infinity;
            GameObject closestNode = null!;
            foreach (GameObject node in nodes)
            {
                float distanceToNode = Vector3.Distance(transform.position, node.transform.position);
                if (distanceToNode < closestDistance)
                {
                    closestDistance = distanceToNode;
                    closestNode = node;
                }
            }
            return closestNode;
        }

        public override void KillEnemy(bool destroy = false)
        {

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
                HitEnemy(20);
            }
            else if (distance < 3)
            {
                HitEnemy(15);
            }
            else if (distance < 5)
            {
                HitEnemy(19);
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

        // RPC's

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