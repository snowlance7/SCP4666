using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace SCP4666.Doll
{
    internal class SCP4666AttackAreaCollisionDetect : MonoBehaviour
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public SCP4666AI mainScript;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        private void OnTriggerStay(Collider other)
        {
            mainScript.OnCollideWithPlayer(other);
        }
    }
}
