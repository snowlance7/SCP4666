using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666.YulemanKnife
{
    internal class YulemanKnifeRuneBehavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

        public YulemanKnifeBehavior KnifeScript = null!;
        float timeSpawned;

        public override void Update()
        {
            base.Update();
            timeSpawned += Time.deltaTime;
            if (timeSpawned > 1 && playerHeldBy == null && IsServerOrHost)
            {
                NetworkObject.Despawn(true);
            }
        }

        public override void OnHitGround()
        {
            base.OnHitGround();
            NetworkObject.Despawn(true);
        }

        public override void ItemActivate(bool used, bool buttonDown = true) // Synced
        {
            base.ItemActivate(used, buttonDown);

            if (KnifeScript == null)
            {
                logger.LogDebug("KnifeScript is null!");
                playerHeldBy.DespawnHeldObject();
                return;
            }

            if (buttonDown && playerHeldBy != null/* && !KnifeScript.isThrown*/)
            {
                KnifeScript.ReturnToPlayer();
                playerHeldBy.DespawnHeldObject();
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (KnifeScript != null)
            {
                KnifeScript.RuneScript = null;
            }
        }
    }
}
