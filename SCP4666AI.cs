using BepInEx.Logging;
using GameNetcodeStuff;
using SCP4666.YulemanKnife;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Analytics;
using static SCP4666.Plugin;

namespace SCP4666
{
    internal class SCP4666AI : EnemyAI // TODO: Rework and add new stuff
    {
        private static ManualLogSource logger = LoggerInstance;
        public static SCP4666AI? Instance { get; private set; }

#pragma warning disable CS8618
        public Transform RightHandTransform;
        public Transform ChildSackTransform;

        public AudioClip FootstepSFX;
        public AudioClip TeleportSFX;
        public AudioClip LaughSFX;
        public AudioClip RoarSFX;

        public GameObject YulemanMesh;

        public Transform turnCompass;

        public GameObject ThrowingKnifePrefab;

        public GameObject ChildSackPrefab;
        public GameObject YulemanKnifePrefab;
        public GameObject EvilFleshDollPrefab;
        public GameObject FleshDollPrefab;

        public Collider collider;

        public GameObject KnifeMesh;

        public ThrownKnifeScript thrownKnifeScript;
#pragma warning restore CS8618

        Vector3 mainEntranceOutsidePosition;
        Vector3 mainEntrancePosition;

        List<PlayerControllerB> TargetPlayers = [];
        bool localPlayerHasSeenYuleman;
        bool spawnedAndVisible;

        bool isGrabbingPlayer;
        bool isPlayerInSack;

        float timeSinceDamagePlayer;
        float timeSinceTeleport;
        float timeSinceKnifeThrown;
        float timeSinceGrabPlayer;

        bool teleporting;

        bool isThrowingKnife;
        bool isCallingKnife;

        int timesHitWhileAbducting;

        int damageTakenWithoutDamaging;

        // Constants
        readonly Vector3 insideScale = new Vector3(1.5f, 1.5f, 1.5f);
        readonly Vector3 outsideScale = new Vector3(2f, 2f, 2f);
        const float attackRange = 5f;
        const float attackAngle = 45f;
        const float teleportCooldown = 15f;
        const float teleportDistance = 10f;
        const float knifeReturnCooldown = 5f;
        const float knifeThrowCooldown = 10f;
        const float knifeThrowMinDistance = 5f;
        const float knifeThrowMaxDistance = 10f;
        const float hitAmountToDropPlayer = 5;
        const int slapDamage = 10;

        public enum State
        {
            Spawning,
            Chasing,
            Abducting
        }

        public override void Start()
        {
            base.Start();
            logger.LogDebug("SCP-4666 Spawned");

            currentBehaviourStateIndex = (int)State.Spawning;

            //SetEnemyOutside(true);

            mainEntrancePosition = RoundManager.FindMainEntrancePosition();
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(false, true);

            if (!IsServer) { return; }

            int num = UnityEngine.Random.Range(3, 6);
            SpawnPresents(num);

            thrownKnifeScript = GameObject.Instantiate(ThrowingKnifePrefab, Vector3.zero, Quaternion.identity).GetComponent<ThrownKnifeScript>();
            thrownKnifeScript.KnifeReturnedEvent.AddListener(KnifeReturned);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-4666 in the scene. Removing this one.");
                if (!IsServer) { return; }
                NetworkObject.Despawn(true);
                return;
            }
            Instance = this;
            logger.LogDebug("Finished spawning SCP-4666");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
                PluginInstance.BlackScreenOverlay.SetActive(false);
            }
        }

        public override void OnDestroy()
        {
            Destroy(thrownKnifeScript?.gameObject);
            base.OnDestroy();
        }

        public void SpawnPresents(int amount)
        {
            System.Random random = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);

            for (int i = 0; i < amount; i++)
            {
                Item giftItem = StartOfRound.Instance.allItemsList.itemsList.Where(x => x.name == "GiftBox").FirstOrDefault();
                Vector3 pos = RoundManager.Instance.GetRandomPositionInRadius(transform.position, 1, 1.5f, random);
                GiftBoxItem gift = GameObject.Instantiate(giftItem.spawnPrefab, pos, Quaternion.identity, RoundManager.Instance.mapPropsContainer.transform).GetComponentInChildren<GiftBoxItem>();
                gift.NetworkObject.Spawn(true);
            }
        }

        public override void Update()
        {
            base.Update();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || !spawnedAndVisible)
            {
                return;
            };

            timeSinceDamagePlayer += Time.deltaTime;
            timeSinceTeleport += Time.deltaTime;
            timeSinceKnifeThrown += Time.deltaTime;
            timeSinceGrabPlayer += Time.deltaTime;

            if (currentBehaviourStateIndex == (int)State.Spawning)
            {
                turnCompass.LookAt(localPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y - 90, 0f)), 10f * Time.deltaTime); // TODO: Test this
            }

            if (localPlayer != inSpecialAnimationWithPlayer && localPlayer.HasLineOfSightToPosition(transform.position))
            {
                localPlayer.IncreaseFearLevelOverTime(0.1f, 0.5f);

                if (!localPlayerHasSeenYuleman)
                {
                    localPlayerHasSeenYuleman = true;
                    AddTargetPlayerServerRpc(localPlayer.actualClientId);
                }
            }

            if (inSpecialAnimationWithPlayer != null && isGrabbingPlayer)
            {
                inSpecialAnimationWithPlayer.transform.position = RightHandTransform.position;
                inSpecialAnimationWithPlayer.takingFallDamage = false;
            }

            if (inSpecialAnimationWithPlayer != null && isPlayerInSack)
            {
                inSpecialAnimationWithPlayer.transform.position = ChildSackTransform.position;
                inSpecialAnimationWithPlayer.takingFallDamage = false;
            }

            if (inSpecialAnimation)
            {
                agent.speed = 0f;
                return;
            }
        }

        public override void DoAIInterval()
        {
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                //logger.LogDebug("Not doing ai interval");
                return;
            };

            if (moveTowardsDestination)
            {
                agent.SetDestination(destination);
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Spawning:
                    agent.speed = 0f;

                    if (!spawnedAndVisible)
                    {
                        if (!InLineOfSight())
                        {
                            spawnedAndVisible = true;
                            BecomeVisibleClientRpc();
                        }
                        return;
                    }

                    break;

                case (int)State.Chasing:
                    agent.speed = 5f;

                    if (!TargetClosestPlayer())
                    {
                        //GetEscapeNode();
                        SwitchToBehaviourClientRpc((int)State.Abducting);
                        return;
                    }

                    // Teleport on cooldown
                    if (CanDoSpecialAction() && timeSinceTeleport > teleportCooldown && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance)
                    {
                        logger.LogDebug("teleporting");
                        timeSinceTeleport = 0f;
                        teleporting = true;
                        TeleportToTargetPlayer();
                        return;
                    }

                    if (!inSpecialAnimation)
                    {
                        // Call knife back on cooldown if it is thrown
                        if (isThrowingKnife && !isCallingKnife && timeSinceKnifeThrown > knifeReturnCooldown) // TODO: Test this
                        {
                            logger.LogDebug("KnifeThrown: " + isThrowingKnife);
                            isCallingKnife = true;
                            DoAnimationClientRpc("call"); // Calls CallKnifeBack() and CheckForKnife()
                            return;
                        }

                        // Throw knife on cooldown
                        if (CanDoSpecialAction() && timeSinceKnifeThrown > knifeThrowCooldown)
                        {
                            //logger.LogDebug("Begin throwing knife");
                            float distance = Vector3.Distance(transform.position, targetPlayer.transform.position);
                            if (distance > knifeThrowMinDistance && distance < knifeThrowMaxDistance)
                            {
                                timeSinceKnifeThrown = 0f;
                                inSpecialAnimation = true;
                                transform.LookAt(targetPlayer.transform.position);
                                ThrowKnifeClientRpc(targetPlayer.actualClientId);
                                return;
                            }
                        }
                    }

                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                case (int)State.Abducting:
                    agent.speed = 4f;

                    if (!isPlayerInSack && TargetClosestPlayer())
                    {
                        SwitchToBehaviourClientRpc((int)State.Chasing);
                        return;
                    }

                    if (isOutside)
                    {
                        //if (daytimeEnemyLeaving) { return; }
                        targetNode = ChooseFarthestNodeFromPosition(mainEntranceOutsidePosition);
                        if (Vector3.Distance(transform.position, targetNode.position) < 1f)
                        {
                            if (isPlayerInSack && inSpecialAnimationWithPlayer != null)
                            {
                                KillPlayerInSackClientRpc();
                            }
                            NetworkObject.Despawn(true);
                            return;
                        }

                        if (!SetDestinationToPosition(targetNode.position, true))
                        {
                            logger.LogWarning("Unable to reach escape node");
                            //GetEscapeNode();
                        }

                        return;
                    }

                    // IsInside
                    if (!teleporting && Vector3.Distance(transform.position, mainEntrancePosition) < 1f)
                    {
                        teleporting = true;
                        Teleport(mainEntranceOutsidePosition, true);
                        return;
                    }

                    if (!teleporting && !SetDestinationToPosition(mainEntrancePosition, true))
                    {
                        teleporting = true;
                        Teleport(mainEntranceOutsidePosition, true);
                        return;
                    }

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        bool CanDoSpecialAction()
        {
            return
            !isThrowingKnife
            && !isCallingKnife
            && !inSpecialAnimation
            && !teleporting;
        }

        /*public GameObject GetFarthestNodeFromPosition()
        {
            throw new NotImplementedException();
        }

        public void GetEscapeNode()
        {
            Vector3 farthestPosition = mainEntranceOutsidePosition;
            float farthestDistance = 0f;
            GameObject[] nodes = GameObject.FindGameObjectsWithTag("OutsideAINode");

            foreach (var node in nodes)
            {
                float distance = Vector3.Distance(node.transform.position, mainEntranceOutsidePosition);
                if (distance <= farthestDistance) { continue; }
                if (!CalculatePath(mainEntranceOutsidePosition, node.transform.position)) { continue; }
                farthestPosition = node.transform.position;
                farthestDistance = distance;
            }

            escapePosition = farthestPosition;
        }*/

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
            if (!GetTeleportNode())
            {
                teleporting = false;
                yield break;
            }

            PlayTeleportSFXClientRpc();
            yield return new WaitForSeconds(2f);

            Vector3 pos = RoundManager.Instance.GetNavMeshPosition(targetNode.position, RoundManager.Instance.navHit);
            agent.Warp(pos);
            PlayLaughSFXClientRpc();
            teleporting = false;
        }

        bool GetTeleportNode()
        {
            targetNode = null;
            GameObject[] aiNodes = targetPlayer.isInsideFactory ? RoundManager.Instance.insideAINodes : RoundManager.Instance.outsideAINodes; // TODO: Test this

            float closestDistance = Vector3.Distance(targetPlayer.transform.position, transform.position);
            //float closestDistance = CalculatePathDistance(RoundManager.Instance.GetNavMeshPosition(transform.position, RoundManager.Instance.navHit), RoundManager.Instance.GetNavMeshPosition(targetPlayer.transform.position, RoundManager.Instance.navHit));

            foreach (var node in aiNodes)
            {
                if (node == null) { continue; }
                if (targetPlayer.HasLineOfSightToPosition(node.transform.position + Vector3.up * 0.25f, 68f, 60, 1f) || targetPlayer.HasLineOfSightToPosition(node.transform.position + Vector3.up * 1.6f, 68f, 60, 1f)) { continue; }

                float distance = Vector3.Distance(node.transform.position, targetPlayer.transform.position);
                //float distance = CalculatePathDistance(RoundManager.Instance.GetNavMeshPosition(node.transform.position, RoundManager.Instance.navHit), RoundManager.Instance.GetNavMeshPosition(targetPlayer.transform.position, RoundManager.Instance.navHit));
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    targetNode = node.transform;
                }
            }

            return targetNode != null;
        }

        bool InLineOfSight()
        {
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (!PlayerIsTargetable(player)) { continue; }
                if (player.HasLineOfSightToPosition(transform.position + Vector3.up * 2)) { return true; }
            }

            return false;
        }

        /*public bool TargetClosestPlayer()
        {
            if (isAngry)
            {
                if (targetPlayer != null && PlayerIsTargetable(targetPlayer))
                {
                    return true;
                }
            }

            float closestDistance = 4000f;
            PlayerControllerB? newPlayerToTarget = null;

            foreach (var player in TargetPlayers)
            {
                if (!PlayerIsTargetable(player))
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

            targetPlayer = newPlayerToTarget;
            return targetPlayer != null;
        }*/

        public new bool TargetClosestPlayer(float bufferDistance = 1.5f, bool requireLineOfSight = false, float viewWidth = 70f)
        {
            mostOptimalDistance = 2000f;
            PlayerControllerB playerControllerB = targetPlayer;
            targetPlayer = null;
            foreach (PlayerControllerB player in TargetPlayers.ToList())
            {
                if (PlayerIsTargetable(player)/* && !PathIsIntersectedByLineOfSight(player.transform.position, calculatePathDistance: false, avoidLineOfSight: false)*/)
                {
                    tempDist = Vector3.Distance(base.transform.position, player.transform.position);
                    if (tempDist < mostOptimalDistance)
                    {
                        mostOptimalDistance = tempDist;
                        targetPlayer = player;
                    }
                }
            }
            if (targetPlayer != null && bufferDistance > 0f && playerControllerB != null && Mathf.Abs(mostOptimalDistance - Vector3.Distance(base.transform.position, playerControllerB.transform.position)) < bufferDistance)
            {
                targetPlayer = playerControllerB;
            }
            return targetPlayer != null;
        }

        #region Overrides
        public override void KillEnemy(bool destroy = false) // Synced
        {
            logger.LogDebug("In KillEnemy()");
            //CancelSpecialAnimationWithPlayer();

            thrownKnifeScript.enabled = false;

            MakeKnifeInvisible();

            if (IsServer && !daytimeEnemyLeaving)
            {
                // Spawn YulemanKnife
                YulemanKnifeBehavior newKnife = GameObject.Instantiate(YulemanKnifePrefab, RightHandTransform.position, Quaternion.identity, StartOfRound.Instance.propsContainer).GetComponentInChildren<YulemanKnifeBehavior>();
                newKnife.NetworkObject.Spawn();

                int knifeValue = UnityEngine.Random.Range(configKnifeMinValue.Value, configKnifeMaxValue.Value + 1);
                SetKnifeValueClientRpc(newKnife.NetworkObject, knifeValue);

                // Spawn ChildSack
                ChildSackBehavior sack = GameObject.Instantiate(ChildSackPrefab, RightHandTransform.position, Quaternion.identity, StartOfRound.Instance.propsContainer).GetComponentInChildren<ChildSackBehavior>();
                sack.NetworkObject.Spawn();

                int sackValue = UnityEngine.Random.Range(configSackMinValue.Value, configSackMaxValue.Value + 1);
                SetSackValueClientRpc(sack.NetworkObject, sackValue);

            }

            base.KillEnemy(destroy);
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1) // Runs on all clients
        {
            logger.LogDebug("In HitEnemy()");
            if (isEnemyDead)
            {
                logger.LogDebug("Yuleman is dead");
                return;
            }

            enemyHP -= force;

            if (enemyHP <= 0)
            {
                KillEnemyOnOwnerClient();
                return;
            }

            if (inSpecialAnimationWithPlayer != null)
            {
                timesHitWhileAbducting++;
                logger.LogDebug($"Yuleman hit {timesHitWhileAbducting} times");
                if (timesHitWhileAbducting >= hitAmountToDropPlayer)
                {
                    timesHitWhileAbducting = 0;
                    logger.LogDebug("Dropping player in sack");
                    CancelSpecialAnimationWithPlayer();

                    inSpecialAnimation = true;
                    creatureAnimator.SetTrigger("roar");
                    SwitchToBehaviourStateOnLocalClient((int)State.Chasing);
                }
            }
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client collided with
        {
            base.OnCollideWithPlayer(other);
            if (isEnemyDead) { return; }
            if (timeSinceDamagePlayer < 3f) { return; }
            if (inSpecialAnimation) { return; }
            PlayerControllerB? player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null || !PlayerIsTargetable(player) || player != localPlayer) { return; }
            if (inSpecialAnimationWithPlayer != null && inSpecialAnimationWithPlayer == player) { return; }

            timeSinceDamagePlayer = 0f;

            if (IsPlayerChild(player) && timeSinceGrabPlayer > 10f && !isPlayerInSack && !isGrabbingPlayer)
            {
                if (!isThrowingKnife && !isCallingKnife && !teleporting)
                {
                    timeSinceGrabPlayer = 0f;
                    inSpecialAnimation = true;
                    player.DropAllHeldItemsAndSync();
                    FreezePlayer(player, true);
                    GrabPlayerServerRpc(player.actualClientId);
                    return;
                }
            }

            if (!isThrowingKnife)
            {
                //DamagePlayerServerRpc(player.actualClientId, "slash");
                DoAnimationServerRpc("slash");
            }
            else
            {
                //DamagePlayerServerRpc(player.actualClientId, "slap");
                DoAnimationServerRpc("slap");
            }
            logger.LogDebug("Finished OnCollideWithPlayer()");
        }


        public override void CancelSpecialAnimationWithPlayer()
        {
            logger.LogDebug("In CancelSpecialAnimationWithPlayer()");

            if (isPlayerInSack)
            {
                inSpecialAnimationWithPlayer.playerCollider.gameObject.SetActive(true);
                inSpecialAnimationWithPlayer.voiceMuffledByEnemy = false;
                MakePlayerInvisible(inSpecialAnimationWithPlayer, false);
                creatureAnimator.SetBool("bagWalk", false);

                if (localPlayer == inSpecialAnimationWithPlayer)
                {
                    PluginInstance.BlackScreenOverlay.SetActive(false);
                }
            }

            if (localPlayer == inSpecialAnimationWithPlayer)
            {
                FreezePlayer(localPlayer, false);
                PluginInstance.AllowPlayerDeathAfterDelay(5f);
            }

            if (inSpecialAnimationWithPlayer != null)
            {
                inSpecialAnimationWithPlayer.transform.SetParent(null);
            }

            isGrabbingPlayer = false;
            isPlayerInSack = false;
            timesHitWhileAbducting = 0;
            timeSinceGrabPlayer = 0f;
            timeSinceDamagePlayer = 0f;
            timeSinceKnifeThrown = 0f;
            timeSinceTeleport = 0f;

            base.CancelSpecialAnimationWithPlayer();
            logger.LogDebug("Finished CancelSpecialAnimationWithPlayer()");
        }

        public bool PlayerIsTargetable(PlayerControllerB playerScript)
        {
            if (playerScript.isPlayerControlled && !playerScript.isPlayerDead && playerScript.inAnimationWithEnemy == null && playerScript.sinkingValue < 0.73f)
            {
                return true;
            }
            return false;
        }

        #endregion

        public void KnifeReturned()
        {
            isCallingKnife = false;
            isThrowingKnife = false;
            timeSinceKnifeThrown = 0f;
            MakeKnifeVisible();
        }

        #region Animation
        // Animation Functions

        public void DoSlashDamageAnimation() // Animation
        {
            PlayerControllerB player = localPlayer;
            if (player == null || !PlayerIsTargetable(player)) { return; }
            if (Vector3.Distance(RightHandTransform.position, player.transform.position) > attackRange) { return; }

            Vector3 directionToPlayer = (player.transform.position - transform.position).normalized;

            float dot = Vector3.Dot(transform.forward, directionToPlayer);
            float angleToPlayer = Mathf.Acos(dot) * Mathf.Rad2Deg;

            if (angleToPlayer <= attackAngle)
            {
                logger.LogDebug("Damaging " + player.playerUsername);
                int deathAnim = UnityEngine.Random.Range(0, 2) == 1 ? 7 : 0;
                player.DamagePlayer(YulemanKnifeBehavior.knifeHitForcePlayer, true, true, CauseOfDeath.Stabbing, deathAnim);
            }
        }

        public void DoSlapDamageAnimation() // Animation
        {
            PlayerControllerB player = localPlayer;
            if (player == null || !PlayerIsTargetable(player)) { return; }
            if (Vector3.Distance(RightHandTransform.position, player.transform.position) > attackRange) { return; }

            Vector3 directionToPlayer = (player.transform.position - transform.position).normalized;

            float dot = Vector3.Dot(transform.forward, directionToPlayer);
            float angleToPlayer = Mathf.Acos(dot) * Mathf.Rad2Deg;

            if (angleToPlayer <= attackAngle)
            {
                logger.LogDebug("Damaging " + player.playerUsername);
                player.DamagePlayer(slapDamage, true, true, CauseOfDeath.Mauling, 0, false, transform.position + transform.forward * 5);
            }
        }

        public void SetInSpecialAnimation() // Animation
        {
            inSpecialAnimation = true;
        }

        public void UnsetInSpecialAnimation() // Animation
        {
            inSpecialAnimation = false;
        }

        public void CallKnifeBack() // Animation
        {
            logger.LogDebug("CallKnifeBack() called");
            inSpecialAnimation = true;
            thrownKnifeScript.CallKnife();
        }

        public void ThrowKnife() // Animation
        {
            logger.LogDebug("ThrowKnife() called");
            if (isThrowingKnife) { return; }

            isThrowingKnife = true;
            MakeKnifeInvisible();

            //inSpecialAnimation = false;

            Vector3 throwDirection = (targetPlayer.bodyParts[5].position - RightHandTransform.position).normalized;
            thrownKnifeScript.ThrowKnife(RightHandTransform, throwDirection);
        }

        public void MakeKnifeVisible() // Animation
        {
            logger.LogDebug("MakeKnifeVisible() called");
            KnifeMesh.SetActive(true);
        }

        public void MakeKnifeInvisible() // Animation
        {
            logger.LogDebug("MakeKnifeInvisible() called");
            KnifeMesh.SetActive(false);
        }

        public void PlayRoarSFX() // Animation
        {
            creatureVoice.PlayOneShot(RoarSFX);
        }

        public void PlayFootstepSFX() // Animation
        {
            creatureSFX.PlayOneShot(FootstepSFX);
        }

        public void GrabPlayer() // Animation
        {
            logger.LogDebug("GrabPlayer() called");
            if (inSpecialAnimationWithPlayer == null) { logger.LogError("inSpecialAnimationWithPlayer is null in GrabPlayer()"); return; }
            inSpecialAnimation = true;
            inSpecialAnimationWithPlayer.transform.SetParent(RightHandTransform);
            isGrabbingPlayer = true;
        }

        public void PutPlayerInSack() // Animation
        {
            logger.LogDebug("PutPlayerInSack() called");
            inSpecialAnimation = false;
            isGrabbingPlayer = false;
            if (inSpecialAnimationWithPlayer == null) { logger.LogError("inSpecialAnimationWithPlayer is null in PutPlayerInSack()"); return; }
            TargetPlayers.Remove(inSpecialAnimationWithPlayer);

            inSpecialAnimationWithPlayer.transform.SetParent(ChildSackTransform);
            isPlayerInSack = true;

            creatureVoice.PlayOneShot(LaughSFX);
            creatureAnimator.SetBool("bagWalk", true);

            if (localPlayer == inSpecialAnimationWithPlayer)
            {
                PluginInstance.BlackScreenOverlay.SetActive(true);
                StartOfRound.Instance.allowLocalPlayerDeath = false;
            }

            MakePlayerInvisible(inSpecialAnimationWithPlayer, true);
            inSpecialAnimationWithPlayer.voiceMuffledByEnemy = true;
            inSpecialAnimationWithPlayer.playerCollider.gameObject.SetActive(false);
            SwitchToBehaviourStateOnLocalClient((int)State.Abducting);
            logger.LogDebug(inSpecialAnimationWithPlayer.playerUsername + " put in yulemans sack");
        }

        public void FinishStartAnimation() // Animation
        {
            logger.LogDebug("In FinishStartAnimation()");
            if (IsServer)
            {
                SwitchToBehaviourClientRpc((int)State.Chasing);
            }

            inSpecialAnimation = false;
        }

        #endregion

        // RPC's

        [ClientRpc]
        public void BecomeVisibleClientRpc()
        {
            spawnedAndVisible = true;
            YulemanMesh.SetActive(true);
        }

        [ClientRpc]
        public void ThrowKnifeClientRpc(ulong clientId)
        {
            targetPlayer = PlayerFromId(clientId);
            creatureAnimator.SetTrigger("throw");
        }

        [ClientRpc]
        public void KillPlayerInSackClientRpc()
        {
            logger.LogDebug("In KillPlayerInSackClientRpc()");
            if (inSpecialAnimationWithPlayer == null) { logger.LogError("inSpecialAnimationWithPlayer is null in KillPlayerInSackClientRpc()"); return; }
            PlayerControllerB player = inSpecialAnimationWithPlayer;
            CancelSpecialAnimationWithPlayer();
            if (localPlayer != player) { return; }
            localPlayer.KillPlayer(Vector3.zero, false);
        }

        [ServerRpc(RequireOwnership = false)]
        public void DoAnimationServerRpc(string animationName)
        {
            if (!IsServer) { return; }
            logger.LogDebug("Doing " + animationName + " animation");
            DoAnimationClientRpc(animationName);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            creatureAnimator.SetTrigger(animationName);
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
            if (!IsServer) { return; }
            inSpecialAnimation = true;
            //targetPlayer = PlayerFromId(clientId);
            //inSpecialAnimationWithPlayer = targetPlayer;
            inSpecialAnimationWithPlayer = PlayerFromId(clientId);
            GrabPlayerClientRpc(clientId);
        }

        [ClientRpc]
        public void GrabPlayerClientRpc(ulong clientId)
        {
            inSpecialAnimation = true;
            //targetPlayer = PlayerFromId(clientId);
            //inSpecialAnimationWithPlayer = targetPlayer;
            inSpecialAnimationWithPlayer = PlayerFromId(clientId);
            inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
            inSpecialAnimationWithPlayer.snapToServerPosition = true;
            inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
            creatureAnimator.SetTrigger("pickup");
        }

        [ServerRpc(RequireOwnership = false)]
        public void AddTargetPlayerServerRpc(ulong clientId)
        {
            if (!IsServer) { return; }

            PlayerControllerB player = PlayerFromId(clientId);

            if (currentBehaviourStateIndex == (int)State.Spawning)
            {
                DoAnimationClientRpc("start");
            }

            if (TargetPlayers.Contains(player)) { return; }
            TargetPlayers.Add(player);

            if (currentBehaviourStateIndex == (int)State.Abducting) // TODO: Test this
            {
                SwitchToBehaviourClientRpc((int)State.Chasing);
            }

            logger.LogDebug($"Added {player.playerUsername} to targeted players");
        }

        [ClientRpc]
        public void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
            transform.localScale = value ? outsideScale : insideScale;
        }

        [ClientRpc]
        public void SetKnifeValueClientRpc(NetworkObjectReference netRef, int value)
        {
            if (!netRef.TryGet(out NetworkObject netobj)) { return; }
            if (!netobj.TryGetComponent(out YulemanKnifeBehavior knife)) { return; }
            knife.SetScrapValue(value);
            knife.FallToGround();
        }

        [ClientRpc]
        public void SetSackValueClientRpc(NetworkObjectReference netRef, int value)
        {
            if (!netRef.TryGet(out NetworkObject netobj)) { return; }
            if (!netobj.TryGetComponent(out ChildSackBehavior sack)) { return; }
            sack.SetScrapValue(value);
            sack.FallToGround();
        }
    }
}