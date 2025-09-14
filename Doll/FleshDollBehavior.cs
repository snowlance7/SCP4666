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
        public Collider collider;

        public Vector3 destination;
        NavMeshPath path1 = new NavMeshPath();

        const float AIIntervalTime = 0.2f;
        float timeSinceIntervalUpdate;

        GrabbableObject? heldObject;

        bool isThrown;
        bool landing;

        bool isInsideFactory;
        Vector3 mainEntranceInsidePosition;
        Vector3 mainEntranceOutsidePosition;
        Vector3 shipNode = StartOfRound.Instance.insideShipPositions[5].position;

        public override void Update()
        {
            if (heldObject != null && heldObject.playerHeldBy != null && IsServer) // TODO: Need to handle player grabbing item back from doll, test this
            {
                DropItemClientRpc(transform.position);
                return;
            }
            if (playerHeldBy != null)
            {
                isInsideFactory = playerHeldBy.isInsideFactory;
            }
            if (StartOfRound.Instance.currentLevel.spawnEnemiesAndScrap)
            {
                agent.enabled = !isHeld && reachedFloorTarget && fallTime >= 1f;
                /*if (fallTime >= 1f && !reachedFloorTarget)
                {
                    targetFloorPosition = base.transform.position;
                    destination = base.transform.position;
                    agent.enabled = true;
                }*/
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

                    if (heldObject != null)
                    {
                        if (isInsideFactory)
                        {
                            if (!SetDestinationToPosition(mainEntranceInsidePosition, true))
                            {
                                logger.LogDebug("No path to main entrance position");
                                DropItemClientRpc(transform.position);
                                return;
                            }
                            if (Vector3.Distance(transform.position, mainEntranceInsidePosition) < 1f)
                            {
                                Teleport(mainEntranceOutsidePosition, false);
                                return;
                            }
                        }
                        else
                        {
                            if (!SetDestinationToPosition(shipNode, true) || Vector3.Distance(transform.position, shipNode) < 1f)
                            {
                                logger.LogDebug("No path to ship node from outside");
                                DropItemClientRpc(transform.position);
                                return;
                            }
                        }
                    }
                }
            }

            if (isThrown && fallTime > 0.75 && !landing)
            {
                landing = true;
                itemAnimator.SetTrigger("land");
            }
        }

        public void Teleport(Vector3 position, bool _isInsideFactory)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            if (IsServer) { agent.Warp(position); }
            transform.position = position;
            isInsideFactory = _isInsideFactory;
        }

        public override void LateUpdate()
        {
            if (parentObject != null)
            {
                base.transform.rotation = parentObject.rotation;
                base.transform.Rotate(itemProperties.rotationOffset);
                base.transform.position = parentObject.position;
                Vector3 positionOffset = itemProperties.positionOffset;
                positionOffset = parentObject.rotation * positionOffset;
                base.transform.position += positionOffset;
            }
            if (radarIcon != null)
            {
                radarIcon.position = base.transform.position;
            }
            if (heldObject != null)
            {
                heldObject.transform.position = HoldItemPosition.position;
            }

            itemAnimator.SetBool("sit", isHeld && playerHeldBy == null);
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

        public override void OnHitGround()
        {
            logger.LogDebug("OnHitGround");

            if (IsServer && isThrown && StartOfRound.Instance.shipHasLanded)
            {
                heldObject = GetClosestItem(1f);
                if (heldObject == null) { logger.LogDebug("Cant find item to grab"); return; }

                if (isInsideFactory)
                {
                    mainEntranceInsidePosition = RoundManager.FindMainEntrancePosition(getTeleportPosition: true, getOutsideEntrance: false);
                    if (!Utils.CalculatePath(transform.position, mainEntranceInsidePosition)) { logger.LogDebug("Cant find path to entrance"); return; }
                }
                else
                {
                    mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(getTeleportPosition: true, getOutsideEntrance: true);
                    if (!Utils.CalculatePath(transform.position, mainEntranceOutsidePosition)) { logger.LogDebug("Cant find path to entrance"); return; }
                }
                GrabItemClientRpc(heldObject.NetworkObject);
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

        public override void GrabItemFromEnemy(EnemyAI enemy)
        {
            base.GrabItemFromEnemy(enemy);
            if (heldObject != null && IsServer)
            {
                DropItemClientRpc(transform.position);
            }
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

            heldObject = grabObj;
            heldObject.parentObject = HoldItemPosition;
            heldObject.hasHitGround = false;
            heldObject.GrabItemFromEnemy(null);
            heldObject.isHeldByEnemy = true;
            heldObject.EnablePhysics(false);
            HoarderBugAI.grabbableObjectsInMap.Remove(heldObject.gameObject);
            grabbable = false;
            collider.enabled = false;
            itemAnimator.SetTrigger("carry");
        }

        [ClientRpc]
        public void DropItemClientRpc(Vector3 targetFloorPosition)
        {
            if (heldObject == null)
            {
                return;
            }
            GrabbableObject itemGrabbableObject = heldObject;
            itemGrabbableObject.parentObject = null;
            itemGrabbableObject.transform.SetParent(StartOfRound.Instance.propsContainer, worldPositionStays: true);
            itemGrabbableObject.EnablePhysics(enable: true);
            itemGrabbableObject.fallTime = 0f;
            itemGrabbableObject.startFallingPosition = itemGrabbableObject.transform.parent.InverseTransformPoint(itemGrabbableObject.transform.position);
            itemGrabbableObject.targetFloorPosition = itemGrabbableObject.transform.parent.InverseTransformPoint(targetFloorPosition);
            itemGrabbableObject.floorYRot = -1;
            itemGrabbableObject.DiscardItemFromEnemy();
            itemGrabbableObject.isHeldByEnemy = false;
            HoarderBugAI.grabbableObjectsInMap.Add(itemGrabbableObject.gameObject);

            
            heldObject = null;
            grabbable = true;
            collider.enabled = true;
            itemAnimator.SetTrigger("idle");
        }
    }
}
