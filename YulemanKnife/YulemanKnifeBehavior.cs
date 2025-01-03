using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666.YulemanKnife
{
    internal class YulemanKnifeBehavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable 0649
        public AudioSource KnifeAudio = null!;
        public ScanNodeProperties ScanNode = null!;
        public AudioClip[] SliceSFX = null!;
        public AudioClip[] TearSFX = null!;
        public AudioClip KnifeImpactSFX = null!;
        public AudioClip KnifeWallPullSFX = null!;
        public AudioClip KnifeChargeSFX = null!;
        public GameObject RunePrefab = null!;
        public Transform KnifeTip = null!;
        public Collider collider = null!;
#pragma warning restore 0649

        List<Collider> EntitiesHitByKnife = [];
        public YulemanKnifeRuneBehavior? RuneScript = null!;

        // Constants
        readonly Vector3 PositionOffsetStab = new Vector3(-0.2f, 0.26f, -0.02f);
        readonly Vector3 PositionOffsetThrow = new Vector3(0.18f, 0.035f, -0.08f);
        readonly Vector3 RotationOffsetStab = new Vector3(-30, -90, 90);
        readonly Vector3 RotationOffsetThrow = new Vector3(30, 100, -90);
        const int knifeMask = 1084754248;
        const int defaultExcludeMask = -2621449;
        const float maxThrowDistance = 400f;

        // Variables
        bool isCharged;
        Coroutine? chargeCoroutine = null;
        RaycastHit[]? objectsHitByKnife;
        List<RaycastHit> objectsHitByKnifeList = new List<RaycastHit>();
        float timeAtLastDamageDealt;
        public PlayerControllerB? previousPlayerHeldBy = null!;

        float returnTime;
        float returnSpeed = 1f;
        bool returningToPlayer;
        Vector3 startThrowPosition;
        Vector3 postThrowPosition;
        private bool returningToYuleman;
        public bool isThrown;
        public bool stuckInWall;
        bool canFall = true;

        // Config Variables
        float chargeTime = 1f;
        int knifeHitForce = 1;
        float throwForce = 100f;
        int knifeHitForceYuleman = 25;

        public override void Start()
        {
            base.Start();

            chargeTime = configChargeTime.Value;
            knifeHitForce = configKnifeHitForce.Value;
            throwForce = configThrowForce.Value;
            knifeHitForceYuleman = configKnifeHitForceYuleman.Value;

            if (SCP4666AI.Instance != null && SCP4666AI.Instance.KnifeScript == this)
            {
                logger.LogDebug("Setting knife owned by yuleman");
                grabbable = false;
                canFall = false;
                ScanNode.enabled = false;
            }
        }

        public override void Update()
        {
            if (!isThrown && !stuckInWall && !returningToPlayer && !returningToYuleman && canFall) { base.Update(); }
            else
            {
                fallTime = 1f;
                reachedFloorTarget = true;
                bool wasHeld = isHeld;
                isHeld = true;
                base.Update();
                isHeld = wasHeld;
            }

            if (playerHeldBy != null)
            {
                previousPlayerHeldBy = playerHeldBy;
            }
            if (returningToYuleman && SCP4666AI.Instance != null)
            {
                returnTime += Time.deltaTime * returnSpeed;

                returnTime = Mathf.Clamp01(returnTime);

                transform.position = Vector3.Lerp(postThrowPosition, SCP4666AI.Instance.RightHandTransform.position, returnTime);

                if (returnTime >= 1f && IsServerOrHost)
                {
                    returningToYuleman = false;
                    NetworkObject.Despawn();
                }
                return;
            }
            if (returningToPlayer && previousPlayerHeldBy != null)
            {
                returnTime += Time.deltaTime * returnSpeed;

                returnTime = Mathf.Clamp01(returnTime);

                transform.position = Vector3.Lerp(postThrowPosition, GetPositionFrontOfPlayer(previousPlayerHeldBy), returnTime);

                if (returnTime >= 1f)
                {
                    returningToPlayer = false;
                    returnTime = 0f;
                    if (localPlayer == previousPlayerHeldBy)
                    {
                        GrabGrabbableObjectOnClient(this);
                    }
                }
            }
        }

        public void FixedUpdate()
        {
            if (isThrown)
            {
                Vector3 nextPosition = transform.position + transform.forward * throwForce * Time.fixedDeltaTime;

                if (Physics.Raycast(transform.position, nextPosition - transform.position, throwForce * Time.fixedDeltaTime, StartOfRound.Instance.collidersAndRoomMask)) // Detect wall
                {
                    logger.LogDebug("Knife hit wall, stopping");
                    isThrown = false;
                    StopKnife(true);
                }
                else if (Vector3.Distance(startThrowPosition, postThrowPosition) >= maxThrowDistance)
                {
                    logger.LogDebug("Knife reached max distance, falling to ground");
                    isThrown = false;
                    StopKnife();
                }
                else
                {
                    transform.position = nextPosition;  // Move object if no collision
                }
            }
        }

        Vector3 GetKnifeEndPoint()
        {
            Ray ray = new Ray(transform.position, transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, maxThrowDistance, StartOfRound.Instance.collidersAndRoomMask))
            {
                float offset = Vector3.Distance(KnifeTip.position, transform.position);
                return hit.point - transform.forward * offset;
            }

            logger.LogDebug("Couldnt find wall");
            return ray.GetPoint(maxThrowDistance);
        }

        public void OnTriggerEnter(Collider other) // Synced
        {
            if (isThrown)
            {
                logger.LogDebug("Hit " + other.gameObject.name);
                if (!other.transform.TryGetComponent<IHittable>(out var iHit) || EntitiesHitByKnife.Contains(other)) { return; }
                logger.LogDebug("Hit");
                Vector3 forward = KnifeTip.transform.forward;
                if (previousPlayerHeldBy != null)
                {
                    bool hitSuccessful = iHit.Hit(knifeHitForce, forward, previousPlayerHeldBy, playHitSFX: true, 5);
                    if (!hitSuccessful) { logger.LogDebug("Hit unsuccessful"); return; }
                }
                else
                {
                    if (!other.TryGetComponent<PlayerControllerB>(out PlayerControllerB player)) { return; }
                    if (localPlayer != player) { return; }
                    player.DamagePlayer(knifeHitForceYuleman, true, true, CauseOfDeath.Stabbing);
                }
                EntitiesHitByKnife.Add(other);
                RoundManager.PlayRandomClip(KnifeAudio, TearSFX);
            }
        }

        public void ThrowKnife(Vector3 throwDirection)
        {
            parentObject = null;
            transform.SetParent(null);
            grabbable = false;
            isHeld = false;
            isHeldByEnemy = false;
            transform.rotation = Quaternion.LookRotation(throwDirection, KnifeTip.up);
            EnableCollider(true);
            startThrowPosition = transform.position;
            postThrowPosition = GetKnifeEndPoint();
            isThrown = true;
            canFall = false;
        }

        void StopKnife(bool hitWall = false)
        {
            logger.LogDebug("Stopping knife");
            EntitiesHitByKnife.Clear();
            grabbable = true;

            if (isThrown)
            {
                isThrown = false;
                return;
            }

            transform.position = postThrowPosition;

            stuckInWall = hitWall;
            if (!stuckInWall)
            {
                canFall = true;
                FallToGround();
                return;
            }
            RoundManager.Instance.PlayAudibleNoise(transform.position);
            KnifeAudio.PlayOneShot(KnifeImpactSFX);
            WalkieTalkie.TransmitOneShotAudio(KnifeAudio, KnifeImpactSFX, 0.5f);
        }

        public void EnableCollider(bool enable)
        {
            ScanNode.enabled = !enable;
            collider.enabled = enable;
            collider.includeLayers = knifeMask;
            collider.excludeLayers = enable ? 0 : defaultExcludeMask;
        }

        public override void EquipItem()
        {
            if (!IsOwner)
            {
                ItemEquippedServerRpc(playerHeldBy.actualClientId);
            }
            else
            {
                ItemEquippedClientRpc();
            }
            base.EquipItem();
            ChargeCancel();
        }

        public override void PocketItem()
        {
            base.PocketItem();
            ChargeCancel();
            HitKnife(cancel: true);
        }

        public override void ItemActivate(bool used, bool buttonDown = true) // Synced
        {
            base.ItemActivate(used, buttonDown);

            if (buttonDown)
            {
                chargeCoroutine = StartCoroutine(ChargeKnife());
            }
            else
            {
                StopCoroutine(chargeCoroutine);
                playerHeldBy.playerBodyAnimator.SetTrigger("UseHeldItem1");

                if (isCharged)
                {
                    ChargeCancel();
                    PlayerThrowKnife();
                }
                else
                {
                    RoundManager.PlayRandomClip(KnifeAudio, SliceSFX);
                    previousPlayerHeldBy = playerHeldBy;
                    HitKnife();
                }

                isCharged = false;
            }
        }

        IEnumerator ChargeKnife()
        {
            yield return new WaitForSecondsRealtime(chargeTime);
            itemProperties.rotationOffset = RotationOffsetThrow;
            itemProperties.positionOffset = PositionOffsetThrow;
            KnifeAudio.PlayOneShot(KnifeChargeSFX, 1f);
            WalkieTalkie.TransmitOneShotAudio(KnifeAudio, KnifeChargeSFX, 1f);
            RoundManager.Instance.PlayAudibleNoise(playerHeldBy.transform.position, KnifeAudio.maxDistance, 0.5f, 0, playerHeldBy.isInHangarShipRoom);
            isCharged = true;
            logger.LogDebug("Knife is charged");
        }

        void PlayerThrowKnife()
        {
            logger.LogDebug("Throwing knife...");
            isThrown = true;
            canFall = false;

            if (previousPlayerHeldBy != localPlayer) { return; }
            playerHeldBy.DiscardHeldObject();
            PlayerThrowKnifeServerRpc();
        }

        void ChargeCancel()
        {
            isCharged = false;
            itemProperties.rotationOffset = RotationOffsetStab;
            itemProperties.positionOffset = PositionOffsetStab;
        }

        public void HitKnife(bool cancel = false)
        {
            if (!IsOwner) { return; }
            if (previousPlayerHeldBy == null)
            {
                Debug.LogError("Previousplayerheldby is null on this client when HitShovel is called.");
                return;
            }
            previousPlayerHeldBy.activatingItem = false;
            bool flag = false;
            bool flag2 = false;
            int num = -1;
            bool flag3 = false;
            if (!cancel && Time.realtimeSinceStartup - timeAtLastDamageDealt > 0.43f)
            {
                previousPlayerHeldBy.twoHanded = false;
                objectsHitByKnife = Physics.SphereCastAll(previousPlayerHeldBy.gameplayCamera.transform.position + previousPlayerHeldBy.gameplayCamera.transform.right * 0.1f, 0.3f, previousPlayerHeldBy.gameplayCamera.transform.forward, 0.75f, knifeMask, QueryTriggerInteraction.Collide);
                objectsHitByKnifeList = objectsHitByKnife.OrderBy((x) => x.distance).ToList();
                List<EnemyAI> list = new List<EnemyAI>();
                for (int i = 0; i < objectsHitByKnifeList.Count; i++)
                {
                    string layerName = LayerMask.LayerToName(objectsHitByKnifeList[i].transform.gameObject.layer);
                    logger.LogDebug("Hit " + layerName);
                    if (objectsHitByKnifeList[i].transform.gameObject.layer == 8 || objectsHitByKnifeList[i].transform.gameObject.layer == 11)
                    {
                        flag = true;
                        string text = objectsHitByKnifeList[i].collider.gameObject.tag;
                        for (int j = 0; j < StartOfRound.Instance.footstepSurfaces.Length; j++)
                        {
                            if (StartOfRound.Instance.footstepSurfaces[j].surfaceTag == text)
                            {
                                num = j;
                                break;
                            }
                        }
                    }
                    else
                    {
                        if (!objectsHitByKnifeList[i].transform.TryGetComponent<IHittable>(out var component) || objectsHitByKnifeList[i].transform == previousPlayerHeldBy.transform || !(objectsHitByKnifeList[i].point == Vector3.zero) && Physics.Linecast(previousPlayerHeldBy.gameplayCamera.transform.position, objectsHitByKnifeList[i].point, out var _, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            continue;
                        }
                        flag = true;
                        Vector3 forward = previousPlayerHeldBy.gameplayCamera.transform.forward;
                        try
                        {
                            EnemyAICollisionDetect component2 = objectsHitByKnifeList[i].transform.GetComponent<EnemyAICollisionDetect>();
                            if (component2 != null)
                            {
                                if (!(component2.mainScript == null) && !list.Contains(component2.mainScript))
                                {
                                    goto IL_02f2;
                                }
                                continue;
                            }
                            if (!(objectsHitByKnifeList[i].transform.GetComponent<PlayerControllerB>() != null))
                            {
                                goto IL_02f2;
                            }
                            if (!flag3)
                            {
                                flag3 = true;
                                goto IL_02f2;
                            }
                            goto end_IL_027b;
                        IL_02f2:
                            bool flag4 = component.Hit(knifeHitForce, forward, previousPlayerHeldBy, playHitSFX: true, 5);
                            if (flag4 && component2 != null)
                            {
                                list.Add(component2.mainScript);
                            }
                            if (!flag2 && flag4)
                            {
                                flag2 = true;
                                timeAtLastDamageDealt = Time.realtimeSinceStartup;
                                //bloodParticle.Play(withChildren: true);
                                RoundManager.PlayRandomClip(KnifeAudio, TearSFX);

                            }
                        end_IL_027b:;
                        }
                        catch (Exception arg)
                        {
                            Debug.Log($"Exception caught when hitting object with shovel from player #{previousPlayerHeldBy.playerClientId}: {arg}");
                        }
                    }
                }
            }
            if (flag)
            {
                //RoundManager.PlayRandomClip(knifeAudio, hitSFX);
                FindObjectOfType<RoundManager>().PlayAudibleNoise(transform.position, 17f, 0.8f);
                if (!flag2 && num != -1)
                {
                    KnifeAudio.PlayOneShot(StartOfRound.Instance.footstepSurfaces[num].hitSurfaceSFX);
                    WalkieTalkie.TransmitOneShotAudio(KnifeAudio, StartOfRound.Instance.footstepSurfaces[num].hitSurfaceSFX);
                }
                HitShovelServerRpc(num);
            }
        }

        private void HitSurfaceWithKnife(int hitSurfaceID)
        {
            KnifeAudio.PlayOneShot(StartOfRound.Instance.footstepSurfaces[hitSurfaceID].hitSurfaceSFX);
            WalkieTalkie.TransmitOneShotAudio(KnifeAudio, StartOfRound.Instance.footstepSurfaces[hitSurfaceID].hitSurfaceSFX);
        }

        public override void FallWithCurve()
        {
            if (isThrown || !canFall) { return; }
            base.FallWithCurve();
        }

        public override void OnHitGround()
        {
            if (isThrown || !canFall) { return; }
            base.OnHitGround();
        }

        public override void GrabItem() // Synced
        {
            if (RuneScript != null && RuneScript.playerHeldBy == localPlayer) // TODO: TEST THIS
            {
                canFall = true;
                RuneScript.playerHeldBy.DespawnHeldObject();
                localPlayer.activatingItem = false;
            }
        }

        public void ReturnToPlayer()
        {
            if (previousPlayerHeldBy != null)
            {
                StopKnife();
                canFall = false;
                stuckInWall = false;
                postThrowPosition = transform.position;
                returningToPlayer = true;
            }
        }

        public void ReturnToYuleman()
        {
            if (SCP4666AI.Instance != null)
            {
                StopKnife();
                canFall = false;
                stuckInWall = false;
                postThrowPosition = transform.position;
                returningToYuleman = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (RuneScript != null && RuneScript.playerHeldBy != null)
            {
                RuneScript.playerHeldBy.DropAllHeldItemsAndSync();
            }
        }

        // RPCs

        [ServerRpc(RequireOwnership = false)]
        public void ItemEquippedServerRpc(ulong clientId)
        {
            if (IsServerOrHost)
            {
                NetworkObject.ChangeOwnership(clientId);
                if (RuneScript != null)
                {
                    RuneScript.playerHeldBy.DropAllHeldItemsAndSync();
                }
                ItemEquippedClientRpc();
            }
        }

        [ClientRpc]
        public void ItemEquippedClientRpc()
        {
            isThrown = false;
            canFall = true;
            if (stuckInWall) { KnifeAudio.PlayOneShot(KnifeWallPullSFX); }
            stuckInWall = false;
            EntitiesHitByKnife.Clear();
            EnableCollider(false);
            ScanNode.enabled = true;
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayerThrowKnifeServerRpc()
        {
            if (IsServerOrHost)
            {
                if (previousPlayerHeldBy == null) { logger.LogError("PreviousPlayerHeldBy is null"); return; }
                logger.LogDebug("spawning rune");
                RuneScript = GameObject.Instantiate(RunePrefab, GetPositionFrontOfPlayer(previousPlayerHeldBy), Quaternion.identity).GetComponentInChildren<YulemanKnifeRuneBehavior>();
                RuneScript.fallTime = 0f;
                RuneScript.KnifeScript = this;
                RuneScript.NetworkObject.Spawn(destroyWithScene: true);
                PlayerThrowKnifeClientRpc(RuneScript.NetworkObject);
            }
        }

        [ClientRpc]
        public void PlayerThrowKnifeClientRpc(NetworkObjectReference netRef)
        {
            if (previousPlayerHeldBy == null) { logger.LogError("PreviousPlayerHeldBy is null"); return; }
            PlayerControllerB player = previousPlayerHeldBy;
            if (netRef.TryGet(out NetworkObject netObj))
            {
                RuneScript = netObj.GetComponent<YulemanKnifeRuneBehavior>();
                RuneScript.KnifeScript = this;

                if (localPlayer == player)
                {
                    GrabGrabbableObjectOnClient(RuneScript);
                }

                Vector3 throwDirection = player.playerEye.transform.forward;
                ThrowKnife(throwDirection);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void HitShovelServerRpc(int hitSurfaceID)
        {
            HitShovelClientRpc(hitSurfaceID);
        }

        [ClientRpc]
        public void HitShovelClientRpc(int hitSurfaceID)
        {
            //RoundManager.PlayRandomClip(KnifeAudio, hitSFX);
            if (hitSurfaceID != -1)
            {
                HitSurfaceWithKnife(hitSurfaceID);
            }
        }
    }
}