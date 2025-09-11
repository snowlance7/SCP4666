using BepInEx.Logging;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static SCP4666.Plugin;

namespace SCP4666
{
    public class FleshDollBehavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

        public NavMeshAgent agent;
        public Transform HoldItemPosition;
        public Animator itemAnimator;
        public AnimationCurve grenadeFallCurve;
        public AnimationCurve grenadeVerticalFallCurve;
        public AnimationCurve grenadeVerticalFallCurveNoBounce;

        public Vector3 destination;
        NavMeshPath navmeshPath = new NavMeshPath();

        const float AIIntervalTime = 0.2f;
        float timeSinceIntervalUpdate;

        GrabbableObject? heldItem;

        bool isThrown;
        bool landing;
        Ray grenadeThrowRay;
        RaycastHit grenadeHit;
        int stunGrenadeMask = 268437761;

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

                    if (heldItem != null)
                    {

                    }
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
            if (parentObject != null)
            {
                base.transform.rotation = parentObject.rotation;
                base.transform.Rotate(itemProperties.rotationOffset);
                base.transform.position = parentObject.position;
                Vector3 positionOffset = itemProperties.positionOffset;
                positionOffset = parentObject.rotation * positionOffset;
                base.transform.position += positionOffset;
            }
            if (rotateObject)
            {
                base.transform.Rotate(new Vector3(0f, Time.deltaTime * 60f, 0f), Space.World);
            }
            if (radarIcon != null)
            {
                radarIcon.position = base.transform.position;
            }

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

        /*public override void FallWithCurve() // Coin version
        {
            // Log initial state
            logIfDebug($"cFallWithCurve called. Start Position: {startFallingPosition}, Target Position: {targetFloorPosition}, Initial cfallTime: {fallTime}");

            float magnitude = (startFallingPosition - targetFloorPosition).magnitude;
            logIfDebug($"Calculated magnitude: {magnitude}");

            // Log rotation interpolation
            Quaternion targetRotation = Quaternion.Euler(itemProperties.restingRotation.x, base.transform.eulerAngles.y, itemProperties.restingRotation.z);
            base.transform.rotation = Quaternion.Lerp(base.transform.rotation, targetRotation, 14f * Time.deltaTime / magnitude);
            logIfDebug($"Updated rotation to: {base.transform.rotation.eulerAngles}");

            // Log position interpolation for primary fall
            base.transform.localPosition = Vector3.Lerp(startFallingPosition, targetFloorPosition, grenadeFallCurve.Evaluate(fallTime));
            logIfDebug($"Updated primary fall position to: {base.transform.localPosition}");

            // Conditional logging for vertical fall curve
            if (magnitude > 5f)
            {
                logIfDebug("Magnitude > 5, using grenadeVerticalFallCurveNoBounce.");
                base.transform.localPosition = Vector3.Lerp(
                    new Vector3(base.transform.localPosition.x, startFallingPosition.y, base.transform.localPosition.z),
                    new Vector3(base.transform.localPosition.x, targetFloorPosition.y, base.transform.localPosition.z),
                    grenadeVerticalFallCurveNoBounce.Evaluate(fallTime)
                );
            }
            else
            {
                logIfDebug("Magnitude <= 5, using grenadeVerticalFallCurve.");
                base.transform.localPosition = Vector3.Lerp(
                    new Vector3(base.transform.localPosition.x, startFallingPosition.y, base.transform.localPosition.z),
                    new Vector3(base.transform.localPosition.x, targetFloorPosition.y, base.transform.localPosition.z),
                    grenadeVerticalFallCurve.Evaluate(fallTime)
                );
            }

            // Log updated position and fallTime
            logIfDebug($"Updated local position after vertical fall: {base.transform.localPosition}");

            fallTime += Mathf.Abs(Time.deltaTime * 12f / magnitude);
            logIfDebug($"Updated cfallTime to: {fallTime}");
        }*/

        /*public Vector3 GetGrenadeThrowDestination(Transform ejectPoint, float _throwDistance = 10f)
        {
            Vector3 position = base.transform.position;
            grenadeThrowRay = new Ray(ejectPoint.position, ejectPoint.forward);

            // Adjusted throw distance
            if (!Physics.Raycast(grenadeThrowRay, out grenadeHit, _throwDistance, stunGrenadeMask, QueryTriggerInteraction.Ignore))
            {
                position = grenadeThrowRay.GetPoint(_throwDistance - 2f); // Adjust target point
            }
            else
            {
                position = grenadeThrowRay.GetPoint(grenadeHit.distance - 0.05f);
            }

            // Second raycast downward to find the ground
            grenadeThrowRay = new Ray(position, Vector3.down);
            if (Physics.Raycast(grenadeThrowRay, out grenadeHit, 30f, stunGrenadeMask, QueryTriggerInteraction.Ignore))
            {
                position = grenadeHit.point + Vector3.up * 0.05f;
            }
            else
            {
                position = grenadeThrowRay.GetPoint(30f);
            }

            // Add randomness
            position += new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f));

            return position;
        }*/

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
            itemAnimator.SetTrigger("carry");
        }
    }
}
