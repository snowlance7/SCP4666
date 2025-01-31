using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Unity.Mathematics;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666
{
    public class ChildSackBehavior : PhysicsProp
    {
        public static bool localPlayerSizeChangedFromSack;

        // Configs
        bool makePlayersChildOnRevive = true;
        float minSize = 0.6f;
        float maxSize = 0.8f;
        bool usingRandomMode = false;

        public override void Start()
        {
            base.Start();
            usingRandomMode = configRandomSack.Value;
            makePlayersChildOnRevive = configMakePlayersChildOnRevive.Value;
            minSize = configChildMinSize.Value;
            maxSize = configChildMaxSize.Value;
        }

        public void Activate()
        {
            StartCoroutine(ActivateCoroutine());
        }

        public IEnumerator ActivateCoroutine()
        {
            LoggerInstance.LogDebug("In Activate()");
            yield return new WaitForSeconds(5f);

            if (playerHeldBy != null) { playerHeldBy.DropAllHeldItemsAndSync(); } // TODO: Log this

            if (usingRandomMode)
            {
                LoggerInstance.LogDebug("Using random mode for child sack");
                int playersRespawned = 0;
                foreach (var player in StartOfRound.Instance.allPlayerScripts)
                {
                    if (!player.isPlayerDead) { continue; }

                    if (playersRespawned == 0)
                    {
                        float size = UnityEngine.Random.Range(minSize, maxSize);
                        DoALotOfShitToRevivePlayerClientRpc(player.actualClientId, size);
                        playersRespawned++;
                        continue;
                    }

                    int num = UnityEngine.Random.Range(0, 2);
                    if (num == 0)
                    {
                        float size = UnityEngine.Random.Range(minSize, maxSize);
                        DoALotOfShitToRevivePlayerClientRpc(player.actualClientId, size);
                        playersRespawned++;
                    }
                    else
                    {
                        SpawnPresent();
                    }
                }
            }
            else
            {
                LoggerInstance.LogDebug("Reviving dead players");

                foreach (var player in StartOfRound.Instance.allPlayerScripts)
                {
                    if (!player.isPlayerDead) { continue; }

                    float size = UnityEngine.Random.Range(minSize, maxSize);
                    DoALotOfShitToRevivePlayerClientRpc(player.actualClientId, size);
                }
            }

            NetworkObject.Despawn(true);
        }

        public void SpawnPresent()
        {
            Item giftItem = StartOfRound.Instance.allItemsList.itemsList.Where(x => x.name == "GiftBox").FirstOrDefault();
            GiftBoxItem gift = GameObject.Instantiate(giftItem.spawnPrefab, transform.position, Quaternion.identity).GetComponentInChildren<GiftBoxItem>();
            gift.NetworkObject.Spawn();
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
                LoggerInstance.LogDebug("playerInital is dead, reviving them.");
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

            PlayerScript.thisPlayerBody.localScale = new Vector3(playerSize, playerSize, playerSize);
        }
    }
}