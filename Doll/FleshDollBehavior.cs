using BepInEx.Logging;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static SCP4666.Plugin;

namespace SCP4666
{
    public class FleshDollBehavior : StunGrenadeItem
    {
        private static ManualLogSource logger = LoggerInstance;

        public NavMeshAgent agent;
        public Transform HoldItemPosition;

        public Vector3 destination;
        NavMeshPath navmeshPath = new NavMeshPath();

        const float AIIntervalTime = 0.2f;
        float timeSinceIntervalUpdate;

        GrabbableObject? heldItem;

        bool isThrown;
        bool landing;

        public override void GrabItem()
        {
            base.GrabItem();
        }

        public override void DiscardItem()
        {
            base.DiscardItem();
        }

        public override void Update()
        {
            /*if (IsServer && !isHeld && !base.IsOwner)
            {
                GetComponent<NetworkObject>().RemoveOwnership();
            }*/
            if (StartOfRound.Instance.currentLevel.spawnEnemiesAndScrap)
            {
                agent.enabled = !isHeld && reachedFloorTarget && fallTime >= 1f;
                if (fallTime >= 1f && !reachedFloorTarget)
                {
                    targetFloorPosition = base.transform.position;
                    destination = base.transform.position;
                    agent.enabled = true;
                }
            }
            if (isHeld || !reachedFloorTarget || fallTime < 1f || isInElevator)
            {
                base.Update();
            }
            else if (IsServer)
            {
                timeSinceIntervalUpdate += Time.deltaTime;
                if (timeSinceIntervalUpdate > AIIntervalTime)
                {
                    timeSinceIntervalUpdate = 0f;


                }
            }

            if (isThrown && fallTime > 0.75 && !landing)
            {
                landing = true;
                itemAnimator.SetTrigger("land");
            }
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (heldItem != null)
            {
                heldItem.transform.position = HoldItemPosition.position;
            }
        }

        public void SetDestinationToPosition(Vector3 position, bool checkForPath = false)
        {
            if (checkForPath)
            {
                position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 1.75f);
                if (!agent.CalculatePath(position, navmeshPath))
                {
                    Debug.Log(base.gameObject.name + " calculatepath returned false.");
                    return;
                }
                if (Vector3.Distance(navmeshPath.corners[navmeshPath.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 2.7f)) > 1.55f)
                {
                    Debug.Log(base.gameObject.name + " path calculation went wrong.");
                    return;
                }
            }
            destination = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, -1f);
            agent.SetDestination(destination);
        }

        public override void ItemActivate(bool used, bool buttonDown = true) // Synced
        {
            if (IsOwner)
            {
                playerHeldBy.DiscardHeldObject(placeObject: true, null, GetGrenadeThrowDestination());
            }

            itemAnimator.SetTrigger("fall");
            isThrown = true;
        }

        public override void OnHitGround()
        {
            if (IsServer && isThrown)
            {
                heldItem = GetClosestItem(1f);
                if (heldItem == null) { return; }

                GrabItemClientRpc(heldItem.NetworkObject);
            }

            isThrown = false;
            landing = false;
        }

        GrabbableObject? GetClosestItem(float maxDistance)
        {
            HoarderBugAI.RefreshGrabbableObjectsInMapList();

            float closestDistance = maxDistance;
            GrabbableObject? closestItem = null;

            foreach (var item in HoarderBugAI.grabbableObjectsInMap.ToList())
            {
                if (item == null) { continue; }
                float distance = Vector3.Distance(transform.position, item.transform.position);

                if (distance < closestDistance && item.TryGetComponent(out GrabbableObject grabObj))
                {
                    closestDistance = distance;
                    closestItem = grabObj;
                }
            }

            return closestItem;
        }

        [ServerRpc(RequireOwnership = false)]
        public void DoAnimationServerRpc(string animationName)
        {
            if (!IsServer) { return; }
            DoAnimationClientRpc(animationName);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            itemAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void GrabItemClientRpc(NetworkObjectReference netRef)
        {
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Couldnt get netObj from NetworkObjectReference in GrabItemClientRpc"); return; }
            if (!netObj.TryGetComponent(out GrabbableObject grabObj)) { logger.LogError("Couldnt get GrabbableObject from NetworkObject in GrabItemClientRpc"); return; }

            heldItem = grabObj;
            heldItem.parentObject = HoldItemPosition;
            heldItem.hasHitGround = false;
            heldItem.isHeldByEnemy = true;
            heldItem.EnablePhysics(false);
            HoarderBugAI.grabbableObjectsInMap.Remove(heldItem.gameObject);
            grabbable = false;
        }
    }
}
