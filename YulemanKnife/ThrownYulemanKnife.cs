using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666.YulemanKnife
{
    internal class ThrownYulemanKnife : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable 0649
        public Rigidbody rb = null!;
        public BoxCollider collider = null!;
        public Transform KnifeTip = null!;
        public GameObject YulemanKnifePrefab = null!;
#pragma warning restore 0649

        public YulemanKnifeBehaviorOld placeHolderScript = null!;
        float throwForce = 10f;

        public void Update()
        {
            if (rb.velocity.magnitude > 0.1f)
            {
                KnifeTip.LookAt(KnifeTip.transform.forward + rb.velocity);
            }
        }

        public void Throw(Vector3 direction)
        {
            rb.isKinematic = false;
            rb.AddForce(direction * throwForce);
        }

        public void OnCollisionEnter(Collision collision)
        {
            logger.LogDebug(collision.collider.tag);
        }
        
        public void SpawnYulemanKnife()
        {
            YulemanKnifeBehaviorOld YulemanKnifeScript = Instantiate(YulemanKnifePrefab, transform.position, transform.rotation).GetComponent<YulemanKnifeBehaviorOld>();
            YulemanKnifeScript.NetworkObject.Spawn(true);
            YulemanKnifeScript.fallTime = 1f;
            YulemanKnifeScript.isThrown = true;
            YulemanKnifeScript.KnifePlaceholderScript = placeHolderScript;
            placeHolderScript.ThrownKnifeScript = YulemanKnifeScript;
        }
    }
}
// .1 percent chance it goes to 10000 force and doesnt stick into the wall