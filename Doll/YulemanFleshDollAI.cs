using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine.AI;
using static SCP4666.Plugin;

namespace SCP4666.Doll
{
    internal class YulemanFleshDollAI : NetworkBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

        public NavMeshAgent agent;
    }
}
