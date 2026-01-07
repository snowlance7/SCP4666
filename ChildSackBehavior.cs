using Dawn;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static SCP4666.Plugin;
// TODO: make a config for disabling the player shrinking mechanic
namespace SCP4666
{
    public class ChildSackBehavior : PhysicsProp
    {
        public static List<ChildSackBehavior> Instances { get; private set; } = [];

        public static bool localPlayerSizeChangedFromSack;

        // Configs // TODO: Set up these configs in unity
        const float minSize = 0.6f;
        const float maxSize = 0.8f;
        const bool allowManualActivation = true;
        public const bool activateOnTeamWipe = true;
        const bool shrinkPlayerOnRevive = false;

        public override void Start()
        {
            base.Start();
            Instances.Add(this);
        }

        public override void OnDestroy()
        {
            Instances.Remove(this);
            base.OnDestroy();
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            if (!buttonDown || !allowManualActivation) { return; }

            playerHeldBy.DiscardHeldObject();
            ActivateServerRpc();
        }

        public void Activate(float delay = 0f)
        {
            if (!IsServer) { return; }

            IEnumerator ActivateCoroutine(float delay)
            {
                yield return null;
                yield return new WaitForSeconds(delay);

                int playersRespawned = 0;
                foreach (var player in StartOfRound.Instance.allPlayerScripts)
                {
                    if (!player.isPlayerDead || player.isPlayerControlled) { continue; }

                    if (playersRespawned == 0)
                    {
                        float size = UnityEngine.Random.Range(minSize, maxSize);
                        DoALotOfShitToRevivePlayerClientRpc(player.actualClientId, size);
                        playersRespawned++;
                        continue;
                    }

                    if (UnityEngine.Random.Range(0, 2) == 0)
                    {
                        float size = UnityEngine.Random.Range(minSize, maxSize);
                        DoALotOfShitToRevivePlayerClientRpc(player.actualClientId, size);
                        playersRespawned++;
                    }
                    else
                    {
                        Utils.SpawnItem(ItemKeys.Gift, transform.position);
                    }
                }

                NetworkObject.Despawn(true);
            }

            StartCoroutine(ActivateCoroutine(delay));
        }

        public static void OnPlayerDeath(PlayerControllerB player)
        {
            try
            {
                if (localPlayerSizeChangedFromSack)
                {
                    logger.LogDebug("Changing player size back to default size");
                    localPlayerSizeChangedFromSack = false;
                    NetworkHandlerSCP4666.Instance.ChangePlayerSizeServerRpc(player.actualClientId, 1f);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e);
                return;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void ActivateServerRpc()
        {
            if (!IsServer) { return; }
            Activate();
        }

        [ClientRpc]
        private void DoALotOfShitToRevivePlayerClientRpc(ulong clientId, float playerSize = 1f)
        {
            PlayerControllerB PlayerScript = PlayerFromId(clientId);
            DeadBodyInfo deadBodyInfo = PlayerScript.deadBody;
            PlayerScript.isInsideFactory = false;
            PlayerScript.isInElevator = true;
            PlayerScript.isInHangarShipRoom = true;

            PlayerScript.ResetPlayerBloodObjects(PlayerScript.isPlayerDead);
            PlayerScript.health = 5;
            PlayerScript.isClimbingLadder = false;
            PlayerScript.clampLooking = false;
            PlayerScript.inVehicleAnimation = false;
            PlayerScript.disableMoveInput = false;
            PlayerScript.disableLookInput = false;
            PlayerScript.disableInteract = false;
            PlayerScript.ResetZAndXRotation();
            PlayerScript.thisController.enabled = true;
            if (PlayerScript.isPlayerDead)
            {
                logger.LogDebug("playerInital is dead, reviving them.");
                PlayerScript.thisController.enabled = true;
                PlayerScript.isPlayerDead = false;
                PlayerScript.isPlayerControlled = true;
                PlayerScript.health = 5;
                PlayerScript.hasBeenCriticallyInjured = false;
                PlayerScript.criticallyInjured = false;
                PlayerScript.playerBodyAnimator.SetBool("Limp", value: false);
                //PlayerScript.TeleportPlayer(revivePositions[random.Next(revivePositions.Count)].position, false, 0f, false, true);
                PlayerScript.TeleportPlayer(transform.position, false, 0f, false, true);
                PlayerScript.parentedToElevatorLastFrame = false;
                PlayerScript.overrideGameOverSpectatePivot = null;
                StartOfRound.Instance.SetPlayerObjectExtrapolate(enable: false);
                PlayerScript.setPositionOfDeadPlayer = false;
                PlayerScript.DisablePlayerModel(PlayerScript.gameObject, enable: true, disableLocalArms: true);
                PlayerScript.helmetLight.enabled = false;
                PlayerScript.Crouch(crouch: false);
                if (PlayerScript.playerBodyAnimator != null)
                {
                    PlayerScript.playerBodyAnimator.SetBool("Limp", value: false);
                }
                PlayerScript.bleedingHeavily = true;
                PlayerScript.deadBody = null;
                PlayerScript.activatingItem = false;
                PlayerScript.twoHanded = false;
                PlayerScript.inShockingMinigame = false;
                PlayerScript.inSpecialInteractAnimation = false;
                PlayerScript.freeRotationInInteractAnimation = false;
                PlayerScript.disableSyncInAnimation = false;
                PlayerScript.inAnimationWithEnemy = null;
                PlayerScript.holdingWalkieTalkie = false;
                PlayerScript.speakingToWalkieTalkie = false;
                PlayerScript.isSinking = false;
                PlayerScript.isUnderwater = false;
                PlayerScript.sinkingValue = 0f;
                PlayerScript.statusEffectAudio.Stop();
                PlayerScript.DisableJetpackControlsLocally();
                PlayerScript.mapRadarDotAnimator.SetBool("dead", value: false);
                PlayerScript.hasBegunSpectating = false;
                PlayerScript.externalForceAutoFade = Vector3.zero;
                PlayerScript.hinderedMultiplier = 1f;
                PlayerScript.isMovementHindered = 0;
                PlayerScript.sourcesCausingSinking = 0;
                PlayerScript.reverbPreset = StartOfRound.Instance.shipReverb;

                SoundManager.Instance.earsRingingTimer = 0f;
                PlayerScript.voiceMuffledByEnemy = false;
                SoundManager.Instance.playerVoicePitchTargets[Array.IndexOf(StartOfRound.Instance.allPlayerScripts, PlayerScript)] = 1f;
                SoundManager.Instance.SetPlayerPitch(1f, Array.IndexOf(StartOfRound.Instance.allPlayerScripts, PlayerScript));

                if (PlayerScript.currentVoiceChatIngameSettings == null)
                {
                    StartOfRound.Instance.RefreshPlayerVoicePlaybackObjects();
                }

                if (PlayerScript.currentVoiceChatIngameSettings != null)
                {
                    if (PlayerScript.currentVoiceChatIngameSettings.voiceAudio == null)
                    {
                        PlayerScript.currentVoiceChatIngameSettings.InitializeComponents();
                    }

                    if (PlayerScript.currentVoiceChatIngameSettings.voiceAudio == null)
                    {
                        return;
                    }

                    PlayerScript.currentVoiceChatIngameSettings.voiceAudio.GetComponent<OccludeAudio>().overridingLowPass = false;
                }
            }

            if (GameNetworkManager.Instance.localPlayerController == PlayerScript)
            {
                PlayerScript.bleedingHeavily = false;
                PlayerScript.criticallyInjured = false;
                PlayerScript.health = 5;
                HUDManager.Instance.UpdateHealthUI(5, hurtPlayer: true);
                PlayerScript.playerBodyAnimator?.SetBool("Limp", false);
                PlayerScript.spectatedPlayerScript = null;
                StartOfRound.Instance.SetSpectateCameraToGameOverMode(false, PlayerScript);
                StartOfRound.Instance.SetPlayerObjectExtrapolate(false);
                HUDManager.Instance.audioListenerLowPass.enabled = false;
                HUDManager.Instance.gasHelmetAnimator.SetBool("gasEmitting", false);
                HUDManager.Instance.RemoveSpectateUI();
                HUDManager.Instance.gameOverAnimator.SetTrigger("revive");
                localPlayerSizeChangedFromSack = true;
            }

            StartOfRound.Instance.allPlayersDead = false;
            StartOfRound.Instance.livingPlayers++;
            StartOfRound.Instance.UpdatePlayerVoiceEffects();

            deadBodyInfo.DeactivateBody(false);

            if (!shrinkPlayerOnRevive) { return; }
            PlayerScript.thisPlayerBody.localScale = new Vector3(playerSize, playerSize, playerSize); // TODO: Look at how lega shrinks players in cursed scrap
            Utils.RebuildRig(PlayerScript);
        }
    }
}