using BepInEx.Logging;
using GameNetcodeStuff;
using HandyCollections.Heap;
using HarmonyLib;
using SCP4666.YulemanKnife;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;
using static MonoMod.Cil.RuntimeILReferenceBag.FastDelegateInvokers;
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
        public Transform ChildSackTransform = null!;
        public GameObject KnifeMeshObj = null!;
        public YulemanKnifeBehavior? KnifeScript = null!;
        public GameObject KnifePrefab = null!;
        public GameObject ChildSackPrefab = null!;
        public AudioClip FootstepSFX = null!;
        public AudioClip TeleportSFX = null!;
        public AudioClip LaughSFX = null!;
        public AudioClip RoarSFX = null!;
#pragma warning restore 0649

        Vector3 mainEntranceOutsidePosition;
        Vector3 mainEntrancePosition;
        Vector3 escapePosition;

        List<PlayerControllerB> TargetPlayers = [];
        PlayerControllerB? playerInSack = null;
        bool localPlayerHasSeenYuleman = false;

        float timeSinceDamagePlayer;
        float timeSinceTeleport;
        float timeSinceKnifeThrown;

        bool teleporting;

        bool isKnifeOwned = true;
        bool isKnifeThrown;
        bool callingKnifeBack;
        bool isAngry;
        bool grabbingPlayer;
        int timesHitWhileAbducting;

        // Constants

        // Config Values
        int minPresentCount = 3;
        int maxPresentCount = 5;
        float teleportCooldown = 10f;
        float knifeThrowCooldown = 10f;
        float knifeReturnCooldown = 1f;
        float knifeThrowMinDistance = 5f;
        float knifeThrowMaxDistance = 10f;
        float teleportDistance = 15f;
        float distanceToPickUpKnife = 15f;
        int sliceDamage = 25;
        int slapDamage = 10;
        int hitAmountToDropPlayer = 5;

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
                    GetEscapeNode();
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

            mainEntrancePosition = RoundManager.FindMainEntrancePosition();
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(false, true);

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
            timeSinceTeleport += Time.deltaTime;
            timeSinceKnifeThrown += Time.deltaTime;

            if (localPlayer.HasLineOfSightToPosition(transform.position, 30f, 20) && localPlayer != playerInSack)
            {
                localPlayer.IncreaseFearLevelOverTime(0.1f, 0.5f);

                if (!localPlayerHasSeenYuleman)
                {
                    localPlayerHasSeenYuleman = true;
                    AddTargetPlayerServerRpc(localPlayer.actualClientId);
                }
            }

            if (inSpecialAnimationWithPlayer != null && grabbingPlayer)
            {
                inSpecialAnimationWithPlayer.transform.position = RightHandTransform.position;
            }

            if (playerInSack != null)
            {
                playerInSack.transform.position = ChildSackTransform.position;
            }

            if (stunNormalizedTimer > 0f || inSpecialAnimation)
            {
                agent.speed = 0f;
                return;
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f)
            {
                logger.LogDebug("Not doing ai interval");
                return;
            };

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Spawning:
                    //agent.speed = 0f;

                    break;

                case (int)State.Chasing:
                    agent.speed = isAngry ? 7f : 5f;

                    if (TargetPlayers.Count == 0 || !TargetClosestPlayer())
                    {
                        SwitchToBehaviourStateCustom(State.Abducting);
                        return;
                    }

                    if (timeSinceTeleport > teleportCooldown
                        && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance

                        && !isKnifeThrown
                        && !callingKnifeBack
                        && !inSpecialAnimation)
                    {
                        if ((timeSinceTeleport > teleportCooldown && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance)
                            || (isAngry && timeSinceTeleport > teleportCooldown / 2 && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance / 2))
                        timeSinceTeleport = 0f;
                        teleporting = true;
                        TeleportToTargetPlayer();
                        return;
                    }

                    if (isKnifeOwned && !inSpecialAnimation)
                    {
                        if (isKnifeThrown && !callingKnifeBack && timeSinceKnifeThrown > knifeReturnCooldown && KnifeScript != null && !KnifeScript.isThrown)
                        {
                            callingKnifeBack = true;
                            networkAnimator.SetTrigger("grab");
                        }
                        if (!isKnifeThrown
                            && !callingKnifeBack
                            && !teleporting
                            && timeSinceKnifeThrown > knifeThrowCooldown)
                        {
                            logger.LogDebug("Begin throwing knife");
                            float distance = Vector3.Distance(transform.position, targetPlayer.transform.position);
                            if (distance > knifeThrowMinDistance && distance < knifeThrowMaxDistance)
                            {
                                timeSinceKnifeThrown = 0f;
                                transform.LookAt(targetPlayer.transform.position);
                                inSpecialAnimation = true;
                                networkAnimator.SetTrigger("throw");
                                return;
                            }
                        }
                    }

                    if (!isKnifeOwned && KnifeScript != null && KnifeScript.playerHeldBy == null && KnifeScript.hasHitGround && !KnifeScript.isHeldByEnemy && Vector3.Distance(transform.position, KnifeScript.transform.position) < distanceToPickUpKnife)
                    {
                        Vector3 position = RoundManager.Instance.GetNavMeshPosition(KnifeScript.transform.position);
                        if (SetDestinationToPosition(position, true))
                        {
                            if (Vector3.Distance(position, transform.position) < 1f)
                            {
                                networkAnimator.SetTrigger("pickup");
                                KnifeScript.NetworkObject.Despawn();
                                KnifeScript = null;
                            }
                            return;
                        }
                    }

                    //logger.LogDebug("Setting destination");
                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                case (int)State.Abducting:
                    agent.speed = 6f;

                    if (TargetPlayers.Count > 0)
                    {
                        SwitchToBehaviourStateCustom(State.Chasing);
                        return;
                    }

                    if (isOutside)
                    {
                        if (daytimeEnemyLeaving) { return; }
                        if (Vector3.Distance(transform.position, escapePosition) < 1f)
                        {
                            daytimeEnemyLeaving = true;
                            DaytimeEnemyLeave();
                        }

                        SetDestinationToPosition(escapePosition);
                        return;
                    }

                    // IsInside
                    if (!teleporting && Vector3.Distance(transform.position, mainEntrancePosition) < 1f)
                    {
                        teleporting = true;
                        Teleport(mainEntranceOutsidePosition, true);
                        return;
                    }

                    SetDestinationToPosition(mainEntrancePosition);

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        public void GetEscapeNode()
        {
            Vector3 farthestPosition = mainEntranceOutsidePosition;
            float farthestDistance = 0f;

            foreach (var node in RoundManager.Instance.outsideAINodes)
            {
                float distance = Vector3.Distance(node.transform.position, mainEntranceOutsidePosition);
                if (distance <= farthestDistance) { continue; }
                if (!CalculatePath(mainEntranceOutsidePosition, node.transform.position)) { continue; }
                farthestPosition = node.transform.position;
                farthestDistance = distance;
            }

            escapePosition = farthestPosition;
        }

        public void Teleport(Vector3 position, bool outside)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            agent.Warp(position);
            SetEnemyOutsideClientRpc(outside);
            teleporting = false;
        }

        public void TeleportToTargetPlayer()
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
            teleporting = false;
        }

        bool GetTeleportNode()
        {
            targetNode = null;

            if (targetPlayer.isInsideFactory == isOutside)
            {
                SetEnemyOutsideClientRpc(!targetPlayer.isInsideFactory);
            }

            float closestDistance = 4000f;

            foreach (var node in allAINodes)
            {
                if (node != null && !targetPlayer.HasLineOfSightToPosition(node.transform.position, 45, 10, 5))
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
            if (isAngry)
            {
                if (targetPlayer != null && !targetPlayer.isPlayerDead)
                {
                    return true;
                }
            }

            float closestDistance = 4000f;
            PlayerControllerB? newPlayerToTarget = null;

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
                    newPlayerToTarget = player;
                    closestDistance = distance;
                }
            }

            if (newPlayerToTarget != null && targetPlayer != newPlayerToTarget) { ChangeTargetPlayerClientRpc(newPlayerToTarget.actualClientId); }
            targetPlayer = newPlayerToTarget;
            return targetPlayer != null;
        }

        public void FreezePlayer(PlayerControllerB player, bool value)
        {
            player.disableInteract = value;
            player.disableLookInput = value;
            player.disableMoveInput = value;
        }

        public void DropPlayerFromSack()
        {
            if (playerInSack == null)
            {
                logger.LogError("Player in sack in null");
                timesHitWhileAbducting = 0;
                creatureAnimator.SetBool("bagWalk", false);
                return;
            }

            if (localPlayer == playerInSack)
            {
                MakePlayerScreenBlack(false);
            }

            TargetPlayers.Add(playerInSack);

            playerInSack = null;
            timesHitWhileAbducting = 0;
            creatureAnimator.SetBool("bagWalk", false);
        }

        #region Overrides
        public override void DaytimeEnemyLeave()
        {
            base.DaytimeEnemyLeave();
            if (!IsServerOrHost || currentBehaviourStateIndex != (int)State.Abducting) { return; }
            if (playerInSack != null)
            {
                KillPlayerClientRpc(playerInSack.actualClientId);
                playerInSack = null;
            }
            KillEnemyOnOwnerClient(true);

        }

        public override void KillEnemy(bool destroy = false)
        {
            if (inSpecialAnimation) { return; }
            if (playerInSack != null) { DropPlayerFromSack(); }
            if (IsServerOrHost && !daytimeEnemyLeaving)
            {
                if (isKnifeOwned)
                {
                    YulemanKnifeBehavior knife = GameObject.Instantiate(KnifePrefab, transform.position, Quaternion.identity).GetComponentInChildren<YulemanKnifeBehavior>();
                    knife.NetworkObject.Spawn(true);
                    knife.FallToGround();
                }
                ChildSackBehavior sack = GameObject.Instantiate(ChildSackPrefab, transform.position, Quaternion.identity).GetComponentInChildren<ChildSackBehavior>();
                sack.NetworkObject.Spawn();
            }
            base.KillEnemy(destroy);
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1) // Runs on all clients
        {
            if (isEnemyDead || inSpecialAnimation) return;

            enemyHP -= force;

            if (enemyHP <= 0)
            {
                KillEnemyOnOwnerClient();
                if (playerInSack != null)
                {
                    DropPlayerFromSack();
                }
                return;
            }

            if (playerInSack != null)
            {
                timesHitWhileAbducting++;
                if (timesHitWhileAbducting >= hitAmountToDropPlayer)
                {
                    DropPlayerFromSack();
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

        public override void OnCollideWithPlayer(Collider other) // This only runs on client collided with
        {
            base.OnCollideWithPlayer(other);
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (playerInSack != null && player != null && playerInSack == player) { return; }
            if (timeSinceDamagePlayer > 3f)
            {
                if (player != null && !player.isPlayerDead && !inSpecialAnimation && !isEnemyDead && player == targetPlayer)
                {
                    timeSinceDamagePlayer = 0f;

                    if (IsPlayerChild(player))
                    {
                        inSpecialAnimation = true;
                        FreezePlayer(player, true);
                        GrabPlayerServerRpc(player.actualClientId);
                        return;
                    }

                    if (isKnifeOwned && !isKnifeThrown)
                    {
                        int deathAnim = UnityEngine.Random.Range(0, 2) == 1 ? 7 : 0;
                        player.DamagePlayer(sliceDamage, true, true, CauseOfDeath.Stabbing, deathAnim);
                        DoAnimationServerRpc("slash");
                    }
                    else
                    {
                        player.DamagePlayer(slapDamage, true, true, CauseOfDeath.Mauling, 0, false, transform.forward * 5);
                        DoAnimationServerRpc("slap");
                    }
                }
            }
        }

        #endregion

        #region Animation
        // Animation Functions

        public void SetInSpecialAnimation()
        {
            inSpecialAnimation = true;
        }

        public void UnsetInSpecialAnimation()
        {
            inSpecialAnimation = false;
        }

        public void CallKnifeBack()
        {
            inSpecialAnimation = true;
            logger.LogDebug("CallKnifeBack() called");

            if (KnifeScript == null)
            {
                logger.LogError("NO KNIFE");
                callingKnifeBack = false;
                return;
            }

            if (KnifeScript.playerHeldBy != null)
            {
                logger.LogDebug("Call knife back failed, player has knife");
                targetPlayer = KnifeScript.playerHeldBy;
                isKnifeOwned = false;
                isKnifeThrown = false;
                MakeKnifeInvisible();
                return;
            }

            // Call knife back
            logger.LogDebug("Player doesnt have knife, calling it back");
            KnifeScript.ReturnToYuleman();
        }

        public void CheckForKnife()
        {
            logger.LogDebug("CheckForKnife() called");
            callingKnifeBack = false;

            if (!isKnifeOwned)
            {
                logger.LogDebug("Knife not owned, starting roar animation");
                creatureAnimator.SetTrigger("roar");
                isAngry = true;
                return;
            }

            callingKnifeBack = false;
            GrabKnife();
        }

        public void GrabKnife()
        {
            logger.LogDebug("GrabKnife() called");
            if (KnifeScript != null) { KnifeScript.previousPlayerHeldBy = null; }
            KnifeScript = null;
            isKnifeOwned = true;
            isKnifeThrown = false;
            callingKnifeBack = false;
            timeSinceKnifeThrown = 0f;
            MakeKnifeVisible();
            isAngry = false;
            logger.LogDebug("Grabbed knife");
        }

        public void ThrowKnife()
        {
            logger.LogDebug("ThrowKnife() called");
            if (!isKnifeOwned || isKnifeThrown) { return; }
            logger.LogDebug("In throwing knife animation");
            isKnifeThrown = true;
            inSpecialAnimation = false;
            if (!IsServerOrHost) { return; }
            KnifeScript = UnityEngine.GameObject.Instantiate(KnifePrefab, RightHandTransform.position, Quaternion.identity).GetComponentInChildren<YulemanKnifeBehavior>();
            KnifeScript.NetworkObject.Spawn(destroyWithScene: true);
            ThrowKnifeClientRpc(KnifeScript.NetworkObject);
        }

        public void MakeKnifeVisible()
        {
            logger.LogDebug("MakeKnifeVisible() called");
            if (!isKnifeOwned || isKnifeThrown) { return; }
            KnifeMeshObj.SetActive(true);
        }

        public void MakeKnifeInvisible()
        {
            logger.LogDebug("MakeKnifeInvisible() called");
            KnifeMeshObj.SetActive(false);
        }

        public void PlayRoarSFX()
        {
            creatureVoice.PlayOneShot(RoarSFX);
        }

        public void PlayFootstepSFX()
        {
            creatureSFX.PlayOneShot(FootstepSFX);
        }

        public void GrabPlayer()
        {
            logger.LogDebug("GrabPlayer() called");
            inSpecialAnimation = true;
            inSpecialAnimationWithPlayer = targetPlayer;
            grabbingPlayer = true;
        }

        public void PutPlayerInSack()
        {
            logger.LogDebug("PutPlayerInSack() called");
            grabbingPlayer = false;
            inSpecialAnimation = false;
            playerInSack = inSpecialAnimationWithPlayer;
            inSpecialAnimationWithPlayer = null;
            TargetPlayers.Remove(playerInSack);
            creatureVoice.PlayOneShot(LaughSFX);
            creatureAnimator.SetBool("bagWalk", true);

            if (localPlayer == playerInSack)
            {
                MakePlayerScreenBlack(true);
            }
        }

        public void FinishStartAnimation()
        {
            if (IsServerOrHost)
            {
                SwitchToBehaviourStateCustom(State.Chasing);
            }
        }

        #endregion

        // Test function
        public static void ChangePlayerSize(float size)
        {
            if (Instance == null) { return; }
            Instance.ChangePlayerSizeServerRpc(localPlayer.actualClientId, size);
        }

        // RPC's

        [ServerRpc(RequireOwnership = false)]
        public void ChangePlayerSizeServerRpc(ulong clientId, float size)
        {
            ChangePlayerSizeClientRpc(clientId, size);
        }

        [ClientRpc]
        public void ChangePlayerSizeClientRpc(ulong clientId, float size)
        {
            PlayerControllerB player = PlayerFromId(clientId);
            player.thisPlayerBody.localScale = new Vector3(size, size, size);
        }

        [ClientRpc]
        public void ThrowKnifeClientRpc(NetworkObjectReference netRef)
        {
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Couldnt get knife to throw"); return; }
            if (!netObj.TryGetComponent(out YulemanKnifeBehavior knife)) { logger.LogError("Couldnt get knife to throw"); return; }
            KnifeScript = knife;
            Vector3 throwDirection = (targetPlayer.bodyParts[5].position - RightHandTransform.position).normalized;
            KnifeScript.ThrowKnife(throwDirection);
            logger.LogDebug("Knife was thrown");
        }

        [ClientRpc]
        public void KillPlayerClientRpc(ulong clientId)
        {
            if (localPlayer.actualClientId != clientId) { return; }
            MakePlayerScreenBlack(false);
            localPlayer.KillPlayer(Vector3.zero, false);
        }

        [ServerRpc(RequireOwnership = false)]
        public void DoAnimationServerRpc(string animationName)
        {
            if (!IsServerOrHost) { return; }
            logger.LogDebug("Doing " + animationName + " animation");
            networkAnimator.SetTrigger(animationName);
        }

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
        public void GrabPlayerServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }

            ChangeTargetPlayerClientRpc(clientId);
            targetPlayer = PlayerFromId(clientId);
            inSpecialAnimation = true;
            networkAnimator.SetTrigger("grabPlayer");
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
            if (IsServerOrHost) { return; }
            targetPlayer = PlayerFromId(clientId);
            logger.LogDebug("Changed target player to " + targetPlayer.playerUsername);
        }

        [ClientRpc]
        private void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity