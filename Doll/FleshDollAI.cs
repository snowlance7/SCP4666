using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static SCP4666.Plugin;

namespace SCP4666.Doll
{
    internal class FleshDollAI : NetworkBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

        public NavMeshAgent agent;
        public Animator animator;
        public Rigidbody rb;

        PlayerControllerB? targetPlayer;

        const float AIIntervalTime = 0.2f;
        float timeSinceIntervalUpdate;
        private NavMeshPath path1 = new NavMeshPath();
        private Vector3 destination;

        bool isGrounded;

        const float force = 10f;

        public void Start()
        {

        }

        public void Update()
        {
            if (!isGrounded)
            {
                isGrounded = IsGrounded();
            }

            timeSinceIntervalUpdate += Time.deltaTime;

            if (timeSinceIntervalUpdate > AIIntervalTime)
            {
                timeSinceIntervalUpdate = 0f;


            }
        }

        public void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {

            }
        }

        bool IsGrounded()
        {
            return Physics.Raycast(transform.position, Vector3.down, 0.1f, 268437761);
        }

        public void JumpAtTargetPlayer()
        {

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
            if (Physics.Raycast(startPosition, -Vector3.up, out var hitInfo, 80f, 268437761, QueryTriggerInteraction.Ignore))
            {
                return hitInfo.point + Vector3.up * 0.04f/* + itemProperties.verticalOffset * Vector3.up*/;
            }
            return startPosition;
        }
    }
}
