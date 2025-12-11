using Dawn;
using Dawn.Utils;
using Dusk;
using GameNetcodeStuff;
using SCP4666.Doll;
using SCP4666.YulemanKnife;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static SCP4666.Plugin;
using static SCP4666.Utils;

namespace SCP4666
{
    internal class SCP4666AI : EnemyAI // TODO: Rework and add new stuff
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

        public GameObject ThrowingKnifePrefab;

        public Collider collider;

        public GameObject KnifeMesh;

        public ParticleSystem GroundSlamParticles;

        public ThrownKnifeScript thrownKnifeScript;

        public SmartAgentNavigator nav;

        public GameObject DEBUG_hudOverlay;
#pragma warning restore CS8618

        public new bool isOutside => nav.IsAgentOutside();

        Vector3 mainEntranceOutsidePosition;
        Vector3 mainEntranceInsidePosition;
        List<EntranceTeleport> entrances = [];

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
        private float timeSinceSwitchBehavior;

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

        const float spawnTurnCompassSpeed = 20f;
        const float LOSOffset = 2f;
        const float grabPlayerCooldown = 10f;

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

            nav.SetAllValues(isOutside: true);

            mainEntranceInsidePosition = RoundManager.FindMainEntrancePosition();
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(false, true);
            entrances = GameObject.FindObjectsOfType<EntranceTeleport>(includeInactive: false).ToList();

            // spawn throwing knife
            thrownKnifeScript = GameObject.Instantiate(ThrowingKnifePrefab, Vector3.zero, Quaternion.identity).GetComponent<ThrownKnifeScript>();
            thrownKnifeScript.KnifeReturnedEvent.AddListener(KnifeReturned);

            if (!IsServer) { return; }

            if (isBeta)
                Instantiate(DEBUG_hudOverlay, Vector3.zero, Quaternion.identity);

            // spawn presents
            int num = UnityEngine.Random.Range(minPresentsToSpawn, maxPresentsToSpawn);
            SpawnPresents(num);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instances.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            Instances.Remove(this);
            base.OnNetworkDespawn();
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
                gift.NetworkObject.Spawn(destroyWithScene: true);
            }
        }

        public void CustomEnemyAIUpdate()
        {
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
            UpdateTestingHUD();

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
                    if (CanDoSpecialAction() && timeSinceTeleport > teleportCooldown && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance && GetTeleportNode()
                        && (!Utils.isBeta || DEBUG_Teleport))
                    {
                        LongTeleport(targetNode.position, !targetPlayer.isInsideFactory);
                        return;
                    }

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

                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                case (int)State.Abducting:
                    agent.speed = 4f;

                    if (!isPlayerInSack && TargetClosestPlayer())
                    {
                        SwitchToBehaviourClientRpc((int)State.Chasing);
                        return;
                    }

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
                    
                    if (isInsideFactory && !teleporting && !SetDestinationToPosition(targetNode.position))
                    {
                        LongTeleport(mainEntranceOutsidePosition, true);
                        return;
                    }

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        void UpdateTestingHUD()
        {
            if (isBeta && TestingHUDOverlay.Instance != null) // TestingHUD
            {
                TestingHUDOverlay.Instance.label1.text = ((State)currentBehaviourStateIndex).ToString();

                TestingHUDOverlay.Instance.label2.text = "";

                TestingHUDOverlay.Instance.label3.text = "TargetPlayer: " + targetPlayer.playerUsername;

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
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            if (!SmartCanPathToPoint(position)) { return false; }
            return nav.DoPathingToDestination(position);
        }

        public bool SmartCanPathToPoint(Vector3 position)
        {
            Vector3 scpPos = RoundManager.Instance.GetNavMeshPosition(transform.position, RoundManager.Instance.navHit);
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);

            if (nav.CanPathToPoint(scpPos, position) > 0)
                return true;

            foreach (var entrance in entrances)
            {
                bool relevantEntrance = isInsideFactory ? !entrance.isEntranceToBuilding : entrance.isEntranceToBuilding;
                if (!relevantEntrance)
                    continue;

                Vector3 teleportFrom = RoundManager.Instance.GetNavMeshPosition(entrance.entrancePoint.position, RoundManager.Instance.navHit);

                if (entrance.exitPoint == null && !entrance.FindExitPoint())
                    continue;

                Vector3 teleportTo = RoundManager.Instance.GetNavMeshPosition(entrance.exitPoint!.position, RoundManager.Instance.navHit);

                if (nav.CanPathToPoint(scpPos, teleportFrom) > 0 && nav.CanPathToPoint(teleportTo, position) > 0)
                    return true;
            }

            return false;
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
                if (player.HasLineOfSightToPosition(transform.position + Vector3.up * LOSOffset)) { return true; }
            }

            return false;
        }

        public new bool TargetClosestPlayer(float bufferDistance = 1.5f, bool requireLineOfSight = false, float viewWidth = 70f)
        {
            mostOptimalDistance = Mathf.Infinity;
            PlayerControllerB playerControllerB = targetPlayer;
            targetPlayer = null;
            foreach (PlayerControllerB player in TargetPlayers.ToList())
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
            CancelSpecialAnimationWithPlayer();

            thrownKnifeScript.enabled = false;

            MakeKnifeInvisible();

            if (IsServer)
            {
                // Spawn YulemanKnife
                YulemanKnifeBehavior newKnife = GameObject.Instantiate(LethalContent.Items[SCP4666Keys.YulemanKnife].Item.spawnPrefab, transform.position, Quaternion.identity, RoundManager.Instance.mapPropsContainer.transform).GetComponentInChildren<YulemanKnifeBehavior>();
                newKnife.fallTime = 0f;
                newKnife.NetworkObject.Spawn();

                //int knifeValue = UnityEngine.Random.Range(configKnifeMinValue.Value, configKnifeMaxValue.Value + 1);
                //SetKnifeValueClientRpc(newKnife.NetworkObject, knifeValue);

                // Spawn ChildSack
                ChildSackBehavior sack = GameObject.Instantiate(LethalContent.Items[SCP4666Keys.ChildSack].Item.spawnPrefab, transform.position, Quaternion.identity, StartOfRound.Instance.propsContainer).GetComponentInChildren<ChildSackBehavior>();
                sack.fallTime = 0f;
                sack.NetworkObject.Spawn();

                //int sackValue = UnityEngine.Random.Range(configSackMinValue.Value, configSackMaxValue.Value + 1);
                //SetSackValueClientRpc(sack.NetworkObject, sackValue);

                // Spawn Dolls
                int dollsToDrop = UnityEngine.Random.Range(minDollsToDrop, maxDollsToDrop + 1);
                for (int i = 0; i < dollsToDrop; i++)
                {
                    FleshDollBehavior doll = GameObject.Instantiate(LethalContent.Items[SCP4666Keys.FleshDoll].Item.spawnPrefab, transform.position, Quaternion.identity, StartOfRound.Instance.propsContainer).GetComponent<FleshDollBehavior>();
                    doll.fallTime = 0f;
                    doll.NetworkObject.Spawn();
                }
            }

            base.KillEnemy(destroy);
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1) // Synced
        {
            logger.LogDebug("In HitEnemy()");
            if (isEnemyDead)
            {
                logger.LogDebug("Yuleman is dead");
                return;
            }

            enemyHP -= force;
            logger.LogDebug("hp: " + enemyHP);

            if (enemyHP <= 0)
            {
                logger.LogDebug("Attempt killing yuleman on server");
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
                && (!Utils.isBeta || DEBUG_GroundSlam))
            {
                logger.LogDebug("Performing ground slam");
                timesHitWithoutDamagingPlayers = 0;
                timeSinceGroundSlam = 0f;
                inSpecialAnimation = true;
                DoAnimationClientRpc("groundSlam");
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (isEnemyDead) { return; }
            if (timeSinceDamagePlayer < 3f) { return; }
            if (inSpecialAnimation) { return; }
            PlayerControllerB? player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null || !PlayerIsTargetable(player) || player != localPlayer) { return; }
            if (inSpecialAnimationWithPlayer != null && inSpecialAnimationWithPlayer == player) { return; }

            timeSinceDamagePlayer = 0f;

            if (IsPlayerChild(player) && timeSinceGrabPlayer > grabPlayerCooldown && !isPlayerInSack)
            {
                if (CanDoSpecialAction())
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
            logger.LogDebug("OnCollideWithPlayer()");
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

            if (inSpecialAnimationWithPlayer != null)
            {
                if (localPlayer == inSpecialAnimationWithPlayer)
                {
                    NetworkHandlerSCP4666.Instance?.BlackScreenOverlay.SetActive(false);
                    FreezePlayer(localPlayer, false);
                    Instance.AllowPlayerDeathAfterDelay(5f);
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
            logger.LogDebug("CancelSpecialAnimationWithPlayer()");
        }

        #endregion

        #region Animation
        // Animation Functions

        public void SpawnDoll() // Animation
        {
            if (!IsServer) { return; }

            if (dollsToSpawn <= 0)
            {
                inSpecialAnimation = false;
                DoAnimationClientRpc("reset");
                MakeKnifeVisible();
                return;
            }

            logger.LogDebug("DollsToSpawn: " +  dollsToSpawn);
            dollsToSpawn -= 1;
            EvilFleshDollAI doll = GameObject.Instantiate(NetworkHandlerSCP4666.Instance.EvilDollPrefab, RightHandTransform.position, transform.rotation).GetComponent<EvilFleshDollAI>();
            doll.yulemanThrownBy = this;
            doll.NetworkObject.Spawn(destroyWithScene: true);

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

            //inSpecialAnimation = false;

            Vector3 throwDirection = (targetPlayer.bodyParts[5].position - RightHandTransform.position).normalized;
            thrownKnifeScript.ThrowKnife(RightHandTransform, throwDirection);
        }

        public void MakeKnifeVisible() // Animation
        {
            logger.LogDebug("MakeKnifeVisible() called");
            if (isThrowingKnife) { return; }
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
                NetworkHandlerSCP4666.Instance.BlackScreenOverlay.SetActive(true);
                StartOfRound.Instance.allowLocalPlayerDeath = false;
            }

            MakePlayerInvisible(inSpecialAnimationWithPlayer, true);
            inSpecialAnimationWithPlayer.voiceMuffledByEnemy = true;
            inSpecialAnimationWithPlayer.playerCollider.gameObject.SetActive(false);
            SwitchToBehaviourStateOnLocalClient((int)State.Abducting);
            logger.LogDebug(inSpecialAnimationWithPlayer.playerUsername + " put in yulemans sack");
        }

        #endregion

        public new void SetEnemyOutside(bool outside) // Call in SmartNavAgent in unity editor
        {
            transform.localScale = outside ? outsideScale : insideScale;
        }

        public new void SwitchToBehaviourStateOnLocalClient(int stateIndex)
        {
            if (currentBehaviourStateIndex == stateIndex) { return; }

            logger.LogDebug("Switching behavior to " + (State)stateIndex);
            previousBehaviourStateIndex = currentBehaviourStateIndex;
            currentBehaviourStateIndex = stateIndex;
            currentBehaviourState = enemyBehaviourStates[stateIndex];
            PlayAudioOfCurrentState();
            PlayAnimationOfCurrentState();

            timeSinceSwitchBehavior = 0f;

            BehaviourSwitchCleanUp();
        }

        public void BehaviourSwitchCleanUp()
        {

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

        [ClientRpc]
        public void KillPlayerInSackClientRpc()
        {
            logger.LogDebug("KillPlayerInSackClientRpc()");
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
            creatureAnimator.SetTrigger("pickup");
        }

        [ServerRpc(RequireOwnership = false)]
        public void AddTargetPlayerServerRpc(ulong clientId)
        {
            if (!IsServer) { return; }

            PlayerControllerB player = PlayerFromId(clientId);

            if (currentBehaviourStateIndex == (int)State.Spawning)
            {
                SwitchToBehaviourClientRpc((int)State.Chasing);
                DoAnimationClientRpc("start");
            }

            if (TargetPlayers.Contains(player)) { return; }
            TargetPlayers.Add(player);

            /*if (currentBehaviourStateIndex == (int)State.Abducting) // TODO: Test this
            {
                SwitchToBehaviourClientRpc((int)State.Chasing);
            }*/

            logger.LogDebug($"Added {player.playerUsername} to targeted players");
        }

        /*[ClientRpc]
        public void SetKnifeValueClientRpc(NetworkObjectReference netRef, int value)
        {
            if (!netRef.TryGet(out NetworkObject netobj)) { return; }
            if (!netobj.TryGetComponent(out YulemanKnifeBehavior knife)) { return; }
            knife.SetScrapValue(value);
            //knife.FallToGround();
        }

        [ClientRpc]
        public void SetSackValueClientRpc(NetworkObjectReference netRef, int value)
        {
            if (!netRef.TryGet(out NetworkObject netobj)) { return; }
            if (!netobj.TryGetComponent(out ChildSackBehavior sack)) { return; }
            sack.SetScrapValue(value);
            sack.FallToGround();
        }*/
    }
}