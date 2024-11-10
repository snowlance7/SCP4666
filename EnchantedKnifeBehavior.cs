using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode.Components;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666
{
    internal class EnchantedKnifeBehavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable 0649
        public Animator itemAnimator = null!;
        public AudioSource ItemSFX = null!;
#pragma warning restore 0649

        public override void Start()
        {
            base.Start();
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);
        }
        // TODO: LOOK AT DARTBOARD SCRIPT TO FIGURE OUT HOW THIS WORKS
        public override void FallWithCurve() // TODO: Look at stun grenade script to figure out how this works, soccer ball script works too
        {
            base.FallWithCurve(); // TODO: Do something with this to make it be thrown and add a collider to the knife to detect if it hits an enemy or player
        }

        /*public Vector3 GetGrenadeThrowDestination()
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
        }*/
    }
}
