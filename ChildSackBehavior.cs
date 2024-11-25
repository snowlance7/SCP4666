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
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable 0649
        public AudioSource ItemAudio = null!;
        public ScanNodeProperties ScanNode = null!;
#pragma warning restore 0649

        public override void Update()
        {
            base.Update();

            if (isInShipRoom && StartOfRound.Instance.allPlayersDead)
            {
                StartOfRound.Instance.ReviveDeadPlayers();
                NetworkObject.Despawn(true);
            }
        }
    }
}
