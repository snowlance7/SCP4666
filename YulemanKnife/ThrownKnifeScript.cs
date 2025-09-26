using BepInEx.Logging;
using GameNetcodeStuff;
using System.Collections.Generic;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666.YulemanKnife
{
    internal class ThrownKnifeScript : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618
        public AudioSource KnifeAudio;
        public AudioClip[] TearSFX;
        public AudioClip KnifeImpactSFX;
        public AudioClip KnifeWallPullSFX;
        public Transform KnifeTip;
        public GameObject KnifeMesh;
#pragma warning restore CS8618

        Transform? knifeThrownBy;
        PlayerControllerB? playerThrownBy;

        public SimpleEvent KnifeReturnedEvent = new SimpleEvent();

        List<Collider> EntitiesHitByKnife = [];

        bool isThrown;
        bool returning;
        bool inWall;

        Vector3 postThrowPosition;
        float returnTime;

        // Constants
        public const float timeToReturn = 1f;
        public static float throwForce = 100f;
        const float maxThrowDistance = 400f;
        const float returnSpeed = 1f;

        public void OnEnable()
        {
            KnifeMesh.SetActive(true);
        }

        public void OnDisable()
        {
            KnifeMesh.SetActive(false);
            returning = false;
            isThrown = false;
            inWall = false;
            EntitiesHitByKnife.Clear();
        }

        public void Start()
        {
            enabled = false;
        }

        public void Update()
        {
            if (returning)
            {
                returnTime += Time.deltaTime * returnSpeed;

                returnTime = Mathf.Clamp01(returnTime);

                transform.position = Vector3.Lerp(postThrowPosition, knifeThrownBy!.position, returnTime);

                if (returnTime >= timeToReturn)
                {
                    KnifeReturnedEvent.Invoke();
                    enabled = false;
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
                else
                {
                    transform.position = nextPosition;  // Move object if no collision
                }
            }
        }

        public void ThrowKnife(PlayerControllerB _playerThrownBy, Transform _knifeThrownBy, Vector3 throwDirection)
        {
            playerThrownBy = _playerThrownBy;
            ThrowKnife(_knifeThrownBy, throwDirection);
        }

        public void ThrowKnife(Transform _knifeThrownBy, Vector3 throwDirection)
        {
            enabled = true;
            knifeThrownBy = _knifeThrownBy;
            transform.position = knifeThrownBy.position + transform.forward; // TODO: Test this
            transform.rotation = Quaternion.LookRotation(throwDirection, KnifeTip.up);
            postThrowPosition = GetKnifeEndPoint(); // TODO: Test this
            isThrown = true;
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

        void StopKnife(bool hitWall)
        {
            logger.LogDebug("Stopping knife");
            isThrown = false;
            EntitiesHitByKnife.Clear();

            if (hitWall)
            {
                inWall = true;
                transform.position = postThrowPosition;

                RoundManager.Instance.PlayAudibleNoise(transform.position);
                KnifeAudio.PlayOneShot(KnifeImpactSFX);
                WalkieTalkie.TransmitOneShotAudio(KnifeAudio, KnifeImpactSFX, 0.5f);
            }
            else
            {
                postThrowPosition = transform.position;
            }
        }

        public void CallKnife()
        {
            if (isThrown)
            {
                StopKnife(false);
            }
            if (inWall)
            {
                KnifeAudio.PlayOneShot(KnifeWallPullSFX);
                WalkieTalkie.TransmitOneShotAudio(KnifeAudio, KnifeWallPullSFX, 0.5f);
            }

            isThrown = false;
            returning = true;
        }

        public void OnTriggerStay(Collider other)
        {
            if (!isThrown) { return; }

            if (EntitiesHitByKnife.Contains(other) || !other.transform.TryGetComponent<IHittable>(out var iHit)) { return; }
            logger.LogDebug("Hit");

            if (other.gameObject.TryGetComponent(out PlayerControllerB player))
            {
                if (player == playerThrownBy)
                {
                    EntitiesHitByKnife.Add(other);
                    return;
                }

                bool hitSuccessful = iHit.Hit(YulemanKnifeBehavior.knifeHitForcePlayer, KnifeTip.transform.forward, playerThrownBy, playHitSFX: true, 5);
                if (!hitSuccessful) { logger.LogDebug("Hit unsuccessful"); return; }
            }
            else
            {
                if (SCP4666AI.Instance != null && other == SCP4666AI.Instance.collider)
                {
                    EntitiesHitByKnife.Add(other);
                    return;
                }

                bool hitSuccessful = iHit.Hit(YulemanKnifeBehavior.knifeHitForceEnemy, KnifeTip.transform.forward, playerThrownBy, playHitSFX: true, 5);
                if (!hitSuccessful) { logger.LogDebug("Hit unsuccessful"); return; }
            }

            /*if (playerThrownBy != null)
            {
                if (other.gameObject.TryGetComponent(out PlayerControllerB player)) { return; } // TODO: Test this
                bool hitSuccessful = iHit.Hit(YulemanKnifeBehavior.knifeHitForcePlayer, KnifeTip.transform.forward, playerThrownBy, playHitSFX: true, 5);
                if (!hitSuccessful) { logger.LogDebug("Hit unsuccessful"); return; }
            }
            else
            {
                if (!other.gameObject.TryGetComponent(out PlayerControllerB player)) { return; }
                if (localPlayer != player) { return; }
                player.DamagePlayer(YulemanKnifeBehavior.knifeHitForcePlayer, true, true, CauseOfDeath.Stabbing);
            }*/

            EntitiesHitByKnife.Add(other);
            RoundManager.PlayRandomClip(KnifeAudio, TearSFX);
        }
    }
}
