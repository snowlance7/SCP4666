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

#pragma warning disable 0649
        public AudioSource KnifeAudio;
        public AudioClip[] TearSFX;
        public AudioClip KnifeImpactSFX;
        public AudioClip KnifeWallPullSFX;
        public Transform KnifeTip;
#pragma warning restore 0649

        public YulemanKnifeBehavior knifeScript;

        List<Collider> EntitiesHitByKnife = [];

        bool isThrown;
        bool returning;
        bool inWall;

        Vector3 postThrowPosition;
        float returnTime;

        // Constants
        public const float timeToReturn = 1f;
        public static float throwForce = 10f;
        const float maxThrowDistance = 400f;
        const float returnSpeed = 1f;

        public static void ThrowKnife(YulemanKnifeBehavior _knifeScript, Vector3 throwDirection)
        {
            ThrownKnifeScript throwingKnife = GameObject.Instantiate(_knifeScript.ThrowingKnifePrefab, _knifeScript.transform.position, Quaternion.identity).GetComponent<ThrownKnifeScript>();
            throwingKnife.knifeScript = _knifeScript;
            _knifeScript.thrownKnifeScript = throwingKnife;
            throwingKnife.ThrowKnife(throwDirection);
        }

        public void Update()
        {
            if (returning)
            {
                returnTime += Time.deltaTime * returnSpeed;

                returnTime = Mathf.Clamp01(returnTime);

                transform.position = Vector3.Lerp(postThrowPosition, knifeScript.transform.position, returnTime);

                if (returnTime >= timeToReturn)
                {
                    knifeScript.thrownKnifeScript = null;
                    Destroy(this);
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

        void ThrowKnife(Vector3 throwDirection)
        {
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

            /*if (isThrown)
            {
                isThrown = false;
                return;
            }*/

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
                isThrown = false;
                StopKnife(false);
            }
            else
            {
                KnifeAudio.PlayOneShot(KnifeWallPullSFX);
                WalkieTalkie.TransmitOneShotAudio(KnifeAudio, KnifeWallPullSFX, 0.5f);
            }

            returning = true;
        }

        public void OnTriggerEnter(Collider other)
        {
            if (isThrown)
            {
                logger.LogDebug("Hit " + other.gameObject.name);
                if (!other.transform.TryGetComponent<IHittable>(out var iHit) || EntitiesHitByKnife.Contains(other)) { return; }
                logger.LogDebug("Hit");
                Vector3 forward = KnifeTip.transform.forward;
                if (knifeScript.playerHeldBy != null)
                {
                    bool hitSuccessful = iHit.Hit(configKnifeHitForce.Value, forward, knifeScript.previousPlayerHeldBy, playHitSFX: true, 5);
                    if (!hitSuccessful) { logger.LogDebug("Hit unsuccessful"); return; }
                }
                else
                {
                    if (!other.TryGetComponent<PlayerControllerB>(out PlayerControllerB player)) { return; }
                    if (localPlayer != player) { return; }
                    player.DamagePlayer(configKnifeHitForceYuleman.Value, true, true, CauseOfDeath.Stabbing);
                }
                EntitiesHitByKnife.Add(other);
                RoundManager.PlayRandomClip(KnifeAudio, TearSFX);
            }
        }
    }
}
