using BepInEx.Logging;
using Dawn.Utils;
using GameNetcodeStuff;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using static SCP4666.Plugin;
using static UnityEngine.VFX.VisualEffectControlTrackController;

namespace SCP4666
{
    public class FleshDollBehavior : PhysicsProp
    {
#pragma warning disable CS8618
        public SmartAgentNavigator nav;
        public Transform holdItemPosition;
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

        //bool canMove => !isHeld && !isHeldByEnemy && reachedFloorTarget && fallTime >= 1f;

        bool isOutside => nav.IsAgentOutside();
        bool isInsideFactory => !isOutside;
        List<EntranceTeleport> entrances = [];

        Ray grenadeThrowRay;
        RaycastHit grenadeHit;
        const int stunGrenadeMask = 268437761;

        Vector3 shipNode;

        PlayerControllerB? previousPlayerHeldBy;

        int hashSpeed;
        int hashCarrying;
        int hashSit;

        float currentSpeed;
        Vector3 lastPosition;

        public override void Start()
        {
            base.Start();
            Instances.Add(this);
            //nav.DisableMovement(true);

            hashSpeed = Animator.StringToHash("speed");
            hashCarrying = Animator.StringToHash("carrying");
            hashSit = Animator.StringToHash("sit");
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
                nav.StopAgent();
                DropItemClientRpc(transform.position);
            }
            if (playerHeldBy != null)
            {
                previousPlayerHeldBy = playerHeldBy;
                nav.SetAllValues(!playerHeldBy.isInsideFactory);
            }
            /*if (StartOfRound.Instance.currentLevel.spawnEnemiesAndScrap)
            {
                //agent.enabled = !isHeld && !isHeldByEnemy && reachedFloorTarget && fallTime >= 1f;
                //nav.DisableMovement(!canMove);
                if (fallTime >= 1f && !reachedFloorTarget)
                {
                    targetFloorPosition = base.transform.position;
                    destination = base.transform.position;
                    //nav.DisableMovement(false);
                }
            }*/
            if (isHeld || isHeldByEnemy || !reachedFloorTarget || fallTime < 1f || isInElevator)
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
            nav.agent.enabled = heldObject != null;
            if (heldObject == null) { return; }

            if (Vector3.Distance(transform.position, shipNode) < 1f)
            {
                nav.StopAgent();
                DropItemClientRpc(transform.position);
                return;
            }

            nav.DoPathingToDestination(shipNode);
        }

        public void RepositionAgent()
        {
            Vector3 pos = RoundManager.Instance.GetNavMeshPosition(transform.position, RoundManager.Instance.navHit);
            nav.agent.Warp(pos);
            if (previousPlayerHeldBy == null) return;
            nav.SetAllValues(!previousPlayerHeldBy.isInsideFactory);
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
                heldObject.transform.position = holdItemPosition.position;
            }
            if (isThrown && fallTime > 0.75 && !landing)
            {
                landing = true;
                itemAnimator.SetTrigger("land");
            }

            currentSpeed = (transform.position - lastPosition).magnitude / Time.deltaTime;
            lastPosition = transform.position;
            itemAnimator.SetFloat(hashSpeed, currentSpeed);

            itemAnimator.SetBool(hashSit, isHeldByEnemy);
            itemAnimator.SetBool(hashCarrying, heldObject != null);
        }

        public override void ItemActivate(bool used, bool buttonDown = true) // Synced
        {
            nav.SetAllValues(!playerHeldBy.isInsideFactory);

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
            grenadeThrowRay = new Ray(playerHeldBy.gameplayCamera.transform.position, playerHeldBy.gameplayCamera.transform.forward);
            position = ((!Physics.Raycast(grenadeThrowRay, out grenadeHit, 12f, stunGrenadeMask, QueryTriggerInteraction.Ignore)) ? grenadeThrowRay.GetPoint(10f) : grenadeThrowRay.GetPoint(grenadeHit.distance - 0.05f));
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

        public override void OnHitGround()
        {
            logger.LogDebug("OnHitGround");

            if (IsServer && isThrown && StartOfRound.Instance.shipHasLanded)
            {
                heldObject = GetClosestItem(1f);
                if (heldObject != null)
                {
                    shipNode = RoundManager.Instance.GetNavMeshPosition(StartOfRound.Instance.insideShipPositions[5].position, RoundManager.Instance.navHit);
                    //nav.DisableMovement(false);
                    GrabItemClientRpc(heldObject.NetworkObject);
                }
            }

            landing = false;
            isThrown = false;
        }

        GrabbableObject? GetClosestItem(float maxDistance)
        {
            float closestDistance = maxDistance;
            GrabbableObject? closestItem = null;

            foreach (GrabbableObject item in GameObject.FindObjectsOfType<GrabbableObject>())
            {
                if (item == null || item == this || !item.grabbable || !item.grabbableToEnemies || isHeld || isHeldByEnemy) { continue; }
                //logger.LogDebug(item.name);
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
                    //logger.LogDebug(item.name);
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

        /*public override void GrabItemFromEnemy(EnemyAI enemy)
        {
            base.GrabItemFromEnemy(enemy);
            if (heldObject != null && IsServer)
            {
                nav.StopAgent();
                DropItemClientRpc(transform.position);
            }
        }*/

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
            if (heldObject != null) return;
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Couldnt get netObj from NetworkObjectReference in GrabItemClientRpc"); return; }
            if (!netObj.TryGetComponent(out GrabbableObject grabObj)) { logger.LogError("Couldnt get GrabbableObject from NetworkObject in GrabItemClientRpc"); return; }

            heldObject = grabObj;
            heldObject.parentObject = holdItemPosition;
            heldObject.hasHitGround = false;
            heldObject.isHeldByEnemy = true;
            //heldObject.EnablePhysics(false);
            HoarderBugAI.grabbableObjectsInMap.Remove(heldObject.gameObject);
            HoarderBugAI.grabbableObjectsInMap.Remove(gameObject);
            grabbable = false;
            heldObject.grabbable = true;
            grabbableToEnemies = false;
            heldObject.grabbableToEnemies = false;
            collider.enabled = false;
        }

        [ClientRpc]
        public void DropItemClientRpc(Vector3 targetFloorPosition)
        {
            if (heldObject == null) return;
            GrabbableObject item = heldObject;
            item.parentObject = null;
            item.transform.SetParent(StartOfRound.Instance.propsContainer, worldPositionStays: true);
            //item.EnablePhysics(enable: true);
            item.fallTime = 0f;
            item.startFallingPosition = item.transform.parent.InverseTransformPoint(item.transform.position);
            item.targetFloorPosition = item.transform.parent.InverseTransformPoint(targetFloorPosition);
            item.floorYRot = -1;
            item.DiscardItemFromEnemy();
            item.isHeldByEnemy = false;
            item.grabbable = true;
            item.grabbableToEnemies = true;
            HoarderBugAI.grabbableObjectsInMap.Add(item.gameObject);
            HoarderBugAI.grabbableObjectsInMap.Add(gameObject);

            heldObject = null;
            grabbable = true;
            grabbableToEnemies = true;
            collider.enabled = true;
        }
    }
}
