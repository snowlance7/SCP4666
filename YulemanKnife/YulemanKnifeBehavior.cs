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
        public AudioSource KnifeAudio;
        public AudioClip[] SliceSFX;
        public AudioClip[] TearSFX;
        public AudioClip KnifeChargeSFX;
        public Transform KnifeTip;
        public GameObject ThrowingKnifePrefab;
        public GameObject KnifeMesh;
        public GameObject RuneMesh;
        public ScanNodeProperties ScanNode;
#pragma warning restore 0649

        List<Collider> EntitiesHitByKnife = [];
        public ThrownKnifeScript? thrownKnifeScript;

        // Constants
        readonly Vector3 PositionOffsetStab = new Vector3(-0.2f, 0.26f, -0.02f);
        readonly Vector3 PositionOffsetThrow = new Vector3(0.18f, 0.035f, -0.08f);
        readonly Vector3 RotationOffsetStab = new Vector3(-30, -90, 90);
        readonly Vector3 RotationOffsetThrow = new Vector3(30, 100, -90);
        const int knifeMask = 1084754248;
        const int defaultExcludeMask = -2621449;

        // Variables
        bool isCharged;
        Coroutine? chargeCoroutine = null;
        RaycastHit[]? objectsHitByKnife;
        List<RaycastHit> objectsHitByKnifeList = new List<RaycastHit>();
        float timeAtLastDamageDealt;
        public PlayerControllerB previousPlayerHeldBy;

        bool callingKnife;
        public bool isThrown;
        Vector3 rotationOffset = new Vector3(-30f, -90f, 90f);
        Vector3 positionOffset = new Vector3(-0.2f, 0.26f, -0.02f);

        public override void Update()
        {
            base.Update();

            if (playerHeldBy != null)
            {
                previousPlayerHeldBy = playerHeldBy;
            }
        }

        public override void LateUpdate()
        {
            if (parentObject != null)
            {
                transform.rotation = parentObject.rotation;
                transform.Rotate(rotationOffset);
                transform.position = parentObject.position;
                Vector3 _positionOffset = positionOffset;
                _positionOffset = parentObject.rotation * _positionOffset;
                transform.position += _positionOffset;
            }
            if (radarIcon != null)
            {
                radarIcon.position = base.transform.position;
            }
        }

        public void ThrowKnife(Vector3 throwDirection)
        {
            ThrownKnifeScript.ThrowKnife(this, throwDirection);
            isThrown = true;
            if (playerHeldBy == null) { return; }
            MakeKnifeVisible(false);
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

            if (isThrown)
            {
                if (callingKnife) { return; }
                CallKnife();
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
                    //ChargeCancel();
                    ThrowKnife(playerHeldBy.playerEye.transform.forward);
                }
                else
                {
                    RoundManager.PlayRandomClip(KnifeAudio, SliceSFX);
                    HitKnife();
                }

                //isCharged = false;
                ChargeCancel();
            }
        }

        IEnumerator ChargeKnife()
        {
            yield return new WaitForSecondsRealtime(configChargeTime.Value);
            rotationOffset = RotationOffsetThrow;
            positionOffset = PositionOffsetThrow;
            KnifeAudio.PlayOneShot(KnifeChargeSFX, 1f);
            WalkieTalkie.TransmitOneShotAudio(KnifeAudio, KnifeChargeSFX, 1f);
            RoundManager.Instance.PlayAudibleNoise(playerHeldBy.transform.position, KnifeAudio.maxDistance, 0.5f, 0, playerHeldBy.isInHangarShipRoom);
            isCharged = true;
            log("Knife is charged");
        }

        void ChargeCancel()
        {
            if (chargeCoroutine != null)
            {
                StopCoroutine(chargeCoroutine);
                chargeCoroutine = null;
            }

            isCharged = false;
            rotationOffset = RotationOffsetStab;
            positionOffset = PositionOffsetStab;
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
            bool hasHitSomething = false;
            bool dealtDamage = false;
            int surfaceIndex = -1;
            bool hasTriggeredFirstHit = false;
            if (!cancel && Time.realtimeSinceStartup - timeAtLastDamageDealt > 0.43f)
            {
                previousPlayerHeldBy.twoHanded = false;
                objectsHitByKnife = Physics.SphereCastAll(previousPlayerHeldBy.gameplayCamera.transform.position + previousPlayerHeldBy.gameplayCamera.transform.right * 0.1f, 0.3f, previousPlayerHeldBy.gameplayCamera.transform.forward, 0.75f, knifeMask, QueryTriggerInteraction.Collide);
                objectsHitByKnifeList = objectsHitByKnife.OrderBy((x) => x.distance).ToList();
                List<EnemyAI> list = new List<EnemyAI>();
                for (int i = 0; i < objectsHitByKnifeList.Count; i++)
                {
                    string layerName = LayerMask.LayerToName(objectsHitByKnifeList[i].transform.gameObject.layer);
                    log("Hit " + layerName);
                    if (objectsHitByKnifeList[i].transform.gameObject.layer == 8 || objectsHitByKnifeList[i].transform.gameObject.layer == 11)
                    {
                        hasHitSomething = true;
                        string text = objectsHitByKnifeList[i].collider.gameObject.tag;
                        for (int j = 0; j < StartOfRound.Instance.footstepSurfaces.Length; j++)
                        {
                            if (StartOfRound.Instance.footstepSurfaces[j].surfaceTag == text)
                            {
                                surfaceIndex = j;
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
                        hasHitSomething = true;
                        Vector3 forward = previousPlayerHeldBy.gameplayCamera.transform.forward;
                        try
                        {
                            int hitForce = 1;
                            EnemyAICollisionDetect collision = objectsHitByKnifeList[i].transform.GetComponent<EnemyAICollisionDetect>();
                            if (collision != null)
                            {
                                if (collision.mainScript != null && !list.Contains(collision.mainScript))
                                {
                                    goto IL_02f2;
                                }
                                continue;
                            }
                            if (!(objectsHitByKnifeList[i].transform.GetComponent<PlayerControllerB>() != null))
                            {
                                hitForce = configKnifeHitForce.Value;
                                goto IL_02f2;
                            }
                            if (!hasTriggeredFirstHit)
                            {
                                hasTriggeredFirstHit = true;
                                goto IL_02f2;
                            }
                            goto end_IL_027b;
                        IL_02f2:
                            bool damageDealtSuccessfully = component.Hit(hitForce, forward, previousPlayerHeldBy, playHitSFX: true, 5);
                            if (damageDealtSuccessfully && collision != null)
                            {
                                list.Add(collision.mainScript);
                            }
                            if (!dealtDamage && damageDealtSuccessfully)
                            {
                                dealtDamage = true;
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
            if (hasHitSomething)
            {
                //RoundManager.PlayRandomClip(knifeAudio, hitSFX);
                FindObjectOfType<RoundManager>().PlayAudibleNoise(transform.position, 17f, 0.8f);
                if (!dealtDamage && surfaceIndex != -1)
                {
                    KnifeAudio.PlayOneShot(StartOfRound.Instance.footstepSurfaces[surfaceIndex].hitSurfaceSFX);
                    WalkieTalkie.TransmitOneShotAudio(KnifeAudio, StartOfRound.Instance.footstepSurfaces[surfaceIndex].hitSurfaceSFX);
                }
                HitShovelServerRpc(surfaceIndex);
            }
        }
        
        private void HitSurfaceWithKnife(int hitSurfaceID)
        {
            KnifeAudio.PlayOneShot(StartOfRound.Instance.footstepSurfaces[hitSurfaceID].hitSurfaceSFX);
            WalkieTalkie.TransmitOneShotAudio(KnifeAudio, StartOfRound.Instance.footstepSurfaces[hitSurfaceID].hitSurfaceSFX);
        }

        public void CallKnife()
        {
            if (thrownKnifeScript == null) { return; }
            callingKnife = true;
            thrownKnifeScript.CallKnife();
            StartCoroutine(CallKnifeCoroutine());
        }

        IEnumerator CallKnifeCoroutine()
        {
            yield return new WaitForSeconds(ThrownKnifeScript.timeToReturn);
            callingKnife = false;
            isThrown = false;
            if (playerHeldBy == null) { yield break; }
            MakeKnifeVisible(true);
        }

        public void MakeKnifeVisible(bool value) // TODO: Knife switching to rune is desynced for other clients
        {
            if (playerHeldBy != null)
            {
                playerHeldBy.activatingItem = !value;
            }
            isThrown = !value;
            KnifeMesh.SetActive(value);
            if (isHeldByEnemy) { return; }
            RuneMesh.SetActive(!value);
        }

        // RPCs

        [ServerRpc(RequireOwnership = false)]
        void HitShovelServerRpc(int hitSurfaceID)
        {
            HitShovelClientRpc(hitSurfaceID);
        }

        [ClientRpc]
        void HitShovelClientRpc(int hitSurfaceID)
        {
            //RoundManager.PlayRandomClip(KnifeAudio, hitSFX);
            if (hitSurfaceID != -1)
            {
                HitSurfaceWithKnife(hitSurfaceID);
            }
        }
    }
}