using BepInEx.Logging;
using GameNetcodeStuff;
using SCP4666.YulemanKnife;
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
    internal class SCP4666AI : EnemyAI
    {
        private static ManualLogSource logger = LoggerInstance;
        public static SCP4666AI? Instance { get; private set; }

#pragma warning disable 0649
        public NetworkAnimator networkAnimator;
        public Transform RightHandTransform;
        public Transform ChildSackTransform;
        public GameObject ChildSackPrefab;
        public AudioClip FootstepSFX;
        public AudioClip TeleportSFX;
        public AudioClip LaughSFX;
        public AudioClip RoarSFX;
        public GameObject YulemanMesh;
        public Transform turnCompass;
        public ThrownKnifeScript thrownKnifeScript;
        public YulemanKnifeBehavior KnifeScript;
        public BoxCollider AttackArea;
#pragma warning restore 0649

        Vector3 mainEntranceOutsidePosition;
        Vector3 mainEntrancePosition;
        Vector3 escapePosition;

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

        public bool isKnifeOwned = true;
        bool callingKnifeBack;
        bool isAngry;
        int timesHitWhileAbducting;



        // Constants
        readonly Vector3 insideScale = new Vector3(1.5f, 1.5f, 1.5f);
        readonly Vector3 outsideScale = new Vector3(2f, 2f, 2f);

        // Config Values
        int minPresentCount = 3;
        int maxPresentCount = 5;
        float teleportCooldown = 10f;
        float knifeThrowCooldown = 10f;
        float knifeReturnCooldown = 4f;
        float knifeThrowMinDistance = 5f;
        float knifeThrowMaxDistance = 15f;
        float teleportDistance = 15f;
        float distanceToPickUpKnife = 15f;
        int sliceDamage = 25;
        int slapDamage = 10;
        int hitAmountToDropPlayer = 5;
        bool makeScreenBlackAbduct = true;

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

            minPresentCount = configMinPresentCount.Value;
            maxPresentCount = configMaxPresentCount.Value;
            teleportCooldown = configTeleportCooldown.Value;
            knifeThrowCooldown = configKnifeThrowCooldown.Value;
            knifeReturnCooldown = configKnifeReturnCooldown.Value;
            knifeThrowMinDistance = configKnifeThrowMinDistance.Value;
            knifeThrowMaxDistance = configKnifeThrowMaxDistance.Value;
            teleportDistance = configTeleportDistance.Value;
            distanceToPickUpKnife = configDistanceToPickUpKnife.Value;
            sliceDamage = configSliceDamage.Value;
            slapDamage = configSlapDamage.Value;
            hitAmountToDropPlayer = configHitAmountToDropPlayer.Value;
            makeScreenBlackAbduct = configMakeScreenBlackAbduct.Value;

            currentBehaviourStateIndex = (int)State.Spawning;

            SetEnemyOutside(true);

            mainEntrancePosition = RoundManager.FindMainEntrancePosition();
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(false, true);

            if (IsServerOrHost)
            {
                int num = UnityEngine.Random.Range(minPresentCount, maxPresentCount + 1);
                SpawnPresents(num);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-4666 in the scene. Removing this one.");
                if (!IsServerOrHost) { return; }
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
            }
        }

        public void SetKnifeAsOwned(bool value)
        {
            if (value == false) { KnifeScript.MakeKnifeVisible(value); }
            isKnifeOwned = value;
        }

        public void SpawnPresents(int amount)
        {
            System.Random random = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);

            for (int i = 0; i < amount; i++)
            {
                Item giftItem = StartOfRound.Instance.allItemsList.itemsList.Where(x => x.name == "GiftBox").FirstOrDefault();
                Vector3 pos = RoundManager.Instance.GetRandomPositionInRadius(transform.position, 1, 1.5f, random);
                GiftBoxItem gift = GameObject.Instantiate(giftItem.spawnPrefab, pos, Quaternion.identity).GetComponentInChildren<GiftBoxItem>();
                gift.NetworkObject.Spawn();
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
            if (!teleporting) { timeSinceTeleport += Time.deltaTime; }
            timeSinceKnifeThrown += Time.deltaTime;
            timeSinceGrabPlayer += Time.deltaTime;

            if (currentBehaviourStateIndex == (int)State.Spawning)
            {
                turnCompass.LookAt(localPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y - 90, 0f)), 10f * Time.deltaTime); // TODO: Test this
            }

            if (localPlayer.HasLineOfSightToPosition(transform.position) && localPlayer != inSpecialAnimationWithPlayer)
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

            if (isPlayerInSack && inSpecialAnimationWithPlayer != null && (inSpecialAnimationWithPlayer.isPlayerDead || inSpecialAnimationWithPlayer.disconnectedMidGame))
            {
                PlayerDiedWhileInSackClientRpc();
                return;
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Spawning:
                    //agent.speed = 0f;

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
                    agent.speed = isAngry ? 7f : 5f;

                    if (TargetPlayers.Count == 0 || !TargetClosestPlayer())
                    {
                        GetEscapeNode();
                        SwitchToBehaviourClientRpc((int)State.Abducting);
                        return;
                    }

                    if ((timeSinceTeleport > teleportCooldown && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance)
                            || (isAngry && timeSinceTeleport > teleportCooldown / 2 && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance / 2))
                    {
                        logger.LogDebug("can teleport");
                        if (!KnifeScript.isThrown
                            && !callingKnifeBack
                            && !inSpecialAnimation
                            && !teleporting)
                        {
                            logger.LogDebug("teleporting");
                            timeSinceTeleport = 0f;
                            teleporting = true;
                            TeleportToTargetPlayer();
                            return;
                        }
                    }

                    if (isKnifeOwned && !inSpecialAnimation)
                    {
                        if (KnifeScript.isThrown && !callingKnifeBack && timeSinceKnifeThrown > knifeReturnCooldown && KnifeScript != null) // TODO: Test this
                        {
                            logger.LogDebug("KnifeThrown: " + KnifeScript.isThrown);
                            callingKnifeBack = true;
                            networkAnimator.SetTrigger("grab");
                        }
                        if (KnifeScript != null
                            && !KnifeScript.isThrown
                            && !callingKnifeBack
                            && !teleporting
                            && timeSinceKnifeThrown > knifeThrowCooldown)
                        {
                            //logger.LogDebug("Begin throwing knife");
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
                            }
                            return;
                        }
                    }

                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                case (int)State.Abducting:
                    agent.speed = 6f;

                    if (TargetPlayers.Count > 0)
                    {
                        SwitchToBehaviourClientRpc((int)State.Chasing);
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

                    if (!SetDestinationToPosition(mainEntrancePosition, true))
                    {
                        Teleport(mainEntrancePosition, false);
                    }

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
                if (player.HasLineOfSightToPosition(transform.position)) { return true; }
            }

            return false;
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

            //if (newPlayerToTarget != null && targetPlayer != newPlayerToTarget) { ChangeTargetPlayerClientRpc(newPlayerToTarget.actualClientId); }
            targetPlayer = newPlayerToTarget;
            return targetPlayer != null;
        }

        #region Overrides
        public override void DaytimeEnemyLeave()
        {
            logger.LogDebug("In DaytimeEnemyLeave()");
            base.DaytimeEnemyLeave();
            if (!IsServerOrHost || currentBehaviourStateIndex != (int)State.Abducting) { return; }
            if (isPlayerInSack)
            {
                KillPlayerInSackClientRpc();
            }
            KillEnemyOnOwnerClient(true);
        }

        public override void KillEnemy(bool destroy = false) // Synced
        {
            logger.LogDebug("In KillEnemy()");
            CancelSpecialAnimationWithPlayer();

            if (KnifeScript.thrownKnifeScript != null)
            {
                Destroy(KnifeScript.thrownKnifeScript);
                KnifeScript.thrownKnifeScript = null;
            }

            if (IsServerOrHost && !daytimeEnemyLeaving)
            {
                if (isKnifeOwned)
                {
                    YulemanKnifeBehavior newKnife = GameObject.Instantiate(KnifeScript.itemProperties.spawnPrefab, RightHandTransform.position, Quaternion.identity).GetComponentInChildren<YulemanKnifeBehavior>();
                    newKnife.NetworkObject.Spawn();

                    int knifeValue = UnityEngine.Random.Range(configKnifeMinValue.Value, configKnifeMaxValue.Value + 1);
                    SetKnifeValueClientRpc(newKnife.NetworkObject, knifeValue);
                }

                ChildSackBehavior sack = GameObject.Instantiate(ChildSackPrefab, transform.position, Quaternion.identity).GetComponentInChildren<ChildSackBehavior>();
                int sackValue = UnityEngine.Random.Range(configKnifeMinValue.Value, configKnifeMaxValue.Value);
                sack.SetScrapValue(sackValue);
                sack.NetworkObject.Spawn();
                
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
                    CancelSpecialAnimationWithPlayer(); // TODO: Make sure this is running for all clients
                }
            }
        }


        public override void HitFromExplosion(float distance) // TODO: Make sure this is synced
        {
            logger.LogDebug("Yuleman hit from explosion");
            base.HitFromExplosion(distance);

            if (inSpecialAnimation) { return; }

            if (distance < 2)
            {
                HitEnemy(9);
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

        public PlayerControllerB? MeetsStandardPlayerCollisionConditions(Collider other)
        {
            if (isEnemyDead)
            {
                return null;
            }
            PlayerControllerB component = other.gameObject.GetComponent<PlayerControllerB>();
            if (component == null || component != GameNetworkManager.Instance.localPlayerController)
            {
                return null;
            }
            if (!PlayerIsTargetable(component, cannotBeInShip: false))
            {
                Debug.Log("Player is not targetable");
                return null;
            }
            return component;
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client collided with
        {
            base.OnCollideWithPlayer(other);
            PlayerControllerB? player = MeetsStandardPlayerCollisionConditions(other);
            if (player == null) { return; }
            if (inSpecialAnimationWithPlayer != null && inSpecialAnimationWithPlayer == player) { return; }
            if (timeSinceDamagePlayer > 3f)
            {
                if (player.isPlayerDead || inSpecialAnimation || isEnemyDead) { return; }
                timeSinceDamagePlayer = 0f;

                if (IsPlayerChild(player) && timeSinceGrabPlayer > 10f)
                {
                    if (KnifeScript.isThrown || callingKnifeBack) { return; } // TODO: Test this
                    timeSinceGrabPlayer = 0f;
                    inSpecialAnimation = true;
                    FreezePlayer(player, true);
                    player.DropAllHeldItemsAndSync();
                    GrabPlayerServerRpc(player.actualClientId);
                    return;
                }

                if (isKnifeOwned && !KnifeScript.isThrown)
                {
                    //int deathAnim = UnityEngine.Random.Range(0, 2) == 1 ? 7 : 0;
                    //player.DamagePlayer(sliceDamage, true, true, CauseOfDeath.Stabbing, deathAnim);
                    DoAnimationServerRpc("slash");
                }
                else
                {
                    //player.DamagePlayer(slapDamage, true, true, CauseOfDeath.Mauling, 0, false, transform.forward * 5);
                    DoAnimationServerRpc("slap");
                }
            }
        }

        public override void CancelSpecialAnimationWithPlayer() // TODO: Make sure this is synced
        {
            logger.LogDebug("In CancelSpecialAnimationWithPlayer()");

            if (isPlayerInSack)
            {
                inSpecialAnimationWithPlayer.voiceMuffledByEnemy = false;
                MakePlayerInvisible(inSpecialAnimationWithPlayer, false);
                creatureAnimator.SetBool("bagWalk", false);

                if (localPlayer == inSpecialAnimationWithPlayer)
                {
                    MakePlayerScreenBlack(false);
                }
            }

            if (localPlayer == inSpecialAnimationWithPlayer)
            {
                FreezePlayer(localPlayer, false);
                localPlayerHasSeenYuleman = false;
            }

            inSpecialAnimationWithPlayer.transform.SetParent(null);

            isGrabbingPlayer = false;
            isPlayerInSack = false;
            timesHitWhileAbducting = 0;
            timeSinceGrabPlayer = 0f;

            base.CancelSpecialAnimationWithPlayer();
        }

        #endregion

        #region Animation
        // Animation Functions

        public void DoSlashDamageAnimation()
        {
            if (!IsServerOrHost) { return; }
            logger.LogDebug("In DoSlashDamageAnimation()");
            //List<PlayerControllerB> players = GetAllPlayersInAttackArea();
            List<PlayerControllerB> players = StartOfRound.Instance.allPlayerScripts.ToList();
            foreach (var player in players)
            {
                if (Vector3.Distance(player.transform.position, transform.position) > 5f) { continue; }
                int deathAnim = UnityEngine.Random.Range(0, 2) == 1 ? 7 : 0;
                player.DamagePlayer(sliceDamage, true, true, CauseOfDeath.Stabbing, deathAnim);
            }
        }

        public void DoSlapDamageAnimation()
        {
            if (!IsServerOrHost) { return; }
            logger.LogDebug("In DoSlapDamageAnimation()");
            //List<PlayerControllerB> players = GetAllPlayersInAttackArea();
            List<PlayerControllerB> players = StartOfRound.Instance.allPlayerScripts.ToList();
            foreach (var player in players)
            {
                if (Vector3.Distance(player.transform.position, transform.position) > 5f) { continue; }
                player.DamagePlayer(slapDamage, true, true, CauseOfDeath.Mauling, 0, false, transform.forward * 5);
            }
        }

        /*List<PlayerControllerB> GetAllPlayersInAttackArea() // TODO: Figure out how to use this in next update
        {
            List<PlayerControllerB> players = [];

            //Vector3 center = AttackArea.transform.TransformPoint(AttackArea.center);
            Collider[] hitColliders = Physics.OverlapBox(AttackArea.center, AttackArea.size / 2, AttackArea.transform.rotation, StartOfRound.Instance.playersMask);

            foreach (Collider collider in hitColliders)
            {
                logger.LogDebug("Checking collider");
                if (!collider.gameObject.TryGetComponent(out PlayerControllerB player)) { continue; }
                logger.LogDebug("Adding player to list");
                players.Add(player);
            }

            return players;
        }*/

        public void SetInSpecialAnimation() // Synced
        {
            inSpecialAnimation = true;
        }

        public void UnsetInSpecialAnimation() // Synced
        {
            inSpecialAnimation = false;
        }

        public void CallKnifeBack() // Synced
        {
            inSpecialAnimation = true;
            logger.LogDebug("CallKnifeBack() called");

            if (KnifeScript == null) // TODO: Rework this
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
                return;
            }

            // Call knife back
            logger.LogDebug("Player doesnt have knife, calling it back");
            KnifeScript.CallKnife();
        }

        public void CheckForKnife() // Synced
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

            //inSpecialAnimation = false;
            callingKnifeBack = false;
            GrabKnife();
        }

        public void GrabKnife() // Synced
        {
            logger.LogDebug("GrabKnife() called");
            KnifeScript.previousPlayerHeldBy = null;

            isKnifeOwned = true;
            callingKnifeBack = false;
            timeSinceKnifeThrown = 0f;
            MakeKnifeVisible();
            isAngry = false;
            logger.LogDebug("Grabbed knife");
        }

        public void ThrowKnife() // Synced
        {
            logger.LogDebug("ThrowKnife() called");
            if (!isKnifeOwned || KnifeScript.isThrown) { return; }
            logger.LogDebug("In throwing knife animation");
            inSpecialAnimation = false;

            if (!IsServerOrHost) { return; }
            ThrowKnifeClientRpc(targetPlayer.actualClientId);
        }

        public void MakeKnifeVisible() // Synced
        {
            logger.LogDebug("MakeKnifeVisible() called");
            if (!isKnifeOwned) { return; }
            KnifeScript.MakeKnifeVisible(true);
        }

        public void MakeKnifeInvisible() // Synced
        {
            logger.LogDebug("MakeKnifeInvisible() called");
            KnifeScript.MakeKnifeVisible(false);
        }

        public void PlayRoarSFX() // Synced
        {
            creatureVoice.PlayOneShot(RoarSFX);
        }

        public void PlayFootstepSFX() // Synced
        {
            creatureSFX.PlayOneShot(FootstepSFX);
        }

        public void GrabPlayer() // Synced
        {
            logger.LogDebug("GrabPlayer() called");
            if (inSpecialAnimationWithPlayer == null) { logger.LogError("inSpecialAnimationWithPlayer is null in GrabPlayer()"); return; }
            inSpecialAnimation = true;
            inSpecialAnimationWithPlayer.transform.SetParent(RightHandTransform);
            isGrabbingPlayer = true;
        }

        public void PutPlayerInSack() // Synced
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
                MakePlayerScreenBlack(true);
            }

            MakePlayerInvisible(inSpecialAnimationWithPlayer, true);
            inSpecialAnimationWithPlayer.voiceMuffledByEnemy = true;
            logger.LogDebug(inSpecialAnimationWithPlayer.playerUsername + " put in yulemans sack");
        }

        public void FinishStartAnimation() // Synced
        {
            if (IsServerOrHost)
            {
                SwitchToBehaviourClientRpc((int)State.Chasing);
                inSpecialAnimation = false;
            }
        }

        #endregion

        // RPC's

        [ClientRpc]
        public void BecomeVisibleClientRpc()
        {
            spawnedAndVisible = true;
            YulemanMesh.SetActive(true);
        }

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
        public void ThrowKnifeClientRpc(ulong targetPlayerId)
        {
            targetPlayer = PlayerFromId(targetPlayerId);
            Vector3 throwDirection = (targetPlayer.bodyParts[5].position - RightHandTransform.position).normalized;
            KnifeScript.ThrowKnife(throwDirection);
            logger.LogDebug("Knife was thrown");
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
            inSpecialAnimation = true;
            targetPlayer = PlayerFromId(clientId);
            inSpecialAnimationWithPlayer = targetPlayer;
            TargetPlayers.Remove(targetPlayer);
            GrabPlayerClientRpc(clientId);
        }

        [ClientRpc]
        public void GrabPlayerClientRpc(ulong clientId)
        {
            inSpecialAnimation = true;
            targetPlayer = PlayerFromId(clientId);
            inSpecialAnimationWithPlayer = targetPlayer;
            inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
            inSpecialAnimationWithPlayer.snapToServerPosition = true;
            inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
            creatureAnimator.SetTrigger("grabPlayer");
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
        }

        [ClientRpc]
        public void PlayerDiedWhileInSackClientRpc()
        {
            inSpecialAnimationWithPlayer.inAnimationWithEnemy = null;
            inSpecialAnimationWithPlayer = null;
            isPlayerInSack = false;
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity