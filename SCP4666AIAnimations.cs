using GameNetcodeStuff;
using SCP4666.Doll;
using SCP4666.YulemanKnife;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static SCP4666.Plugin;
using static SCP4666.Utils;

namespace SCP4666
{
    internal partial class SCP4666AI
    {
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

            logger.LogDebug("DollsToSpawn: " + dollsToSpawn);
            dollsToSpawn -= 1;
            EvilFleshDollAI doll = GameObject.Instantiate(evilDollPrefab, RightHandTransform.position, transform.rotation).GetComponent<EvilFleshDollAI>();
            doll.yulemanThrownBy = this;
            doll.NetworkObject.Spawn(destroyWithScene: true);
            DollInstances.Add(doll);
            doll.onDestroy.AddListener(OnDollDestroyed);

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
                        localPlayer.JumpToFearLevel(0.7f);
                        localPlayer.drunkness = 0.2f;
                    }
                    StartCoroutine(InjureLocalPlayerCoroutine());
                }
            }
        }

        public void DoSlashDamageAnimation() // Animation
        {
            logger.LogDebug("DoSlashDamageAnimation");
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
            logger.LogDebug("DoSlapDamageAnimation");
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
            logger.LogDebug("UnsetInSpecialAnimation");
            inSpecialAnimation = false;
        }

        public void CallKnifeBack() // Animation
        {
            logger.LogDebug("CallKnifeBack");
            inSpecialAnimation = true;
            thrownKnifeScript.CallKnife();
        }

        public void ThrowKnife() // Animation
        {
            logger.LogDebug("ThrowKnife");
            if (isThrowingKnife) { return; }

            isThrowingKnife = true;

            //inSpecialAnimation = false;

            Vector3 throwDirection = (targetPlayer.bodyParts[5].position - RightHandTransform.position).normalized;
            thrownKnifeScript.ThrowKnife(RightHandTransform, throwDirection);
        }

        public void MakeKnifeVisible() // Animation
        {
            logger.LogDebug("MakeKnifeVisible");
            if (isThrowingKnife) { return; }
            KnifeMesh.SetActive(true);
        }

        public void MakeKnifeInvisible() // Animation
        {
            logger.LogDebug("MakeKnifeInvisible");
            KnifeMesh.SetActive(false);
        }

        public void PlayRoarSFX() // Animation
        {
            logger.LogDebug("PlayRoarSFX");
            creatureVoice.PlayOneShot(RoarSFX);
        }

        public void PlayFootstepSFX() // Animation
        {
            creatureSFX.PlayOneShot(FootstepSFX);
        }

        public void GrabPlayer() // Animation
        {
            logger.LogDebug("GrabPlayer");
            if (inSpecialAnimationWithPlayer == null) { logger.LogError("inSpecialAnimationWithPlayer is null in GrabPlayer()"); return; }
            //inSpecialAnimation = true;
            inSpecialAnimationWithPlayer.transform.SetParent(RightHandTransform);
            isGrabbingPlayer = true;
            creatureVoice.PlayOneShot(LaughSFX);
            onPlayerGrabbed.Invoke(inSpecialAnimationWithPlayer);
        }

        public void PutPlayerInSack() // Animation
        {
            logger.LogDebug("PutPlayerInSack");
            isGrabbingPlayer = false;
            if (inSpecialAnimationWithPlayer == null) { logger.LogError("inSpecialAnimationWithPlayer is null in PutPlayerInSack()"); return; }
            targetPlayers.Remove(inSpecialAnimationWithPlayer);

            inSpecialAnimationWithPlayer.transform.SetParent(ChildSackTransform);
            isPlayerInSack = true;

            if (localPlayer == inSpecialAnimationWithPlayer)
            {
                //NetworkHandlerSCP4666.Instance.blackScreenOverlay.SetActive(true); // TODO: Temp, trying something new
                SpectateYuleman(true);
                StartOfRound.Instance.allowLocalPlayerDeath = false;
            }

            MakePlayerInvisible(inSpecialAnimationWithPlayer, true);
            MufflePlayer(inSpecialAnimationWithPlayer, true);
            inSpecialAnimationWithPlayer.playerCollider.gameObject.SetActive(false);
            logger.LogDebug(inSpecialAnimationWithPlayer.playerUsername + " in sack");
        }
    }
}
