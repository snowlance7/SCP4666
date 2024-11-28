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
        bool callingKnife;
        bool despawning;
        int itemSlot = -1;

        public override void GrabItem()
        {
            base.GrabItem();
            itemSlot = playerHeldBy.currentItemSlot;
        }

        public override void DiscardItem()
        {
            if (despawning || callingKnife) { return; }
            Despawn();
        }

        public override void PocketItem()
        {
            if (despawning || callingKnife) { return; }
            Despawn();
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            if (KnifeScript == null) { Despawn(); return; }

            if (buttonDown && !callingKnife && playerHeldBy != null && !playerHeldBy.activatingItem)
            {
                playerHeldBy.activatingItem = true;
                callingKnife = true;
                KnifeScript.ReturnToPlayer();
            }
        }

        public void Despawn()
        {
            if (despawning) { return; }
            despawning = true;
            if (KnifeScript != null)
            {
                KnifeScript.RuneScript = null;
            }
            if (playerHeldBy != null && playerHeldBy == localPlayer)
            {
                try
                {
                    playerHeldBy.DespawnHeldObject();
                }
                catch
                {
                    NetworkObject.Despawn(true);
                }
                HUDManager.Instance.itemSlotIcons[itemSlot] = null;
            }
        }
    }
}
