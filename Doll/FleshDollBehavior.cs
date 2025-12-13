using BepInEx.Logging;
using Dawn.Utils;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static SCP4666.Plugin;

namespace SCP4666
{
    public class FleshDollBehavior : PhysicsProp
    {
#pragma warning disable CS8618
        public SmartAgentNavigator nav;
        public Transform HoldItemPosition;
        public Collider collider;
        public Animator itemAnimator;

        public AnimationCurve grenadeFallCurve;
        public AnimationCurve grenadeVerticalFallCurveNoBounce;
        public AnimationCurve grenadeVerticalFallCurve;
#pragma warning restore CS8618

        public static HashSet<FleshDollBehavior> Instances = [];

        public Vector3 destination;

        const float AIIntervalTime = 0.2f;
        float timeSinceIntervalUpdate;

        GrabbableObject? heldObject;

        bool isThrown;
        bool landing;

        bool canMove => !isHeld && !isHeldByEnemy && reachedFloorTarget && fallTime >= 1f;

        bool isInsideFactory;
        List<EntranceTeleport> entrances = [];

        Ray grenadeThrowRay;
        RaycastHit grenadeHit;
        const int stunGrenadeMask = 268437761;

        Vector3 shipNode => StartOfRound.Instance.insideShipPositions[5].position;

        // Configs
        const bool canBeGrabbedWhenHoldingScrap = true;

        public override void Start()
        {
            base.Start();

            Instances.Add(this);
        }

        public override void OnDestroy()
        {
            Instances.Remove(this);
            base.OnDestroy();
        }

        public override void Update()
        {
            if (heldObject != null && heldObject.playerHeldBy != null && IsServer) // TODO: Need to handle player grabbing item back from doll, test this
            {
                DropItemClientRpc(transform.position);
                return;
            }
            if (playerHeldBy != null)
            {
                nav.SetAllValues(!playerHeldBy.isInsideFactory);
                //isInsideFactory = playerHeldBy.isInsideFactory;
            }
            if (StartOfRound.Instance.currentLevel.spawnEnemiesAndScrap)
            {
                //canMove = !isHeld && !isHeldByEnemy && reachedFloorTarget && fallTime >= 1f;
                /*if (fallTime >= 1f && !reachedFloorTarget)
                {
                    targetFloorPosition = base.transform.position;
                    destination = base.transform.position;
                    agent.enabled = true;
                }*/
            }
            if (!canMove/*isHeld || isHeldByEnemy || !reachedFloorTarget || fallTime < 1f || isInElevator*/)
            {
                base.Update();
            }
            else if (IsServer)
            {
                timeSinceIntervalUpdate += Time.deltaTime;
                if (timeSinceIntervalUpdate > AIIntervalTime)
                {
                    timeSinceIntervalUpdate = 0f;
                    DoAIInterval();
                }
            }
        }

        public void DoAIInterval()
        {
            if (heldObject != null)
            {
                nav.DisableMovement(!SetDestinationToPosition(shipNode));

                if (Vector3.Distance(transform.position, shipNode) < 1f)
                {
                    DropItemClientRpc(transform.position);
                }
            }
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
            if (isThrown && fallTime > 0.75 && !landing)
            {
                landing = true;
                itemAnimator.SetTrigger("land");
            }

            itemAnimator.SetBool("sit", isHeldByEnemy);
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

        public Vector3 GetGrenadeThrowDestination()
        {
            Vector3 position = base.transform.position;
            Debug.DrawRay(playerHeldBy.gameplayCamera.transform.position, playerHeldBy.gameplayCamera.transform.forward, Color.yellow, 15f);
            grenadeThrowRay = new Ray(playerHeldBy.gameplayCamera.transform.position, playerHeldBy.gameplayCamera.transform.forward);
            position = ((!Physics.Raycast(grenadeThrowRay, out grenadeHit, 12f, stunGrenadeMask, QueryTriggerInteraction.Ignore)) ? grenadeThrowRay.GetPoint(10f) : grenadeThrowRay.GetPoint(grenadeHit.distance - 0.05f));
            Debug.DrawRay(position, Vector3.down, Color.blue, 15f);
            grenadeThrowRay = new Ray(position, Vector3.down);
            if (Physics.Raycast(grenadeThrowRay, out grenadeHit, 30f, stunGrenadeMask, QueryTriggerInteraction.Ignore))
            {
                return grenadeHit.point + Vector3.up * 0.05f;
            }
            return grenadeThrowRay.GetPoint(30f);
        }

        public override void FallWithCurve()
        {
            float magnitude = (startFallingPosition - targetFloorPosition).magnitude;
            base.transform.rotation = Quaternion.Lerp(base.transform.rotation, Quaternion.Euler(itemProperties.restingRotation.x, base.transform.eulerAngles.y, itemProperties.restingRotation.z), 14f * Time.deltaTime / magnitude);
            base.transform.localPosition = Vector3.Lerp(startFallingPosition, targetFloorPosition, grenadeFallCurve.Evaluate(fallTime));
            if (magnitude > 5f)
            {
                base.transform.localPosition = Vector3.Lerp(new Vector3(base.transform.localPosition.x, startFallingPosition.y, base.transform.localPosition.z), new Vector3(base.transform.localPosition.x, targetFloorPosition.y, base.transform.localPosition.z), grenadeVerticalFallCurveNoBounce.Evaluate(fallTime));
            }
            else
            {
                base.transform.localPosition = Vector3.Lerp(new Vector3(base.transform.localPosition.x, startFallingPosition.y, base.transform.localPosition.z), new Vector3(base.transform.localPosition.x, targetFloorPosition.y, base.transform.localPosition.z), grenadeVerticalFallCurve.Evaluate(fallTime));
            }
            fallTime += Mathf.Abs(Time.deltaTime * 12f / magnitude);
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

        public override void OnHitGround()
        {
            logger.LogDebug("OnHitGround");

            try
            {
                if (IsServer && isThrown && StartOfRound.Instance.shipHasLanded)
                {
                    heldObject = GetClosestItem(1f);
                    if (heldObject == null) { logger.LogDebug("Cant find item to grab"); return; }
                    GrabItemClientRpc(heldObject.NetworkObject);
                }
            }
            finally
            {
                landing = false;
                isThrown = false;
            }
        }

        GrabbableObject? GetClosestItem(float maxDistance)
        {
            float closestDistance = maxDistance;
            GrabbableObject? closestItem = null;

            foreach (GrabbableObject item in GameObject.FindObjectsOfType<GrabbableObject>())
            {
                if (item == null || item == this || !item.grabbable || !item.grabbableToEnemies || isHeld || isHeldByEnemy) { continue; }
                logger.LogDebug(item.name);
                float distance = Vector3.Distance(transform.position, item.transform.position);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestItem = item;
                }
            }
            if (closestItem == null)
            {
                foreach (GrabbableObject item in Instances)
                {
                    logger.LogDebug(item.name);
                    if (item == null || item == this) { continue; }
                    float distance = Vector3.Distance(transform.position, item.transform.position);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestItem = item;
                    }
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
            //heldObject.GrabItemFromEnemy(null);
            heldObject.isHeldByEnemy = true;
            heldObject.EnablePhysics(false);
            HoarderBugAI.grabbableObjectsInMap.Remove(heldObject.gameObject);
            //HoarderBugAI.grabbableObjectsInMap.Remove(gameObject);
            grabbable = false;
            grabbableToEnemies = false;
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
            //HoarderBugAI.grabbableObjectsInMap.Add(gameObject);

            heldObject = null;
            grabbable = true;
            grabbableToEnemies = true;
            collider.enabled = true;
            itemAnimator.SetTrigger("idle"); // TODO: Maybe switch this out to him falling down toys story style?
        }
    }
}
