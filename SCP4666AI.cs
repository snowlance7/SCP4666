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
        public GameObject KnifeMeshObj;
        public YulemanKnifeBehavior? KnifeScript;
        public GameObject KnifePrefab;
        public GameObject ChildSackPrefab;
        public AudioClip FootstepSFX;
        public AudioClip TeleportSFX;
        public AudioClip LaughSFX;
        public AudioClip RoarSFX;
        public GameObject YulemanMesh;
        public Transform turnCompass;
#pragma warning restore 0649

        Vector3 mainEntranceOutsidePosition;
        Vector3 mainEntrancePosition;
        Vector3 escapePosition;

        PlayerControllerB? playerStart;
        List<PlayerControllerB> TargetPlayers = [];
        bool localPlayerHasSeenYuleman;
        bool spawnedAndVisible;

        bool isGrabbingPlayer;
        bool isPlayerInSack;

        float timeSinceDamagePlayer;
        float timeSinceTeleport;
        float timeSinceKnifeThrown;
        float timeSinceGrabPlayer;
        float timeSinceSyncedAIInterval;

        bool teleporting;

        bool isKnifeOwned = true;
        bool isKnifeThrown;
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
            timeSinceSyncedAIInterval += Time.deltaTime;

            if (timeSinceSyncedAIInterval > AIIntervalTime)
            {
                timeSinceSyncedAIInterval = 0f;
                DoSyncedAIInterval();
            }

            if (currentBehaviourStateIndex == (int)State.Spawning && playerStart != null)
            {
                turnCompass.LookAt(playerStart.gameplayCamera.transform.position);
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

        public void DoSyncedAIInterval()
        {
            if (currentBehaviourStateIndex != (int)State.Spawning) { return; }
            playerStart = GetClosestPlayer(false, true);
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f)
            {
                logger.LogDebug("Not doing ai interval");
                return;
            };

            /*if (!spawnedAndVisible)
            {
                if (!InLineOfSight())
                {
                    spawnedAndVisible = true;
                    BecomeVisibleClientRpc();
                }
                return;
            }*/

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
                        SwitchToBehaviourStateCustom(State.Abducting);
                        return;
                    }

                    if ((timeSinceTeleport > teleportCooldown && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance)
                            || (isAngry && timeSinceTeleport > teleportCooldown / 2 && Vector3.Distance(targetPlayer.transform.position, transform.position) > teleportDistance / 2))
                    {
                        if (!isKnifeThrown
                            && !callingKnifeBack
                            && !inSpecialAnimation
                            && !teleporting)
                        {
                            timeSinceTeleport = 0f;
                            teleporting = true;
                            TeleportToTargetPlayer();
                            return;
                        }
                    }

                    if (isKnifeOwned && !inSpecialAnimation)
                    {
                        if (isKnifeThrown && !callingKnifeBack && timeSinceKnifeThrown > knifeReturnCooldown && KnifeScript != null/* && !KnifeScript.isThrown*/) // TODO: Test this
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

        bool GetTeleportNode() // TODO: Use getrandomnavmeshpositioninradius instead of ai nodes?
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

        public bool TargetClosestPlayer() // TODO: Change this to just target each player in list
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

        public void DropPlayer() // TODO: Needs to be synced with all clients
        {
            if (inSpecialAnimationWithPlayer != null)
            {
                if (isPlayerInSack)
                {
                    logger.LogDebug($"Dropping {inSpecialAnimationWithPlayer.playerUsername} from sack");

                    inSpecialAnimationWithPlayer.voiceMuffledByEnemy = false;
                    MakePlayerInvisible(inSpecialAnimationWithPlayer, false);
                    creatureAnimator.SetBool("bagWalk", false);
                }

                if (localPlayer == inSpecialAnimationWithPlayer)
                {
                    MakePlayerScreenBlack(false);
                    FreezePlayer(localPlayer, false);
                    localPlayerHasSeenYuleman = false;
                }
                
                inSpecialAnimationWithPlayer.inAnimationWithEnemy = null;
                inSpecialAnimationWithPlayer.transform.SetParent(null);
            }

            isGrabbingPlayer = false;
            isPlayerInSack = false;
            inSpecialAnimationWithPlayer = null;
            timesHitWhileAbducting = 0;
            timeSinceGrabPlayer = 0f;
        }

        #region Overrides
        public override void DaytimeEnemyLeave()
        {
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
            //if (inSpecialAnimation) { return; }
            DropPlayer();
            if (IsServerOrHost && !daytimeEnemyLeaving)
            {
                if (isKnifeOwned)
                {
                    YulemanKnifeBehavior knife = GameObject.Instantiate(KnifePrefab, transform.position, Quaternion.identity).GetComponentInChildren<YulemanKnifeBehavior>();
                    int knifeValue = UnityEngine.Random.Range(configSackMinValue.Value, configSackMaxValue.Value);
                    knife.SetScrapValue(knifeValue);
                    knife.NetworkObject.Spawn(true);
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
            if (isEnemyDead || inSpecialAnimation) return;

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
                    DropPlayer();
                }
            }
        }


        public override void HitFromExplosion(float distance)
        {
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

        public override void OnCollideWithPlayer(Collider other) // This only runs on client collided with
        {
            base.OnCollideWithPlayer(other);
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player == null) { return; }
            if (inSpecialAnimationWithPlayer != null && inSpecialAnimationWithPlayer == player) { return; }
            if (timeSinceDamagePlayer > 3f)
            {
                if (player.isPlayerDead || inSpecialAnimation || isEnemyDead) { return; }
                timeSinceDamagePlayer = 0f;

                if (IsPlayerChild(player) && timeSinceGrabPlayer > 10f)
                {
                    inSpecialAnimation = true;
                    FreezePlayer(player, true);
                    player.DropAllHeldItemsAndSync();
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

        public override void CancelSpecialAnimationWithPlayer()
        {
            DropPlayer();
            base.CancelSpecialAnimationWithPlayer();
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

            inSpecialAnimation = false;
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
            if (inSpecialAnimationWithPlayer == null) { logger.LogWarning("inSpecialAnimationWithPlayer is null in GrabPlayer()"); return; }
            inSpecialAnimation = true;
            inSpecialAnimationWithPlayer.transform.SetParent(RightHandTransform);
            isGrabbingPlayer = true;
        }

        public void PutPlayerInSack()
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

        public void FinishStartAnimation()
        {
            if (IsServerOrHost)
            {
                SwitchToBehaviourStateCustom(State.Chasing);
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
        public void KillPlayerInSackClientRpc()
        {
            if (inSpecialAnimationWithPlayer == null) { logger.LogError("playerInSack is null in KillPlayerInSackClientRpc()"); return; }
            PlayerControllerB player = inSpecialAnimationWithPlayer;
            DropPlayer();
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
            GrabPlayerClientRpc(clientId);
        }

        [ClientRpc]
        public void GrabPlayerClientRpc(ulong clientId)
        {
            inSpecialAnimation = true;
            inSpecialAnimationWithPlayer = PlayerFromId(clientId);
            inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
            TargetPlayers.Remove(targetPlayer);
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
        private void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
            transform.localScale = value ? outsideScale : insideScale;
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity