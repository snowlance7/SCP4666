using BepInEx.Logging;
using Dawn.Utils;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TextCore.Text;
using static ES3Spreadsheet;
using static SCP4666.Plugin;

namespace SCP4666.Doll
{
    internal class EvilFleshDollAI : NetworkBehaviour // TODO: Need to test and fix
    {
        public static int DEBUG_bodyPartIndex = 0;

        public static HashSet<EvilFleshDollAI> Instances = [];


#pragma warning disable CS8618
        public SmartAgentNavigator nav;
        public Animator animator;
        public GameObject bombMesh;
        public AudioSource audioSource;
#pragma warning restore CS8618

        public SCP4666AI? yulemanThrownBy;

        public bool isBombDoll;

        PlayerControllerB? targetPlayer;

        const float AIIntervalTime = 0.2f;
        float timeSinceIntervalUpdate;
        private Vector3 destination;

        public bool isInsideFactory;
        Vector3 mainEntranceInsidePosition;
        Vector3 mainEntranceOutsidePosition;
        List<EntranceTeleport> entrances = [];

        Vector3 targetFloorPosition;

        bool landing;
        bool falling;
        bool jumping;

        bool inSpecialAnimation;

        bool isEnemyDead;

        bool bombTicking;

        bool damagingPlayer;

        //bool clingingToPlayer;
        int bodyPartIndex;
        Transform? parentObject;
        readonly Vector3[] clingPosOffsets =
        [
            new Vector3(0.1f, 0, 0.35f),   // 0 Head
            new Vector3(0, 0.3f, -0.15f),   // 1 Right Arm
            new Vector3(0, 0.3f, -0.2f),   // 2 Left Arm
            new Vector3(0.1f, 0.3f, -0.25f),   // 3 Right Leg
            new Vector3(0.1f, 0.2f, -0.2f),   // 4 Left Leg
            new Vector3(0, -0.2f, 0.3f),   // 5 Chest
            new Vector3(0.05f, 1.6f, -0.6f),   // 6 Feet -> Back Tank
            new Vector3(0, 0.5f, 0.3f),   // 7 Right Hip -> Right Back Leg
            new Vector3(0, 0.5f, 0.2f),   // 8 Crotch -> Left Back Leg
            new Vector3(-0.15f, 0.6f, 0.1f),   // 9 Left Shoulder
            new Vector3(0, 0.5f, 0.15f),   // 10 Right Shoulder
        ];
        readonly Vector3[] clingRotOffsets =
        [
            new Vector3(160, 0, 160),   // 0 Head
            new Vector3(0, 0, 160),   // 1 Right Arm
            new Vector3(0, 0, 200),   // 2 Left Arm
            new Vector3(200, 160, 0),   // 3 Right Leg
            new Vector3(200, 160, 0),   // 4 Left Leg
            new Vector3(180, 0, 180),   // 5 Chest
            new Vector3(10, 0, 0),   // 6 Feet -> Back Tank
            new Vector3(200, 0, 0),   // 7 Right Hip -> Right Back Leg
            new Vector3(180, 0, 0),   // 8 Crotch -> Left Back Leg
            new Vector3(180, -70, 0),   // 9 Left Shoulder
            new Vector3(180, 0, 0),   // 10 Right Shoulder
        ];

        public const int floorMask = 268437761;
        public const int biteDamage = 1;

        public const float distanceToJumpAtPlayer = 4f;
        const float jumpHeight = 2f;
        const float jumpDuration = 0.5f;
        const bool dollsTargetClosestPlayer = false;

        public override void OnDestroy()
        {
            Instances.Remove(this);
            base.OnDestroy();
        }

        public void Start()
        {
            StartOfRound.Instance.LocalPlayerDamagedEvent.AddListener(LocalPlayerDamaged);
            Instances.Add(this);

            mainEntranceInsidePosition = RoundManager.FindMainEntrancePosition(true, false);
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(true, true);
            entrances = GameObject.FindObjectsOfType<EntranceTeleport>(includeInactive: false).ToList();

            nav.DisableMovement(true);
            nav.SetAllValues(isOutside: yulemanThrownBy.isOutside);

            //falling = false;
            //landing = false;
            animator.SetTrigger("fall");
            Lunge(2f, 0f, 0.5f);
        }

        public void Update()
        {
            if (inSpecialAnimation || !IsServer || isEnemyDead || jumping) { return; }

            if (bombTicking)
            {
                if (!audioSource.isPlaying)
                {
                    bombTicking = false;
                    Landmine.SpawnExplosion(transform.position, true);
                    NetworkObject.Despawn(true);
                }
            }

            if (parentObject != null)
            {
                if (targetPlayer == null) { return; }
                isInsideFactory = targetPlayer.isInsideFactory;
                if (targetPlayer.isPlayerDead)
                {
                    parentObject = null;
                    DropDollClientRpc();
                }

                return;
            }

            timeSinceIntervalUpdate += Time.deltaTime;

            if (timeSinceIntervalUpdate > AIIntervalTime)
            {
                timeSinceIntervalUpdate = 0f;
                DoAIInterval();
            }
        }

        void LateUpdate()
        {
            if (parentObject != null)
            {
                base.transform.rotation = parentObject.rotation;
                base.transform.Rotate(clingRotOffsets[bodyPartIndex]);
                base.transform.position = parentObject.position;
                Vector3 positionOffset = clingPosOffsets[bodyPartIndex];
                positionOffset = parentObject.rotation * positionOffset;
                base.transform.position += positionOffset;
            }
        }

        private void OnHitGround()
        {
            logger.LogDebug("OnHitGround");
            ResetVariables();

            nav.agent.enabled = true;
            Vector3 pos = RoundManager.Instance.GetNavMeshPosition(transform.position);
            nav.agent.Warp(pos);
            nav.StopAgent();
            nav.DisableMovement(false);
        }

        public void DoAIInterval()
        {
            /*if (SCP4666AI.Instance == null)
            {
                NetworkObject.Despawn(true);
                return;
            }*/

            targetPlayer = dollsTargetClosestPlayer ? GetClosestPlayer() : yulemanThrownBy?.targetPlayer;

            if (targetPlayer == null || !SetDestinationToPosition(targetPlayer.transform.position))
            {
                if (yulemanThrownBy == null) { return; }
                if (!SetDestinationToPosition(yulemanThrownBy.transform.position))
                {
                    return;
                }
            }
            else
            {
                if (Vector3.Distance(transform.position, targetPlayer.transform.position) <= distanceToJumpAtPlayer)
                {
                    logger.LogDebug("Attempt jump at player");
                    nav.DisableMovement(true);
                    inSpecialAnimation = true;
                    LungeClientRpc(targetPlayer.actualClientId);
                }
            }
        }

        public void Teleport(Vector3 position, bool _isInsideFactory)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            if (IsServer) { nav.agent.Warp(position); }
            transform.position = position;
            isInsideFactory = _isInsideFactory;
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

        public Vector3 GetItemFloorPosition(Vector3 startPosition)
        {
            if (startPosition == Vector3.zero)
            {
                startPosition = base.transform.position + Vector3.up * 0.15f;
            }
            if (Physics.Raycast(startPosition, -Vector3.up, out var hitInfo, 80f, floorMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 pos = hitInfo.point + Vector3.up * 0.04f/* + itemProperties.verticalOffset * Vector3.up*/;
                return RoundManager.Instance.GetNavMeshPosition(pos);
            }
            return startPosition;
        }

        public PlayerControllerB? GetClosestPlayer()
        {
            float closestDistance = Mathf.Infinity;
            PlayerControllerB? closestPlayer = null;

            foreach (var player in StartOfRound.Instance.allPlayerScripts.ToList())
            {
                if (player == null || !player.isPlayerControlled) { continue; }
                //if (player.isHostPlayerObject) { continue; } // TODO: For testing remove later
                float distance = Vector3.Distance(transform.position, player.transform.position);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPlayer = player;
                }
            }

            return closestPlayer;
        }

        public void Lunge(float distance, float jumpHeight, float duration)
        {
            if (jumping) { return; }
            jumping = true;

            IEnumerator LungeRoutine(float distance, float jumpHeight, float duration)
            {
                targetFloorPosition = transform.position + transform.forward * distance;
                targetFloorPosition = GetItemFloorPosition(targetFloorPosition);

                Vector3 start = transform.position;

                float elapsed = 0f;
                while (elapsed < duration)
                {
                    if (!jumping)
                    {
                        yield break;
                    }

                    elapsed += Time.deltaTime;
                    float t = elapsed / duration;

                    // Ease-out curve (starts fast, slows at targetFloorPosition)
                    //t = 1f - Mathf.Pow(1f - t, 3f);

                    // Horizontal motion
                    Vector3 horizontalPos = Vector3.Lerp(start, targetFloorPosition, t);

                    // Vertical arc (parabola) — will be 0 if jumpHeight is 0
                    float height = 4 * jumpHeight * t * (1 - t);

                    logger.LogDebug("duration: " + t);

                    if (t > 0.5f && !falling)
                    {
                        falling = true;
                        animator.SetTrigger("fall");
                        logger.LogDebug("finished fall animation");
                    }
                    if (t > 0.75f && !landing)
                    {
                        landing = true;
                        animator.SetTrigger("land");
                        logger.LogDebug("finished land animation");
                    }

                    // Apply combined motion
                    transform.position = horizontalPos + Vector3.up * height;

                    yield return null;
                }

                jumping = false;
                OnHitGround();
            }

            StartCoroutine(LungeRoutine(distance, jumpHeight, duration));
        }

        /* bodyparts
         * 0 head
         * 1 right arm
         * 2 left arm
         * 3 right leg
         * 4 left leg
         * 5 chest
         * 6 feet
         * 7 right hip
         * 8 crotch
         * 9 left shoulder
         * 10 right shoulder */

        public void OnCollideWithPlayer(PlayerControllerB player)
        {
            //return; // TODO: For testing, remove later
            if (!IsServer) { return; }
            if (parentObject != null || isEnemyDead || inSpecialAnimation) { return; }
            targetPlayer = player;
            nav.DisableMovement(true);
            ResetVariables();

            bodyPartIndex = Utils.testing && Utils.isBeta ? DEBUG_bodyPartIndex : UnityEngine.Random.Range(0, 11);
            parentObject = targetPlayer.bodyParts[bodyPartIndex];

            ClingToPlayerClientRpc(targetPlayer.actualClientId, bodyPartIndex);

            logger.LogDebug($"Doll clinging to body part {bodyPartIndex}");
        }

        public void JumpAtTargetPlayer() // Animation: Gets called after winding up jump
        {
            //if (!IsServer) { return; }

            if (targetPlayer == null || targetPlayer.isInsideFactory != isInsideFactory)
            {
                animator.SetTrigger("reset");
                inSpecialAnimation = false;
                targetPlayer = null;
                return;
            }

            inSpecialAnimation = false;
            Lunge(distanceToJumpAtPlayer * 2, jumpHeight, jumpDuration);
        }

        public void BitePlayer() // Animation: Gets called when doll biting player animation finishes a cycle
        {
            if (targetPlayer == null) { return; }
            damagingPlayer = true;
            targetPlayer!.DamagePlayer(biteDamage, false);
            damagingPlayer = false;
            logger.LogDebug("Player bitten by doll");
        } // TODO: Test this on network

        internal void HitEnemyOnLocalClient()
        {
            if (isEnemyDead) { return; }
            logger.LogDebug("Doll hit on client");
            HitEnemyServerRpc();
        }

        public void LocalPlayerDamaged() // TODO: Test this
        {
            logger.LogDebug("Local player damaged");
            if (parentObject == null || targetPlayer == null || targetPlayer != localPlayer || damagingPlayer) { return; }
            HitEnemyOnLocalClient();
        }

        public void ResetVariables()
        {
            jumping = false;
            landing = false;
            falling = false;
            inSpecialAnimation = false;
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            animator.SetTrigger(animationName);
            logger.LogDebug("Playing animation: " + animationName);
        }

        [ServerRpc(RequireOwnership = false)]
        public void HitEnemyServerRpc() // TODO: test
        {
            if (!IsServer) { return; }

            nav.DisableMovement(true);
            KillDollClientRpc();
        }

        [ClientRpc]
        public void SetBombDollClientRpc()
        {
            isBombDoll = true;
            bombMesh.SetActive(true);
        }

        [ClientRpc]
        public void ClingToPlayerClientRpc(ulong clientId, int _bodyPartIndex)
        {
            targetPlayer = PlayerFromId(clientId);
            ResetVariables();

            bodyPartIndex = _bodyPartIndex;
            parentObject = targetPlayer.bodyParts[bodyPartIndex];

            if (isBombDoll)
            {
                animator.SetTrigger("hang");
                audioSource.Play();
                bombTicking = true;
            }
            else
            {
                animator.SetTrigger("hangBite");
            }
        }

        [ClientRpc]
        public void LungeClientRpc(ulong clientId)
        {
            targetPlayer = PlayerFromId(clientId);
            animator.SetTrigger("jump");
        }

        [ClientRpc]
        public void KillDollClientRpc()
        {
            animator.SetTrigger("die");
            parentObject = null;
            ResetVariables();
            isEnemyDead = true;
            targetPlayer = null;
            Teleport(GetItemFloorPosition(transform.position), isInsideFactory);
        }

        [ClientRpc]
        public void DropDollClientRpc()
        {
            animator.SetTrigger("reset");
            parentObject = null;
            ResetVariables();
            targetPlayer = null;
            Teleport(GetItemFloorPosition(transform.position), isInsideFactory);
        }
    }
}