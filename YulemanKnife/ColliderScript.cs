using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666.YulemanKnife
{
    internal class ColliderScript : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;
        public YulemanKnifeBehavior YulemanKnifeScript = null!;
        public Rigidbody rb = null!;

        public void OnCollisionEnter(Collision collision)
        {
            logger.LogDebug(collision.gameObject.name);
            logger.LogDebug(collision.gameObject.tag);
        }
    }
}
