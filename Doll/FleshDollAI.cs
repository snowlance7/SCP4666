using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static SCP4666.Plugin;

namespace SCP4666.Doll
{
    internal class FleshDollAI : NetworkBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

        public NavMeshAgent agent;
        public Animator animator;

        const float AIIntervalTime = 0.2f;
        float timeSinceIntervalUpdate;

        public void Update()
        {
            timeSinceIntervalUpdate += Time.deltaTime;

            if (timeSinceIntervalUpdate > AIIntervalTime)
            {
                timeSinceIntervalUpdate = 0f;


            }
        }
    }
}
