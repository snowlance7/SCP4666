using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace SCP4666.Doll
{
    internal class EvilFleshDollCollisionDetect : MonoBehaviour, IHittable
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public EvilFleshDollAI mainScript;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        
        public float timeSinceCollision;

        public void Update()
        {
            timeSinceCollision += Time.deltaTime;
        }

        private void OnTriggerStay(Collider other)
        {
            if (!other.CompareTag("Player")) { return; }
            PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null || !player.isPlayerControlled) { return; }
            if (timeSinceCollision < 1f) { return; }
            timeSinceCollision = 0f;
            mainScript.OnCollideWithPlayer(player);
        }

        bool IHittable.Hit(int force, Vector3 hitDirection, PlayerControllerB playerWhoHit, bool playHitSFX, int hitID)
        {
            mainScript.HitEnemyOnLocalClient();
            return true;
        }
    }
}
