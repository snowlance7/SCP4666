using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace SCP4666.Doll
{
    internal class EvilFleshDollCollisionDetect : MonoBehaviour, IHittable
    {
        public EvilFleshDollAI mainScript;
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
