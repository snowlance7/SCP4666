/*using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using static SCP4666.Plugin;

namespace SCP4666
{
    internal class SCP323Behavior : NetworkBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

        bool jumping;

        void Update()
        {
            if (!isHeld && parentObject == null && !jumping)
            {
                if (fallTime < 1f)
                {
                    reachedFloorTarget = false;
                    FallWithCurve();
                    if (base.transform.localPosition.y - targetFloorPosition.y < 0.05f && !hasHitGround)
                    {
                        PlayDropSFX();
                        OnHitGround();
                    }
                    return;
                }
                if (!reachedFloorTarget)
                {
                    if (!hasHitGround)
                    {
                        PlayDropSFX();
                        OnHitGround();
                    }
                    reachedFloorTarget = true;
                    if (floorYRot == -1)
                    {
                        base.transform.rotation = Quaternion.Euler(itemProperties.restingRotation.x, base.transform.eulerAngles.y, itemProperties.restingRotation.z);
                    }
                    else
                    {
                        base.transform.rotation = Quaternion.Euler(itemProperties.restingRotation.x, (float)(floorYRot + itemProperties.floorYOffset) + 90f, itemProperties.restingRotation.z);
                    }
                }
                //base.transform.localPosition = targetFloorPosition;
            }
            else if (isHeld || isHeldByEnemy)
            {
                reachedFloorTarget = false;
            }
        }

        public override void LateUpdate()
        {
            if (AttachedToWendigo != null)
            {
                transform.rotation = parentObject.rotation;
                transform.Rotate(rotOffsetWendigo);
                transform.position = parentObject.position;
                Vector3 positionOffset = posOffsetWendigo;
                positionOffset = parentObject.rotation * positionOffset;
                transform.position += positionOffset;
                return;
            }

            if (!jumping && !isHeld && playerHeldBy == null && playerHighestMadness != null && (playersHeldBy.Contains(playerHighestMadness) || playersMadness[playerHighestMadness] > playerHighestMadness.maxInsanityLevel / 2))
            {
                PlayerControllerB player = playerHighestMadness;
                turnCompass.LookAt(player.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), (playersMadness[player] / 2) * Time.deltaTime);

                Vector3 directionToPlayer = (player.transform.position - transform.position).normalized;

                if (!jumping && Vector3.Dot(transform.forward, directionToPlayer) > 0.9f && IsServerOrHost) // withing ~20 degrees
                {
                    // Can move
                    if (timeSinceJumpForward > 10f)
                    {
                        timeSinceJumpForward = 0f;

                        targetFloorPosition = transform.position + transform.forward * 1f;
                        targetFloorPosition = GetItemFloorPosition(targetFloorPosition);

                        LungeClientRpc(targetFloorPosition, 1f, 1f);
                    }

                    if (timeSinceInchForward > 5f)
                    {
                        timeSinceInchForward = 0f;

                        targetFloorPosition = transform.position + transform.forward * 0.5f;
                        targetFloorPosition = GetItemFloorPosition(targetFloorPosition);

                        LungeClientRpc(targetFloorPosition, 0f, 1f);
                    }
                }
            }

            base.LateUpdate();
        }

        public void Lunge(Vector3 targetPosition, float jumpHeight, float duration)
        {
            grabbable = false;
            jumping = true;
            StartCoroutine(LungeRoutine(targetPosition, jumpHeight, duration));
        }

        IEnumerator LungeRoutine(Vector3 targetPosition, float jumpHeight, float duration)
        {
            Vector3 start = transform.position;

            float elapsed = 0f;
            while (elapsed < duration && !isHeld)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Ease-out curve (starts fast, slows at targetFloorPosition)
                t = 1f - Mathf.Pow(1f - t, 3f);

                // Horizontal motion
                Vector3 horizontalPos = Vector3.Lerp(start, targetFloorPosition, t);

                // Vertical arc (parabola) — will be 0 if jumpHeight is 0
                float height = 4 * jumpHeight * t * (1 - t);

                // Apply combined motion
                transform.position = horizontalPos + Vector3.up * height;

                yield return null;
            }

            jumping = false;
            grabbable = true;
        }

        // RPCs

        [ClientRpc]
        void LungeClientRpc(Vector3 targetPosition, float jumpHeight, float duration)
        {
            Lunge(targetPosition, jumpHeight, duration);
        }
    }
}*/