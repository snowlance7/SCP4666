using BepInEx.Logging;
using GameNetcodeStuff;
using System;
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

        public NavMeshAgent agent;
        public Animator animator;
        public Rigidbody rb;
        public GameObject bombMesh;
        public AudioSource audioSource;

        public bool isBombDoll;

        PlayerControllerB? targetPlayer;

        const float AIIntervalTime = 0.2f;
        float timeSinceIntervalUpdate;
        private NavMeshPath path1 = new NavMeshPath();
        private Vector3 destination;

        public bool isInsideFactory;
        Vector3 mainEntranceInsidePosition;
        Vector3 mainEntranceOutsidePosition;

        bool isGrounded;
        bool landing;
        bool falling;

        bool inSpecialAnimation;

        bool isEnemyDead;
        Vector3 lastPosition;

        int hashRun;

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

        public const float force = 10f;
        public const int floorMask = 268437761;
        public const float distanceToJumpAtPlayer = 2f;
        public const int biteDamage = 2;

        public override void OnDestroy()
        {
            EvilFleshDolls.Remove(this);
            base.OnDestroy();
        }

        public void Start()
        {
            hashRun = Animator.StringToHash("run");
            EvilFleshDolls.Add(this);

            mainEntranceInsidePosition = RoundManager.FindMainEntrancePosition(true, false);
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(true, true);

            rb.isKinematic = false;
            rb.AddForce(transform.forward * force);
            animator.SetTrigger("fall");
        }

        public void Update()
        {
            if (inSpecialAnimation || !IsServer || clingingToPlayer) { return; }

            if (isEnemyDead)
            {
                if (!isGrounded)
                {
                    if (IsCloseToGround(0.04f))
                    {
                        isGrounded = true;
                        rb.isKinematic = true;
                    }
                }

                return;
            }

            if (!isGrounded)
            {
                if (rb.velocity.y > 0.1f) { return; }
                else if (!falling && rb.velocity.y < -0.1f)
                {
                    falling = true;
                    DoAnimationClientRpc("fall");
                }

                if (!landing && IsCloseToGround(1f))
                {
                    landing = true;
                    DoAnimationClientRpc("land");
                }

                if (IsCloseToGround(0.04f))
                {
                    isGrounded = true;
                    rb.isKinematic = true;
                    agent.enabled = true;
                    landing = false;
                    falling = false;
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

        public void LateUpdate()
        {
            if (isEnemyDead) { return; }

            Vector3 delta = transform.position - lastPosition;
            float speed = delta.magnitude / Time.deltaTime;

            animator.SetBool(hashRun, speed > 0f);

            lastPosition = transform.position;
        }

        public void DoAIInterval()
        {
            if (SCP4666AI.Instance == null)
            {
                NetworkObject.Despawn(true);
                return;
            }

            targetPlayer = GetClosestPlayer();

            if (targetPlayer == null)
            {
                agent.enabled = false;
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
                    agent.enabled = false;
                    inSpecialAnimation = true;
                    DoAnimationClientRpc("jump");
                }
            }
        }

        void SetDestinationToEntrance()
        {
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
            if (!IsServer) { return; }
            targetPlayer = player;
            agent.enabled = false;
            inSpecialAnimation = false;
            falling = false;
            landing = false;
            isGrounded = false;

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

        bool IsCloseToGround(float distance)
        {
            return Physics.Raycast(transform.position, Vector3.down, distance, floorMask);
        }

        public bool SetDestinationToPosition(Vector3 position, bool checkForPath = false)
        {
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

        public Vector3 GetItemFloorPosition(Vector3 startPosition = default(Vector3))
        {
            if (startPosition == Vector3.zero)
            {
                startPosition = base.transform.position + Vector3.up * 0.15f;
            }
            if (Physics.Raycast(startPosition, -Vector3.up, out var hitInfo, 80f, floorMask, QueryTriggerInteraction.Ignore))
            {
                return hitInfo.point + Vector3.up * 0.04f/* + itemProperties.verticalOffset * Vector3.up*/;
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

        public void JumpAtTargetPlayer() // Animation
        {
            if (!IsServer) { return; }

            if (targetPlayer == null || targetPlayer.isInsideFactory != isInsideFactory)
            {
                DoAnimationClientRpc("reset");
                inSpecialAnimation = false;
                targetPlayer = null;
                return;
            }

            Vector3 direction = (targetPlayer.gameplayCamera.transform.position - transform.position).normalized;

            rb.isKinematic = false;
            rb.AddForce(direction * force);
            inSpecialAnimation = false;
            isGrounded = false;
        }

        public void FinishHangAnimation() // Animation
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

        public void BitePlayer() // Animation
        {
            if (!IsServer) { return; }

            targetPlayer!.DamagePlayer(biteDamage);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            animator.SetTrigger(animationName);
        }

        [ServerRpc(RequireOwnership = false)]
        public void HitEnemyServerRpc()
        {
            if (!IsServer) { return; }

            if (clingingToPlayer)
            {
                clingingToPlayer = false;
                transform.SetParent(null);
                rb.isKinematic = false;
                isGrounded = false;
                agent.enabled = false;
                isEnemyDead = true;
                DoAnimationClientRpc("die");
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
