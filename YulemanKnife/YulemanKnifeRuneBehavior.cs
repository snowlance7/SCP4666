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

namespace SCP4666.YulemanKnife
{
    internal class YulemanKnifeRuneBehavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

        public YulemanKnifeBehavior KnifeScript = null!;

        public override void Update()
        {
            base.Update();
        }

        public override void DiscardItem()
        {
            KnifeScript.RuneScript = null;
            DestroyObjectInHand(playerHeldBy);
        }

        public override void PocketItem()
        {
            KnifeScript.RuneScript = null;
            DestroyObjectInHand(playerHeldBy);
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            if (buttonDown)
            {

            }
        }

        // RPCs

        [ServerRpc(RequireOwnership = false)]
        public void DespawnServerRpc()
        {
            if (IsServerOrHost)
            {
                NetworkObject.Despawn(true);
            }
        }
    }
}
