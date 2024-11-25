using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666.YulemanKnife
{
    internal class YulemanKnifeBehaviorOld : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable 0649
        public Animator itemAnimator = null!;
        public AudioSource KnifeAudio = null!;
        public ScanNodeProperties ScanNode = null!;
        public SCP4666AI YulemanScript = null!;
        public AudioClip[] SliceSFX = null!;
        public AudioClip[] TearSFX = null!;
        public AudioClip KnifeImpactSFX = null!;
        public AudioClip KnifeWallPullSFX = null!;
        public AudioClip KnifeChargeSFX = null!;
        public GameObject KnifeMesh = null!;
        public GameObject RuneMesh = null!;
        public GameObject ThrownKnifePrefab = null!;
#pragma warning restore 0649

        //public NetworkVariable<bool> IsThrown = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        //public NetworkVariable<bool> IsPlaceholder = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public bool isThrown = false;
        public bool isPlaceholder = false;
        public YulemanKnifeBehaviorOld KnifePlaceholderScript = null!;
        public YulemanKnifeBehaviorOld ThrownKnifeScript = null!;

        // Constants
        Vector3 PositionOffsetStab = new Vector3(-0.2f, 0.26f, -0.02f);
        Vector3 PositionOffsetThrow = new Vector3(0.18f, 0.035f, -0.08f);
        Vector3 RotationOffsetStab = new Vector3(-30, -90, 90);
        Vector3 RotationOffsetThrow = new Vector3(30, 100, -90);
        const int knifeMask = 1084754248;

        // Variables
        bool isCharged;
        Coroutine? chargeCoroutine = null;
        RaycastHit[]? objectsHitByKnife;
        List<RaycastHit> objectsHitByKnifeList = new List<RaycastHit>();
        float timeAtLastDamageDealt;
        PlayerControllerB previousPlayerHeldBy = null!;

        // Config Variables
        float chargeTime = 1f;
        int knifeHitForce = 1;
        float throwForce = 1000f;

        public override void Update()
        {
            base.Update();
            if (isPlaceholder && playerHeldBy == null)
            {
                if (IsServerOrHost)
                {
                    NetworkObject.Despawn(true);
                }
            }
        }

        public override void EquipItem()
        {
            if (!IsOwner)
            {
                ChangeOwnershipServerRpc(playerHeldBy.actualClientId);
            }
            base.EquipItem();
            ThrowCancel();
        }

        public override void PocketItem()
        {
            if (isPlaceholder)
            {
                DespawnServerRpc();
            }

            base.PocketItem();
            ThrowCancel();
            HitKnife(cancel: true);
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            if (isPlaceholder)
            {
                if (!buttonDown)
                {
                    //RecallKnife();
                }
                return;
            }
            
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
                    Throw();
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

        void Throw()
        {
            logger.LogDebug("Throwing knife...");
            //playerThrownBy = playerHeldBy;
            //Vector3 throwDir = playerThrownBy.playerEye.transform.forward;
            Vector3 throwDir = playerHeldBy.playerEye.transform.forward;

            ThrowKnifeServerRpc(throwDir);
        }

        void ThrowCancel()
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

        // RPCs

        [ServerRpc(RequireOwnership = false)]
        public void DespawnServerRpc()
        {
            if (IsServerOrHost)
            {
                NetworkObject.Despawn(true);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangeOwnershipServerRpc(ulong clientId)
        {
            if (IsServerOrHost)
            {
                NetworkObject.ChangeOwnership(clientId);
                if (isThrown)
                {
                    KnifePlaceholderScript.NetworkObject.Despawn(true);
                    isThrown = false;
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void ThrowKnifeServerRpc(Vector3 throwDir)
        {
            if (IsServerOrHost)
            {
                ThrowKnifeClientRpc();
                ThrownYulemanKnife thrownKnife = Instantiate(ThrownKnifePrefab, transform.position, transform.rotation).GetComponent<ThrownYulemanKnife>();
                thrownKnife.gameObject.GetComponent<NetworkObject>().Spawn(true);
                //thrownKnife.placeHolderScript = this;
                thrownKnife.Throw(throwDir);
            }
        }

        [ClientRpc]
        public void ThrowKnifeClientRpc()
        {
            isPlaceholder = true;
            KnifeMesh.SetActive(false);
            //RuneMesh.SetActive(true);
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