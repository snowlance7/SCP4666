using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace SCP4666.Doll
{
    public class SCP4666AttackAreaCollisionDetect : MonoBehaviour
    {
#pragma warning disable CS8618
        public SCP4666AI mainScript;
#pragma warning restore CS8618

        private void OnTriggerStay(Collider other)
        {
            mainScript.OnCollideWithPlayer(other);
        }
    }
}
