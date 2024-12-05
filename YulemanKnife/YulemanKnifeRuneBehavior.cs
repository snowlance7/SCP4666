using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.UIElements.UIR;
using static SCP4666.Plugin;
using Steamworks.Data;

namespace SCP4666.YulemanKnife
{
    internal class YulemanKnifeRuneBehavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

        public YulemanKnifeBehavior KnifeScript = null!;
        float timeSpawned;

        public override void Update()
        {
            base.Update();
            timeSpawned += Time.deltaTime;
            if (timeSpawned > 1 && playerHeldBy == null)
            {
                NetworkObject.Despawn(true);
            }
        }

        public override void OnHitGround()
        {
            base.OnHitGround();
            NetworkObject.Despawn(true);
        }

        public override void ItemActivate(bool used, bool buttonDown = true) // Synced
        {
            base.ItemActivate(used, buttonDown);

            if (KnifeScript == null)
            {
                logger.LogDebug("KnifeScript is null!");
                playerHeldBy.DespawnHeldObject();
                return;
            }

            if (buttonDown && playerHeldBy != null)
            {
                KnifeScript.ReturnToPlayer();
                playerHeldBy.DespawnHeldObject();
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (KnifeScript != null)
            {
                KnifeScript.RuneScript = null;
            }
        }
    }
}
