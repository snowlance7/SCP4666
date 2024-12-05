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
        public GameObject? KnifeObj = null!;
        public YulemanKnifeBehavior? KnifeScript = null!;
        public GameObject ChildSackPrefab = null!;
        public AudioClip FootstepSFX = null!;
        public AudioClip TeleportSFX = null!;
        public AudioClip LaughSFX = null!;
        public AudioClip RoarSFX = null!;
#pragma warning restore 0649

        List<PlayerControllerB> TargetPlayers = [];
        bool localPlayerHasSeenYuleman = false;

        float timeSinceDamagePlayer;
        float timeSinceTeleport;
        float timeSinceKnifeThrow;

        private bool teleporting;
        private bool throwingKnife;
        bool isKnifeThrown;
        private bool callingKnifeBack;

        // Constants

        // Config Values
        int minPresentCount = 3;
        int maxPresentCount = 5;
        float teleportCooldown = 10f;
        float knifeThrowCooldown = 5f;
        float knifeReturnCooldown = 1f;
        float knifeThrowMinDistance = 5f;
        float knifeThrowMaxDistance = 10f;
        float teleportDistance = 15f;

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

            if (IsServerOrHost)
            {
                // Spawn presents
                SetEnemyOutsideClientRpc(true);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null)
            {
                logger.LogDebug("There is already a SCP-4666 in the scene. Removing this one.");
                NetworkObject.Despawn(true);
                return;
            }
            Instance = this;
            logger.LogDebug("Finished spawning SCP-4666");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            Instance = null;
        }

        public override void Update()
        {
            base.Update();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            timeSinceDamagePlayer += Time.deltaTime;
            timeSinceTeleport += Time.unscaledDeltaTime;
            timeSinceKnifeThrow += Time.unscaledDeltaTime;

            if (localPlayer.HasLineOfSightToPosition(transform.position, 30f, 20))
            {
                localPlayer.IncreaseFearLevelOverTime(0.1f, 0.5f);

                if (!localPlayerHasSeenYuleman)
                {
                    localPlayerHasSeenYuleman = true;
                    AddTargetPlayerServerRpc(localPlayer.actualClientId);
                }
            }

            if (inSpecialAnimation || stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
                return;
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f || inSpecialAnimation)
            {
                return;
            };

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Spawning:

                    break;

                case (int)State.Chasing:

                    if (TargetPlayers.Count == 0 || !TargetClosestPlayer())
                    {
                        SwitchToBehaviourStateCustom(State.Abducting);
                        return;
                    }

                    if (timeSinceTeleport > teleportCooldown
                        && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance
                        && !throwingKnife)
                    {
                        timeSinceTeleport = 0f;
                        teleporting = true;
                        Teleport();
                    }

                    /*if (KnifeScript != null)
                    {
                        if (!throwingKnife
                            && !teleporting
                            && timeSinceKnifeThrow > knifeThrowCooldown)
                        {
                            float distance = Vector3.Distance(transform.position, targetPlayer.transform.position);
                            if (distance > knifeThrowMinDistance && distance < knifeThrowMaxDistance)
                            {
                                throwingKnife = true;
                                timeSinceKnifeThrow = 0f;
                                inSpecialAnimation = true;
                                networkAnimator.SetTrigger("throw");
                                return;
                            }
                        }
                        if (throwingKnife && !callingKnifeBack && timeSinceKnifeThrow > knifeReturnCooldown)
                        {
                            callingKnifeBack = true;
                            CallKnifeBack();
                        }
                    }*/

                    logger.LogDebug("Setting destination");
                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                case (int)State.Abducting:

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        public void Teleport()
        {
            StartCoroutine(TeleportCoroutine());
        }

        IEnumerator TeleportCoroutine()
        {
            PlayTeleportSFXClientRpc();
            yield return new WaitForSeconds(2f);
            if (!GetTeleportNode()) { yield break; }
            Vector3 pos = RoundManager.Instance.GetNavMeshPosition(targetNode.transform.position, RoundManager.Instance.navHit);
            agent.Warp(pos);
            PlayLaughSFXClientRpc();
            inSpecialAnimation = false;
            teleporting = false;
        }

        bool GetTeleportNode() // TODO: Test this
        {
            targetNode = null;

            if (targetPlayer.isInsideFactory == isOutside)
            {
                SetEnemyOutsideClientRpc(!targetPlayer.isInsideFactory);
            }

            float closestDistance = 4000f;

            foreach (var node in allAINodes)
            {
                if (node != null && targetPlayer.HasLineOfSightToPosition(node.transform.position, 45, 10, 5)) // TODO: Test this
                {
                    float distance = Vector3.Distance(node.transform.position, targetPlayer.transform.position);
                    if (distance < closestDistance)
                    {
                        targetNode = node.transform;
                        closestDistance = distance;
                    }
                }
            }

            return targetNode != null;
        }

        public bool TargetClosestPlayer()
        {
            float closestDistance = 4000f;

            foreach (var player in TargetPlayers)
            {
                if (player.isPlayerDead || player.disconnectedMidGame)
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

        public static bool IsPlayerChild(PlayerControllerB player)
        {
            return player.thisPlayerBody.localScale.y < 1f;
        }

        public void CallKnifeBack()
        {
            if (KnifeScript == null) { return; }
            if (KnifeScript.playerHeldBy != null)
            {
                targetPlayer = KnifeScript.playerHeldBy;
                KnifeScript = null;
                // Get angry

                return;
            }

            // Call knife back
            KnifeScript.ReturnToYuleman();
        }

        public void GrabKnife()
        {
            if (KnifeScript != null && KnifeObj != null)
            {
                isKnifeThrown = false;
                KnifeObj.transform.SetParent(RightHandTransform);
                KnifeScript.parentObject = RightHandTransform;
                KnifeScript.isHeldByEnemy = true;
                KnifeScript.isHeld = true;
                KnifeScript.grabbable = false;
            }
        }

        // Animation Functions

        public void MakeKnifeVisible()
        {
            KnifeObj.SetActive(true);
        }

        public void MakeKnifeInvisible()
        {
            KnifeObj.SetActive(false);
        }

        public void PlayRoarSFX()
        {
            creatureVoice.PlayOneShot(RoarSFX);
        }

        public void ThrowKnife()
        {
            logger.LogDebug("knife thrown");
            inSpecialAnimation = false;
            throwingKnife = false;
            isKnifeThrown = true;
            Vector3 throwDirection = (transform.position - targetPlayer.transform.position).normalized;
            KnifeScript.ThrowKnife(throwDirection);
        }

        public void PlayFootstepSFX()
        {
            creatureSFX.PlayOneShot(FootstepSFX, 1f);
        }

        public void GrabPlayer()
        {

        }

        public void PutPlayerInSack()
        {
            
        }

        public void FinishStartAnimation()
        {
            if (IsServerOrHost)
            {
                SwitchToBehaviourStateCustom(State.Chasing);
            }
            KnifeObj.SetActive(true);
        }

        // RPC's

        [ClientRpc]
        public void PlayTeleportSFXClientRpc()
        {
            creatureVoice.PlayOneShot(TeleportSFX);
        }

        [ClientRpc]
        public void PlayLaughSFXClientRpc()
        {
            creatureVoice.PlayOneShot(LaughSFX);
        }

        [ServerRpc(RequireOwnership = false)]
        public void AddTargetPlayerServerRpc(ulong clientId)
        {
            if (IsServerOrHost)
            {
                PlayerControllerB player = PlayerFromId(clientId);

                if (currentBehaviourStateIndex == (int)State.Spawning)
                {
                    networkAnimator.SetTrigger("start");
                }

                if (TargetPlayers.Contains(player)) { return; }
                TargetPlayers.Add(player);
                logger.LogDebug($"Added {player.playerUsername} to targeted players");
            }
        }

        [ClientRpc]
        public void ChangeTargetPlayerClientRpc(ulong clientId)
        {
            targetPlayer = PlayerFromId(clientId);
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