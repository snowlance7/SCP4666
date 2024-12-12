using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666
{
    internal class ChildSackBehavior : PhysicsProp
    {
        public override void Update()
        {
            base.Update();

            if (isInShipRoom)
            {
                StartOfRound.Instance.ReviveDeadPlayers();
                NetworkObject.Despawn(true);
            }
        }
    }
}
