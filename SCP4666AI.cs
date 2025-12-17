using Dawn;
using Dawn.Utils;
using Dusk;
using GameNetcodeStuff;
using SCP4666.Doll;
using SCP4666.YulemanKnife;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using static SCP4666.Plugin;
using static SCP4666.Utils;

namespace SCP4666
{
    internal partial class SCP4666AI : EnemyAI // TODO: Rework and add new stuff
    {
        // DEBUG STUFF

        bool DEBUG_SpawnDolls = true;
        bool DEBUG_GroundSlam = true;
        bool DEBUG_ThrowKnife = true;
        bool DEBUG_Teleport = true;
        bool DEBUG_TargetHost = true;

        //public static SCP4666AI? Instance { get; private set; }
        public static List<SCP4666AI> Instances { get; private set; } = [];

#pragma warning disable CS8618
        public Transform RightHandTransform;
        public Transform ChildSackTransform;

        public AudioSource MusicSource;

        public AudioClip FootstepSFX;
        public AudioClip TeleportSFX;
        public AudioClip LaughSFX;
        public AudioClip RoarSFX;
        public AudioClip GroundSlamSFX;

        public GameObject YulemanMesh;

        public Transform turnCompass;

        public GameObject evilDollPrefab;
        public GameObject ThrowingKnifePrefab;

        public Collider collider;

        public GameObject KnifeMesh;

        public ParticleSystem GroundSlamParticles;

        public ThrownKnifeScript thrownKnifeScript;

        public SmartAgentNavigator nav;

        public GameObject DEBUG_hudOverlay;
        public Camera cameraSack;
#pragma warning restore CS8618

        public static UnityEvent<PlayerControllerB> onPlayerGrabbed = new UnityEvent<PlayerControllerB>();

        public new bool isOutside => nav.IsAgentOutside();

        Vector3 mainEntranceOutsidePosition;
        List<EntranceTeleport> entrances = [];

        List<PlayerControllerB> targetPlayers = [];

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

        int damageTakenWithoutDamaging;

        int dollsToSpawn;
        bool useBombDolls;
        float timeSinceSwitchBehavior;
        private Vector3 lastPosition;
        private float currentSpeed;
        private float idleTime;

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
        const int maxDamageTakenToGroundSlam = 3;
        const float groundSlamDistance = 5f;
        const float groundSlamForce = 50f;
        const int groundSlamDamage = 30;

        const int maxHp = 30;

        const float spawnTurnCompassSpeed = 20f;
        const float LOSOffset = 2f;
        const float grabPlayerCooldown = 10f;
        const bool playBossMusic = false;

        const float damagePlayerCooldown = 2f;

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

            Instances.Add(this);

            enemyHP = maxHp;
            currentBehaviourStateIndex = (int)State.Spawning;

            nav.SetAllValues(isOutside: true);

            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(false, true);
            entrances = GameObject.FindObjectsOfType<EntranceTeleport>(includeInactive: false).ToList();

            // spawn throwing knife
            thrownKnifeScript = GameObject.Instantiate(ThrowingKnifePrefab, Vector3.zero, Quaternion.identity).GetComponent<ThrownKnifeScript>();
            thrownKnifeScript.KnifeReturnedEvent.AddListener(KnifeReturned);

            cameraSack.cullingMask = localPlayer.gameplayCamera.cullingMask;

            if (!IsServer) { return; }

            if (isBeta)
                Instantiate(DEBUG_hudOverlay, Vector3.zero, Quaternion.identity);

            // spawn presents
            int num = UnityEngine.Random.Range(minPresentsToSpawn, maxPresentsToSpawn);
            SpawnPresents(num);
        }

        public override void OnDestroy()
        {
            PlayerControllerB? playerInSack = inSpecialAnimationWithPlayer;
            CancelSpecialAnimationWithPlayer();
            if (playerInSack != null && localPlayer == playerInSack)
            {
                StartOfRound.Instance.allowLocalPlayerDeath = true;
                playerInSack.KillPlayer(Vector3.zero, spawnBody: false);
            }
            Destroy(thrownKnifeScript?.gameObject);
            Instances.Remove(this);
            base.OnDestroy();
        }

        public void SpawnPresents(int amount)
        {
            System.Random random = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);

            for (int i = 0; i < amount; i++)
            {
                Item giftItem = LethalContent.Items[ItemKeys.Gift].Item;
                Vector3 pos = RoundManager.Instance.GetRandomPositionInRadius(transform.position, 1, 1.5f, random);
                GiftBoxItem gift = GameObject.Instantiate(giftItem.spawnPrefab, pos, Quaternion.identity, RoundManager.Instance.mapPropsContainer.transform).GetComponentInChildren<GiftBoxItem>();
                gift.NetworkObject.Spawn(destroyWithScene: true);
            }
        }

        public void CustomEnemyAIUpdate()
        {
            if (!IsServer) { return; }

            if (inSpecialAnimation)
            {
                nav.StopAgent();
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
            timeSinceSwitchBehavior += Time.deltaTime;

            if (currentBehaviourStateIndex == (int)State.Spawning)
            {
                turnCompass.LookAt(localPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y - 90, 0f)), spawnTurnCompassSpeed * Time.deltaTime);
            }

            if (localPlayer != inSpecialAnimationWithPlayer && localPlayer.HasLineOfSightToPosition(transform.position + Vector3.up * LOSOffset))
            {
                localPlayer.IncreaseFearLevelOverTime(0.1f, 0.5f);

                if (!localPlayerHasSeenYuleman)
                {
                    localPlayerHasSeenYuleman = true;
                    AddTargetPlayerServerRpc(localPlayer.actualClientId);
                }
            }

            if (targetPlayers.Contains(localPlayer) && playBossMusic)
            {
                if (!MusicSource.isPlaying)
                    MusicSource.Play();
            }
            else
            {
                if (MusicSource.isPlaying)
                    MusicSource.Stop();
            }
        }

        public void LateUpdate()
        {
            if (inSpecialAnimationWithPlayer != null)
            {
                if (isGrabbingPlayer)
                {
                    inSpecialAnimationWithPlayer.transform.position = RightHandTransform.position;
                    inSpecialAnimationWithPlayer.takingFallDamage = false;
                }
                if (isPlayerInSack)
                {
                    inSpecialAnimationWithPlayer.transform.position = ChildSackTransform.position;
                    inSpecialAnimationWithPlayer.takingFallDamage = false;
                }
            }

            currentSpeed = ((transform.position - lastPosition).magnitude / Time.deltaTime) / 2;
            idleTime = currentSpeed <= 0f ? idleTime + Time.deltaTime : 0f;
        }

        public override void DoAIInterval()
        {
            UpdateTestingHUD();

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Spawning:
                    agent.speed = 0f;

                    if (!spawnedAndVisible && !InLineOfSight())
                    {
                        spawnedAndVisible = true;
                        BecomeVisibleClientRpc();
                    }

                    break;

                case (int)State.Chasing:
                    agent.speed = 5f;

                    if (!TargetClosestPlayer())
                    {
                        SwitchToBehaviourClientRpc((int)State.Abducting);
                        return;
                    }
                    // TODO: Set up configs for disabling certain attacks/mechanics
                    // Teleport on cooldown
                    if (CanDoSpecialAction() && timeSinceTeleport > teleportCooldown && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance
                        && (!Utils.isBeta || DEBUG_Teleport))
                    {
                        GameObject? teleportNode = GetClosestNodeBehindPlayer(targetPlayer, 1f);
                        if (teleportNode != null)
                        {
                            LongTeleport(teleportNode.transform.position, targetPlayer.isInsideFactory);
                            return;
                        }
                    }

                    // Spawn dolls on cooldown
                    if (CanDoSpecialAction() && timeSinceDollSpawning > dollSpawningCooldown
                        && (!Utils.isBeta || DEBUG_SpawnDolls))
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
                        && (!Utils.isBeta || DEBUG_ThrowKnife))
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

                    if (targetPlayer == null || inTestRoom) { return; }
                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                case (int)State.Abducting:
                    agent.speed = 4f;

                    if (!isPlayerInSack && TargetClosestPlayer())
                    {
                        SwitchToBehaviourClientRpc((int)State.Chasing);
                        return;
                    }

                    if (Vector3.Distance(transform.position, targetNode.position) < 1f)
                    {
                        /*if (isPlayerInSack && inSpecialAnimationWithPlayer != null)
                        {
                            KillPlayerInSackClientRpc();
                        }*/
                        RoundManager.Instance.SpawnedEnemies.Remove(this);
                        NetworkObject.Despawn(true);
                        return;
                    }
                    
                    if (!SetDestinationToPosition(targetNode.position))
                    {
                        if (isInsideFactory && !teleporting)
                        {
                            LongTeleport(mainEntranceOutsidePosition, true);
                            return;
                        }
                        return;
                    }

                    if (idleTime > 2f && isOutside)
                    {
                        GameObject? teleportNode = outsideAINodes.GetClosestToPosition(transform.position, x => x.transform.position);
                        if (teleportNode == null) { return; }
                        LongTeleport(teleportNode.transform.position, true);
                        return;
                    }

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        public void SpectateYuleman(bool value)
        {
            cameraSack.enabled = value;
            StartOfRound.Instance.SwitchCamera(value ? cameraSack : localPlayer.gameplayCamera);
            logger.LogDebug("Spectate Yuleman: " + value);
        }

        bool CanKidnapPlayer(PlayerControllerB player)
        {
            if (timeSinceGrabPlayer < grabPlayerCooldown) { return false; }
            if (!CanDoSpecialAction()) { return false; }
            if (isPlayerInSack) { return false; }
            if (IsPlayerChild(player)) { return true; }
            if (UnityEngine.Random.Range(0, enemyHP) == 0) { return true; }
            return false;
        }

        void UpdateTestingHUD()
        {
            if (isBeta && TestingHUDOverlay.Instance != null) // TestingHUD
            {
                TestingHUDOverlay.Instance.label1.text = ((State)currentBehaviourStateIndex).ToString();

                TestingHUDOverlay.Instance.label2.text = "TargetPlayer: " + targetPlayer?.playerUsername;

                TestingHUDOverlay.Instance.label3.text = "";

                TestingHUDOverlay.Instance.toggle1.isOn = isOutside;
                TestingHUDOverlay.Instance.toggle1Label.text = "isOutside";

                TestingHUDOverlay.Instance.toggle2.isOn = inSpecialAnimation;
                TestingHUDOverlay.Instance.toggle2Label.text = "inSpecialAnimation";
            }
        }

        bool CanDoSpecialAction()
        {
            return
            !isThrowingKnife
            && !isCallingKnife
            && !inSpecialAnimation
            && !teleporting
            && !isGrabbingPlayer; // TODO: Test isGrabbingPlayer make sure it doesnt break anything
        }

        public void Teleport(Vector3 position, bool outside)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            agent.Warp(position);
            transform.position = position;
            nav.SetAllValues(outside);
            teleporting = false;
        }

        public void LongTeleport(Vector3 position, bool outside)
        {
            logger.LogDebug("Teleporting...");
            teleporting = true;
            timeSinceTeleport = 0f;

            IEnumerator TeleportCoroutine(Vector3 position, bool outside)
            {
                PlayTeleportSFXClientRpc();
                yield return new WaitForSeconds(2f);

                Teleport(position, outside);
                PlayLaughSFXClientRpc();
                teleporting = false;
            }

            StartCoroutine(TeleportCoroutine(position, outside));
        }

        public bool SetDestinationToPosition(Vector3 position)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position);
            if (!SmartCanPathToPoint(position)) { return false; }
            return nav.DoPathingToDestination(position);
        }

        public bool SmartCanPathToPoint(Vector3 position)
        {
            Vector3 enemyPos = RoundManager.Instance.GetNavMeshPosition(transform.position);
            position = RoundManager.Instance.GetNavMeshPosition(position);

            if (nav.CanPathToPoint(enemyPos, position) > 0)
                return true;

            foreach (var entrance in entrances)
            {
                bool relevantEntrance = isInsideFactory ? !entrance.isEntranceToBuilding : entrance.isEntranceToBuilding;
                if (!relevantEntrance)
                    continue;

                Vector3 teleportFrom = RoundManager.Instance.GetNavMeshPosition(entrance.entrancePoint.position);

                if (entrance.exitPoint == null && !entrance.FindExitPoint())
                    continue;

                Vector3 teleportTo = RoundManager.Instance.GetNavMeshPosition(entrance.exitPoint!.position);

                if (nav.CanPathToPoint(enemyPos, teleportFrom) > 0 && nav.CanPathToPoint(teleportTo, position) > 0)
                    return true;
            }

            return false;
        }

        GameObject? GetClosestNodeBehindPlayer(PlayerControllerB player, float minDistance)
        {
            GameObject? closestNode = null;
            GameObject[] nodes = player.isInsideFactory ? Utils.insideAINodes : Utils.outsideAINodes;

            float closestDistance = Vector3.Distance(player.transform.position, transform.position);

            foreach (var node in nodes)
            {
                if (node == null) { continue; }
                Vector3 lookDir = player.gameplayCamera.transform.forward;
                Vector3 toNode = (node.transform.position - player.gameplayCamera.transform.position).normalized;
                if (Vector3.Dot(lookDir, toNode) > 0.6f) // Is player looking in direction of node
                    continue;

                float distance = Vector3.Distance(node.transform.position, player.transform.position);

                if (distance < minDistance) { continue; }
                
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestNode = node;
                }
            }

            return closestNode;
        }

        bool InLineOfSight()
        {
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (!PlayerIsTargetable(player)) { continue; }
                if (player.HasLineOfSightToPosition(transform.position + Vector3.up * LOSOffset)) { return true; }
            }

            return false;
        }

        public new bool TargetClosestPlayer(float bufferDistance = 1.5f, bool requireLineOfSight = false, float viewWidth = 70f)
        {
            mostOptimalDistance = Mathf.Infinity;
            PlayerControllerB playerControllerB = targetPlayer;
            targetPlayer = null;
            foreach (PlayerControllerB player in targetPlayers.ToList())
            {
                if (Utils.isBeta && !DEBUG_TargetHost && player.isHostPlayerObject) { continue; }
                if (PlayerIsTargetable(player))
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

        public bool PlayerIsTargetable(PlayerControllerB playerScript) => playerScript.isPlayerControlled && !playerScript.isPlayerDead && playerScript.inAnimationWithEnemy == null && playerScript.sinkingValue < 0.73f;

        void LaunchPlayer(PlayerControllerB player, Vector3 direction)
        {
            player.playerRigidbody.isKinematic = false;
            player.playerRigidbody.velocity = Vector3.zero;
            player.externalForceAutoFade += direction;
            player.playerRigidbody.isKinematic = true;
        }

        public void KnifeReturned() // Listener
        {
            isCallingKnife = false;
            isThrowingKnife = false;
            timeSinceKnifeThrown = 0f;
            MakeKnifeVisible();
        }

        #region Overrides
        public override void KillEnemy(bool destroy = false) // Synced
        {
            logger.LogDebug("KillEnemy()");
            CancelSpecialAnimationWithPlayer();

            thrownKnifeScript.enabled = false;

            MakeKnifeInvisible();
            targetPlayers.Clear();
            MusicSource.Stop();

            if (IsServer)
            {
                SpawnItem(SCP4666Keys.YulemanKnife, transform.position);
                SpawnItem(SCP4666Keys.ChildSack, transform.position);

                // Spawn Dolls
                int dollsToDrop = UnityEngine.Random.Range(minDollsToDrop, maxDollsToDrop + 1);
                for (int i = 0; i < dollsToDrop; i++)
                {
                    SpawnItem(SCP4666Keys.FleshDoll, transform.position);
                }
            }

            base.KillEnemy(destroy);
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1) // Synced
        {
            logger.LogDebug("HitEnemy()");
            if (isEnemyDead)
                return;

            enemyHP -= force;
            logger.LogDebug("hp: " + enemyHP);

            if (enemyHP <= 0)
            {
                logger.LogDebug("Attempt killing yuleman on server");
                KillEnemyOnOwnerClient();
                return;
            }

            damageTakenWithoutDamaging += force;

            if (inSpecialAnimationWithPlayer != null)
            {
                timesHitWhileAbducting++;
                logger.LogDebug($"Yuleman hit {timesHitWhileAbducting} times while abducting");
                if (timesHitWhileAbducting >= hitAmountToDropPlayer)
                {
                    timesHitWhileAbducting = 0;
                    CancelSpecialAnimationWithPlayer();

                    inSpecialAnimation = true;
                    creatureAnimator.SetTrigger("roar");
                    SwitchToBehaviourStateOnLocalClient((int)State.Chasing);
                    return;
                }
            }

            if (!IsServer) { return; }

            if (enemyHP <= maxHp / 2)
            {
                useBombDolls = true;
            }

            if (damageTakenWithoutDamaging >= maxDamageTakenToGroundSlam && timeSinceGroundSlam > groundSlamCooldown
                && (!Utils.isBeta || DEBUG_GroundSlam))
            {
                logger.LogDebug("Performing ground slam");
                damageTakenWithoutDamaging = 0;
                timeSinceGroundSlam = 0f;
                inSpecialAnimation = true;
                DoAnimationClientRpc("groundSlam");
            }
        }

        public override void OnCollideWithPlayer(Collider other) // Synced
        {
            base.OnCollideWithPlayer(other);
            if (isEnemyDead) { return; }
            if (timeSinceDamagePlayer < damagePlayerCooldown) { return; }
            if (inSpecialAnimation) { return; }
            PlayerControllerB? player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null || !PlayerIsTargetable(player) || player != localPlayer) { return; }
            if (inSpecialAnimationWithPlayer != null && inSpecialAnimationWithPlayer == player) { return; }

            timeSinceDamagePlayer = 0f;

            if (CanKidnapPlayer(player))
            {
                timeSinceGrabPlayer = 0f;
                inSpecialAnimation = true;
                player.DropAllHeldItemsAndSync();
                FreezePlayer(player, true);
                GrabPlayerServerRpc(player.actualClientId);
                return;
            }

            if (!isThrowingKnife)
            {
                DoAnimationServerRpc("slash");
            }
            else
            {
                DoAnimationServerRpc(UnityEngine.Random.Range(0, 2) == 0 ? "sackSlap" : "slap");
            }
        }

        public override void CancelSpecialAnimationWithPlayer()
        {
            logger.LogDebug("CancelSpecialAnimationWithPlayer()");

            if (isPlayerInSack)
            {
                inSpecialAnimationWithPlayer.playerCollider.gameObject.SetActive(true);
                MufflePlayer(inSpecialAnimationWithPlayer, false);
                MakePlayerInvisible(inSpecialAnimationWithPlayer, false);
            }

            if (inSpecialAnimationWithPlayer != null)
            {
                if (localPlayer == inSpecialAnimationWithPlayer)
                {
                    SpectateYuleman(false);
                    //NetworkHandlerSCP4666.Instance?.blackScreenOverlay?.SetActive(false);
                    FreezePlayer(localPlayer, false);
                    //Instance.AllowPlayerDeathAfterDelay(5f);
                    StartOfRound.Instance.allowLocalPlayerDeath = true;
                }
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
        }

        #endregion

        public new void SetEnemyOutside(bool outside) // Call in SmartNavAgent in unity editor TODO
        {
            transform.localScale = outside ? outsideScale : insideScale;
        }

        public new void SwitchToBehaviourStateOnLocalClient(int stateIndex)
        {
            if (currentBehaviourStateIndex == stateIndex) { return; }

            logger.LogDebug("Switching behavior to " + (State)stateIndex);
            timeSinceSwitchBehavior = 0f;
            previousBehaviourStateIndex = currentBehaviourStateIndex;
            currentBehaviourStateIndex = stateIndex;
            currentBehaviourState = enemyBehaviourStates[stateIndex];
            PlayAudioOfCurrentState();
            PlayAnimationOfCurrentState();
            BehaviourSwitchCleanUp();

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Chasing:
                    creatureAnimator.SetBool("bagWalk", false);
                    break;
                case (int)State.Abducting:
                    if (!IsServer) { return; }
                    targetNode = Utils.outsideAINodes.GetFarthestFromPosition(mainEntranceOutsidePosition, x => x.transform.position)?.transform;
                    creatureAnimator.SetBool("bagWalk", true);
                    break;
                default:
                    break;
            }
        }

        public void BehaviourSwitchCleanUp()
        {
            timesHitWhileAbducting = 0;
        }

        // RPC's


        [ClientRpc]
        public new void SwitchToBehaviourClientRpc(int stateIndex)
        {
            if (stateIndex == currentBehaviourStateIndex) { return; }
            SwitchToBehaviourStateOnLocalClient(stateIndex);
        }

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

        /*[ClientRpc]
        public void KillPlayerInSackClientRpc()
        {
            logger.LogDebug("KillPlayerInSackClientRpc()");
            if (inSpecialAnimationWithPlayer == null) { logger.LogError("inSpecialAnimationWithPlayer is null in KillPlayerInSackClientRpc()"); return; }
            PlayerControllerB player = inSpecialAnimationWithPlayer;
            CancelSpecialAnimationWithPlayer();
            if (localPlayer != player) { return; }
            StartOfRound.Instance.allowLocalPlayerDeath = true;
            player.KillPlayer(Vector3.zero, false);
        }*/

        [ServerRpc(RequireOwnership = false)]
        public void DoAnimationServerRpc(string animationName)
        {
            if (!IsServer) { return; }
            DoAnimationClientRpc(animationName);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            logger.LogDebug("DoAnimation: " + animationName);
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
            SwitchToBehaviourStateOnLocalClient((int)State.Abducting);
            creatureAnimator.SetTrigger("pickup");
        }

        [ServerRpc(RequireOwnership = false)]
        public void AddTargetPlayerServerRpc(ulong clientId)
        {
            if (!IsServer) { return; }
            AddTargetPlayerClientRpc(clientId);
        }

        [ClientRpc]
        public void AddTargetPlayerClientRpc(ulong clientId)
        {
            PlayerControllerB player = PlayerFromId(clientId);

            if (currentBehaviourStateIndex == (int)State.Spawning)
            {
                SwitchToBehaviourStateOnLocalClient((int)State.Chasing);
                inSpecialAnimation = true;
                creatureAnimator.SetTrigger("start");
            }

            if (!targetPlayers.Contains(player)) { targetPlayers.Add(player); }

            logger.LogDebug($"Added {player.playerUsername} to targeted players");
        }
    }
}