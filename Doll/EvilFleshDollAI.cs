using BepInEx.Logging;
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
        private static ManualLogSource logger = LoggerInstance;

        public static HashSet<EvilFleshDollAI> EvilFleshDolls = [];

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public NavMeshAgent agent;
        public Animator animator;
        public GameObject bombMesh;
        public AudioSource audioSource;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        public bool isBombDoll;

        PlayerControllerB? targetPlayer;

        const float AIIntervalTime = 0.2f;
        float timeSinceIntervalUpdate;
        private NavMeshPath path1 = new NavMeshPath();
        private Vector3 destination;

        public bool isInsideFactory;
        Vector3 mainEntranceInsidePosition;
        Vector3 mainEntranceOutsidePosition;

        Vector3 targetFloorPosition;

        bool landing;
        bool falling;
        bool jumping;

        bool inSpecialAnimation;

        bool isEnemyDead;
        Vector3 lastPosition;

        //int hashRun;

        bool clingingToPlayer;
        int bodyPartIndex;
        readonly Vector3[] clingOffsets =
        [
            new Vector3(0, 0, 0),   // 0 Head
            new Vector3(0, 0, 0),   // 1 Right Arm
            new Vector3(0, 0, 0),   // 2 Left Arm
            new Vector3(0, 0, 0),   // 3 Right Leg
            new Vector3(0, 0, 0),   // 4 Left Leg
            new Vector3(0, 0, 0),   // 5 Chest
            new Vector3(0, 0, 0),   // 6 Feet
            new Vector3(0, 0, 0),   // 7 Right Hip
            new Vector3(0, 0, 0),   // 8 Crotch
            new Vector3(0, 0, 0),   // 9 Left Shoulder
            new Vector3(0, 0, 0),   // 10 Right Shoulder
        ];

        public const int floorMask = 268437761;
        public const int biteDamage = 2;

        public const float distanceToJumpAtPlayer = 4f;
        const float jumpHeight = 2f;
        const float jumpDuration = 0.5f;

        public override void OnDestroy()
        {
            EvilFleshDolls.Remove(this);
            base.OnDestroy();
        }

        public void Start()
        {
            //hashRun = Animator.StringToHash("run");
            EvilFleshDolls.Add(this);

            mainEntranceInsidePosition = RoundManager.FindMainEntrancePosition(true, false);
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(true, true);

            falling = false;
            landing = false;
            animator.SetTrigger("fall");
            Lunge(2f, 0f, 0.5f);
        }

        public void Update()
        {
            if (inSpecialAnimation || !IsServer || clingingToPlayer || isEnemyDead || jumping) { return; }

            timeSinceIntervalUpdate += Time.deltaTime;

            if (timeSinceIntervalUpdate > AIIntervalTime)
            {
                timeSinceIntervalUpdate = 0f;
                DoAIInterval();
            }
        }

        private void OnHitGround()
        {
            logger.LogDebug("OnHitGround");
            jumping = false;
            falling = false;
            landing = false;
            inSpecialAnimation = false;

            agent.enabled = true;
            agent.Warp(transform.position);
            agent.ResetPath();
            agent.isStopped = false;
        }

        /*public void LateUpdate()
        {
            if (isEnemyDead) { return; }

            Vector3 delta = transform.position - lastPosition;
            float speed = delta.magnitude / Time.deltaTime;

            animator.SetBool(hashRun, speed > 0f);

            lastPosition = transform.position;
        }*/

        public void DoAIInterval()
        {
            /*if (SCP4666AI.Instance == null)
            {
                NetworkObject.Despawn(true);
                return;
            }*/

            targetPlayer = GetClosestPlayer();

            if (targetPlayer == null)
            {
                if (!SetDestinationToPosition(SCP4666AI.Instance.transform.position, true))
                {
                    SetDestinationToEntrance();
                    return;
                }
            }
            else
            {
                if (!SetDestinationToPosition(targetPlayer.transform.position, true))
                {
                    SetDestinationToEntrance();
                    return;
                }

                if (Vector3.Distance(transform.position, targetPlayer.transform.position) <= distanceToJumpAtPlayer)
                {
                    logger.LogDebug("Attempt jump at player");
                    agent.isStopped = true;
                    inSpecialAnimation = true;
                    DoAnimationClientRpc("jump");
                }
            }
        }

        void SetDestinationToEntrance()
        {
            if (agent == null || agent.enabled == false) { return; }
            if (isInsideFactory)
            {
                SetDestinationToPosition(mainEntranceInsidePosition);

                if (Vector3.Distance(transform.position, mainEntranceInsidePosition) < 1f)
                {
                    Teleport(mainEntranceOutsidePosition, false);
                }
            }
            else
            {
                SetDestinationToPosition(mainEntranceOutsidePosition);

                if (Vector3.Distance(transform.position, mainEntranceOutsidePosition) < 1f)
                {
                    Teleport(mainEntranceInsidePosition, true);
                }
            }
        }

        public void Teleport(Vector3 position, bool _isInsideFactory)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            if (IsServer) { agent.Warp(position); }
            transform.position = position;
            isInsideFactory = _isInsideFactory;
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
            return; // TODO: For testing, remove later
            if (!IsServer) { return; }
            targetPlayer = player;
            agent.isStopped = true;
            inSpecialAnimation = false;
            falling = false;
            landing = false;
            jumping = false;

            clingingToPlayer = true;
            bodyPartIndex = UnityEngine.Random.Range(0, 11);

            transform.SetParent(targetPlayer.bodyParts[bodyPartIndex], false);
            transform.localPosition = clingOffsets[bodyPartIndex];
            transform.rotation = Quaternion.LookRotation(-targetPlayer.bodyParts[bodyPartIndex].forward, Vector3.up);

            Debug.Log($"Doll clinging to body part {bodyPartIndex}");
        }

        internal void HitEnemyOnLocalClient()
        {
            if (isEnemyDead) { return; }
            HitEnemyServerRpc();
        }

        public bool SetDestinationToPosition(Vector3 position, bool checkForPath = false)
        {
            if (agent == null || agent.enabled == false) { return false; }
            if (checkForPath)
            {
                position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 1.75f);
                path1 = new NavMeshPath();
                if (!agent.CalculatePath(position, path1))
                {
                    return false;
                }
                if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 2.7f)) > 1.55f)
                {
                    return false;
                }
            }

            //moveTowardsDestination = true;
            destination = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, -1f);
            agent.SetDestination(destination);
            return true;
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
            StartCoroutine(LungeRoutine(distance, jumpHeight, duration));
        }

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
                    DoAnimationClientRpc("fall");
                    logger.LogDebug("finished fall animation");
                }
                if (t > 0.75f && !landing)
                {
                    landing = true;
                    DoAnimationClientRpc("land");
                    logger.LogDebug("finished land animation");
                }

                // Apply combined motion
                transform.position = horizontalPos + Vector3.up * height;

                yield return null;
            }

            jumping = false;
            OnHitGround();
        }


        public void JumpAtTargetPlayer() // Animation: Gets called after winding up jump
        {
            if (!IsServer) { return; }

            if (targetPlayer == null || targetPlayer.isInsideFactory != isInsideFactory)
            {
                DoAnimationClientRpc("reset");
                inSpecialAnimation = false;
                targetPlayer = null;
                return;
            }

            inSpecialAnimation = false;
            Lunge(distanceToJumpAtPlayer * 2, jumpHeight, jumpDuration);
        }

        public void FinishHangAnimation() // Animation: Gets called after attaching to player
        {
            if (!IsServer) { return; }

            if (isBombDoll)
            {
                // TODO
            }
            else
            {
                DoAnimationClientRpc("attack");
            }
        }

        public void BitePlayer() // Animation: Gets called when doll biting player animation finishes a cycle
        {
            if (!IsServer) { return; }

            targetPlayer!.DamagePlayer(biteDamage);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            animator.SetTrigger(animationName);
            logger.LogDebug("Playing animation: " + animationName);
        }

        [ServerRpc(RequireOwnership = false)]
        public void HitEnemyServerRpc()
        {
            if (!IsServer) { return; }

            if (clingingToPlayer)
            {
                clingingToPlayer = false;
                transform.SetParent(null);
                //rb.isKinematic = false;
                jumping = false;
                agent.isStopped = true;
                isEnemyDead = true;
                DoAnimationClientRpc("die");
                Lunge(0f, 0f, 1f); // TODO: Figure out if this works
            }
        }

        [ClientRpc]
        public void SetBombDollClientRpc(bool dollIsBomb)
        {
            isBombDoll = dollIsBomb;
            bombMesh.SetActive(dollIsBomb);
        }
    }
}