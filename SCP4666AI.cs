using BepInEx.Logging;
using GameNetcodeStuff;
using SCP4666.Doll;
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
using static UnityEngine.LightAnchor;

namespace SCP4666
{
    internal class SCP4666AI : EnemyAI // TODO: Rework and add new stuff
    {
        private static ManualLogSource logger = LoggerInstance;

        // DEBUG STUFF

        bool DEBUG_SpawnDolls = false;
        bool DEBUG_GroundSlam = false;
        bool DEBUG_ThrowKnife = false;
        bool DEBUG_Teleport = false;
        bool DEBUG_TargetHost = true;

        public static SCP4666AI? Instance { get; private set; }

#pragma warning disable CS8618
        public Transform RightHandTransform;
        public Transform ChildSackTransform;

        public AudioClip FootstepSFX;
        public AudioClip TeleportSFX;
        public AudioClip LaughSFX;
        public AudioClip RoarSFX;
        public AudioClip GroundSlamSFX;

        public GameObject YulemanMesh;

        public Transform turnCompass;

        public GameObject ThrowingKnifePrefab;

        public GameObject ChildSackPrefab;
        public GameObject YulemanKnifePrefab;
        public GameObject EvilFleshDollPrefab;
        public GameObject FleshDollPrefab;

        public Collider collider;

        public GameObject KnifeMesh;

        public ParticleSystem GroundSlamParticles;

        public ThrownKnifeScript thrownKnifeScript;
#pragma warning restore CS8618

        Vector3 mainEntranceOutsidePosition;
        Vector3 mainEntranceInsidePosition;

        List<PlayerControllerB> TargetPlayers = [];
        bool localPlayerHasSeenYuleman;
        bool spawnedAndVisible;

        bool isGrabbingPlayer;
        bool isPlayerInSack;

        float timeSinceDamagePlayer;
        float timeSinceTeleport;
        float timeSinceKnifeThrown;
        float timeSinceGrabPlayer;
        float timeSinceGroundSlam;
        float timeSinceDollSpawning;

        bool teleporting;

        bool isThrowingKnife;
        bool isCallingKnife;

        int timesHitWhileAbducting;

        int timesHitWithoutDamagingPlayers;

        int dollsToSpawn;
        bool useBombDolls;

        public bool isInsideFactory => !isOutside;

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
        const int minPresentsToSpawn = 3;
        const int maxPresentsToSpawn = 6;

        const int minDollsToDrop = 1;
        const int maxDollsToDrop = 3;
        const int minDollsToSpawn = 2;
        const int maxDollsToSpawn = 5;
        const float dollSpawningCooldown = 60f;

        const float groundSlamCooldown = 15f;
        const int maxHitsToGroundSlam = 3;
        const float groundSlamDistance = 5f;
        const float groundSlamForce = 50f;
        const int groundSlamDamage = 30;

        const int maxHp = 28;

        public enum State
        {
            Spawning,
            Chasing,
            Abducting
        }

        public void DEBUG_DoGroundSlam()
        {
            logger.LogDebug("Performing ground slam");
            inSpecialAnimation = true;
            DoAnimationClientRpc("groundSlam");
        }

        public override void Start()
        {
            base.Start();
            logger.LogDebug("SCP-4666 Spawned");

            currentBehaviourStateIndex = (int)State.Spawning;

            //SetEnemyOutside(true);

            mainEntranceInsidePosition = RoundManager.FindMainEntrancePosition();
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(false, true);

            if (!IsServer) { return; }

            int num = UnityEngine.Random.Range(minPresentsToSpawn, maxPresentsToSpawn);
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

        public void CustomEnemyAIUpdate()
        {
            if (inSpecialAnimation)
            {
                agent.speed = 0f;
                return;
            }

            if (updateDestinationInterval >= 0f)
            {
                updateDestinationInterval -= Time.deltaTime;
            }
            else
            {
                DoAIInterval();
                updateDestinationInterval = AIIntervalTime + UnityEngine.Random.Range(-0.015f, 0.015f);
            }
        }

        public override void Update()
        {
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            }

            CustomEnemyAIUpdate();

            if (!spawnedAndVisible) { return; }

            timeSinceDamagePlayer += Time.deltaTime;
            timeSinceTeleport += Time.deltaTime;
            timeSinceKnifeThrown += Time.deltaTime;
            timeSinceGrabPlayer += Time.deltaTime;
            timeSinceGroundSlam += Time.deltaTime;
            timeSinceDollSpawning += Time.deltaTime;

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
        }

        public override void DoAIInterval()
        {
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
                        SwitchToBehaviourClientRpc((int)State.Abducting);
                        return;
                    }

                    // Teleport on cooldown
                    if (CanDoSpecialAction() && timeSinceTeleport > teleportCooldown && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance
                        && (Utils.isBeta && DEBUG_Teleport))
                    {
                        logger.LogDebug("Teleporting");
                        timeSinceTeleport = 0f;
                        teleporting = true;
                        TeleportToTargetPlayer();
                        return;
                    }

                    if (CanDoSpecialAction() && timeSinceDollSpawning > dollSpawningCooldown
                        && (Utils.isBeta && DEBUG_SpawnDolls))
                    {
                        logger.LogDebug("Spawning dolls");
                        timeSinceDollSpawning = 0f;
                        dollsToSpawn = UnityEngine.Random.Range(minDollsToSpawn, maxDollsToSpawn + 1);
                        inSpecialAnimation = true;
                        DoAnimationClientRpc("spawnDoll");
                        return;
                    }

                    // Call knife back on cooldown if it is thrown
                    if (isThrowingKnife && !isCallingKnife && timeSinceKnifeThrown > knifeReturnCooldown) // TODO: Test this
                    {
                        logger.LogDebug("KnifeThrown: " + isThrowingKnife);
                        isCallingKnife = true;
                        DoAnimationClientRpc("call"); // Calls CallKnifeBack()
                        return;
                    }

                    // Throw knife on cooldown
                    if (CanDoSpecialAction() && timeSinceKnifeThrown > knifeThrowCooldown
                        && (Utils.isBeta && DEBUG_ThrowKnife))
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

                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                case (int)State.Abducting:
                    agent.speed = 4f;

                    if (!isPlayerInSack && TargetClosestPlayer())
                    {
                        SwitchToBehaviourClientRpc((int)State.Chasing);
                        return;
                    }

                    if (isOutside) // outside
                    {
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

                        SetDestinationToPosition(targetNode.position);
                    }
                    else // inside
                    {
                        SetDestinationToEntrance();
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

        public void Teleport(Vector3 position, bool outside)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            agent.Warp(position);
            transform.position = position;
            SetEnemyOutsideClientRpc(outside);
            teleporting = false;
        }

        public void TeleportToTargetPlayer()
        {
            IEnumerator TeleportCoroutine()
            {
                PlayTeleportSFXClientRpc();
                yield return new WaitForSeconds(2f);

                Teleport(targetNode.position, !targetPlayer.isInsideFactory);
                PlayLaughSFXClientRpc();
                teleporting = false;
            }

            if (!GetTeleportNode())
            {
                teleporting = false;
                return;
            }

            StartCoroutine(TeleportCoroutine());
        }

        void SetDestinationToEntrance()
        {
            if (agent == null || agent.enabled == false) { return; }
            if (isInsideFactory)
            {
                SetDestinationToPosition(mainEntranceInsidePosition);

                if (Vector3.Distance(transform.position, mainEntranceInsidePosition) < 1f)
                {
                    Teleport(mainEntranceOutsidePosition, true);
                }
            }
            else
            {
                SetDestinationToPosition(mainEntranceOutsidePosition);

                if (Vector3.Distance(transform.position, mainEntranceOutsidePosition) < 1f)
                {
                    Teleport(mainEntranceInsidePosition, false);
                }
            }
        }

        bool GetTeleportNode()
        {
            targetNode = null;
            GameObject[] aiNodes = targetPlayer.isInsideFactory ? Utils.insideAINodes : Utils.outsideAINodes; // TODO: Test this

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

        public new bool TargetClosestPlayer(float bufferDistance = 1.5f, bool requireLineOfSight = false, float viewWidth = 70f)
        {
            mostOptimalDistance = 2000f;
            PlayerControllerB playerControllerB = targetPlayer;
            targetPlayer = null;
            foreach (PlayerControllerB player in TargetPlayers.ToList())
            {
                if (Utils.isBeta && !DEBUG_TargetHost && player.isHostPlayerObject) { continue; }
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

        public bool PlayerIsTargetable(PlayerControllerB playerScript)
        {
            if (playerScript.isPlayerControlled && !playerScript.isPlayerDead && playerScript.inAnimationWithEnemy == null && playerScript.sinkingValue < 0.73f)
            {
                return true;
            }
            return false;
        }

        public void KnifeReturned()
        {
            isCallingKnife = false;
            isThrowingKnife = false;
            timeSinceKnifeThrown = 0f;
            MakeKnifeVisible();
        }

        #region Overrides
        public override void KillEnemy(bool destroy = false) // Synced
        {
            logger.LogDebug("In KillEnemy()");
            //CancelSpecialAnimationWithPlayer();

            thrownKnifeScript.enabled = false;

            MakeKnifeInvisible();

            if (IsServer)
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

                int dollsToDrop = UnityEngine.Random.Range(minDollsToDrop, maxDollsToDrop + 1);
                for (int i = 0; i < dollsToDrop; i++)
                {
                    FleshDollBehavior doll = GameObject.Instantiate(FleshDollPrefab, transform.position, Quaternion.identity, StartOfRound.Instance.propsContainer).GetComponent<FleshDollBehavior>();
                    doll.NetworkObject.Spawn();
                }
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

            timesHitWithoutDamagingPlayers += 1;

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
                    return;
                }
            }

            if (!IsServer) { return; } // TODO: Test this

            if (enemyHP <= maxHp / 2)
            {
                useBombDolls = true;
            }

            if (timesHitWithoutDamagingPlayers >= maxHitsToGroundSlam && timeSinceGroundSlam > groundSlamCooldown
                && (Utils.isBeta && DEBUG_GroundSlam))
            {
                logger.LogDebug("Performing ground slam");
                timesHitWithoutDamagingPlayers = 0;
                timeSinceGroundSlam = 0f;
                inSpecialAnimation = true;
                DoAnimationClientRpc("groundSlam");
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
                DoAnimationServerRpc("slash");
            }
            else
            {
                bool sackSlap = UnityEngine.Random.Range(0, 2) == 0;
                DoAnimationServerRpc(sackSlap ? "sackSlap" : "slap");
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
            }

            if (inSpecialAnimationWithPlayer != null && localPlayer == inSpecialAnimationWithPlayer)
            {
                PluginInstance.BlackScreenOverlay.SetActive(false);
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
            timeSinceDollSpawning = 0f;
            timeSinceGroundSlam = 0f;

            base.CancelSpecialAnimationWithPlayer();
            logger.LogDebug("Finished CancelSpecialAnimationWithPlayer()");
        }

        #endregion

        #region Animation
        // Animation Functions

        public void SpawnDoll() // Animation
        {
            if (!IsServer) { return; }

            if (dollsToSpawn <= 0)
            {
                DoAnimationClientRpc("reset");
                inSpecialAnimation = false;
                return;
            }

            logger.LogDebug("DollsToSpawn: " +  dollsToSpawn);
            dollsToSpawn -= 1;
            EvilFleshDollAI doll = GameObject.Instantiate(EvilFleshDollPrefab, RightHandTransform.position, transform.rotation).GetComponent<EvilFleshDollAI>(); ;
            doll.NetworkObject.Spawn(true);

            if (useBombDolls)
            {
                doll.SetBombDollClientRpc();
            }

            DoAnimationClientRpc("spawnDoll");
        }

        public void FinishGroundSlamAnimation() // Animation
        {
            logger.LogDebug("FinishGroundSlamAnimation");

            GroundSlamParticles.Play();
            creatureSFX.PlayOneShot(GroundSlamSFX);

            PlayerControllerB[] players = Utils.GetNearbyPlayers(transform.position, groundSlamDistance);

            foreach (PlayerControllerB player in players)
            {
                if (!PlayerIsTargetable(player)) { continue; }

                Vector3 direction = (player.transform.position - transform.position).normalized;
                Vector3 upDirection = transform.TransformDirection(Vector3.up).normalized;
                direction = (direction + upDirection).normalized;
                LaunchPlayer(player, direction * groundSlamForce);

                // Damage player
                if (localPlayer == player)
                {
                    IEnumerator InjureLocalPlayerCoroutine()
                    {
                        yield return new WaitUntil(() => localPlayer.thisController.isGrounded || localPlayer.isInHangarShipRoom);
                        if (localPlayer.isPlayerDead) { yield break; }
                        localPlayer.DamagePlayer(groundSlamDamage);
                        localPlayer.sprintMeter /= 2;
                        localPlayer.JumpToFearLevel(0.8f);
                        localPlayer.drunkness = 0.2f;
                    }
                    StartCoroutine(InjureLocalPlayerCoroutine());
                }
            }
        }

        void LaunchPlayer(PlayerControllerB player, Vector3 direction)
        {
            player.playerRigidbody.isKinematic = false;
            player.playerRigidbody.velocity = Vector3.zero;
            player.externalForceAutoFade += direction;
            player.playerRigidbody.isKinematic = true;
        }

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
            inSpecialAnimationWithPlayer = PlayerFromId(clientId);
            GrabPlayerClientRpc(clientId);
        }

        [ClientRpc]
        public void GrabPlayerClientRpc(ulong clientId)
        {
            inSpecialAnimation = true;
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