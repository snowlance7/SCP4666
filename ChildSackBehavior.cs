using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
                        RevivePlayerClientRpc(player.actualClientId);
                        playersRespawned++;
                        continue;
                    }

                    int num = UnityEngine.Random.Range(0, 2);
                    if (num == 0)
                    {
                        RevivePlayerClientRpc(player.actualClientId);
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

                    RevivePlayerClientRpc(player.actualClientId);
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
        public void RevivePlayerClientRpc(ulong clientId, float playerSize = 1f)
        {
            PlayerControllerB player = PlayerFromId(clientId);
            DeadBodyInfo deadBodyInfo = player.deadBody;
            player.isInsideFactory = false;
            player.isInElevator = true;
            player.isInHangarShipRoom = true;
            player.ResetPlayerBloodObjects(player.isPlayerDead);
            player.health = 5;
            player.isClimbingLadder = false;
            player.clampLooking = false;
            player.inVehicleAnimation = false;
            player.disableMoveInput = false;
            player.disableLookInput = false;
            player.disableInteract = false;
            player.ResetZAndXRotation();
            player.thisController.enabled = true;
            if (player.isPlayerDead)
            {
                //Plugin.ExtendedLogging("playerInital is dead, reviving them.");
                player.thisController.enabled = true;
                player.isPlayerDead = false;
                player.isPlayerControlled = true;
                player.health = 5;
                player.hasBeenCriticallyInjured = false;
                player.criticallyInjured = false;
                player.playerBodyAnimator.SetBool("Limp", value: false);
                //PlayerScript.TeleportPlayer(revivePositions[random.Next(revivePositions.Count)].position);
                player.TeleportPlayer(StartOfRound.Instance.GetPlayerSpawnPosition((int)clientId));
                player.parentedToElevatorLastFrame = false;
                player.overrideGameOverSpectatePivot = null;
                StartOfRound.Instance.SetPlayerObjectExtrapolate(enable: false);
                player.setPositionOfDeadPlayer = false;
                player.DisablePlayerModel(player.gameObject, enable: true, disableLocalArms: true);
                player.helmetLight.enabled = false;
                player.Crouch(crouch: false);
                if (player.playerBodyAnimator != null)
                {
                    player.playerBodyAnimator.SetBool("Limp", value: false);
                }
                player.bleedingHeavily = true;
                player.deadBody = null;
                player.activatingItem = false;
                player.twoHanded = false;
                player.inShockingMinigame = false;
                player.inSpecialInteractAnimation = false;
                player.freeRotationInInteractAnimation = false;
                player.disableSyncInAnimation = false;
                player.inAnimationWithEnemy = null;
                player.holdingWalkieTalkie = false;
                player.speakingToWalkieTalkie = false;
                player.isSinking = false;
                player.isUnderwater = false;
                player.sinkingValue = 0f;
                player.statusEffectAudio.Stop();
                player.DisableJetpackControlsLocally();
                player.mapRadarDotAnimator.SetBool("dead", value: false);
                player.hasBegunSpectating = false;
                player.externalForceAutoFade = Vector3.zero;
                player.hinderedMultiplier = 1f;
                player.isMovementHindered = 0;
                player.sourcesCausingSinking = 0;
                player.reverbPreset = StartOfRound.Instance.shipReverb;
                SoundManager.Instance.earsRingingTimer = 0f;
                player.voiceMuffledByEnemy = false;
                SoundManager.Instance.playerVoicePitchTargets[Array.IndexOf(StartOfRound.Instance.allPlayerScripts, player)] = 1f;
                SoundManager.Instance.SetPlayerPitch(1f, Array.IndexOf(StartOfRound.Instance.allPlayerScripts, player));
                if (player.currentVoiceChatIngameSettings == null)
                {
                    StartOfRound.Instance.RefreshPlayerVoicePlaybackObjects();
                }
                if (player.currentVoiceChatIngameSettings != null)
                {
                    if (player.currentVoiceChatIngameSettings.voiceAudio == null)
                    {
                        player.currentVoiceChatIngameSettings.InitializeComponents();
                    }
                    if (player.currentVoiceChatIngameSettings.voiceAudio == null)
                    {
                        return;
                    }
                    player.currentVoiceChatIngameSettings.voiceAudio.GetComponent<OccludeAudio>().overridingLowPass = false;
                }
            }
            if (GameNetworkManager.Instance.localPlayerController == player)
            {
                player.bleedingHeavily = false;
                player.criticallyInjured = false;
                player.health = 5;
                HUDManager.Instance.UpdateHealthUI(5);
                player.playerBodyAnimator?.SetBool("Limp", value: false);
                player.spectatedPlayerScript = null;
                StartOfRound.Instance.SetSpectateCameraToGameOverMode(enableGameOver: false, player);
                StartOfRound.Instance.SetPlayerObjectExtrapolate(enable: false);
                HUDManager.Instance.audioListenerLowPass.enabled = false;
                HUDManager.Instance.gasHelmetAnimator.SetBool("gasEmitting", value: false);
                HUDManager.Instance.RemoveSpectateUI();
                HUDManager.Instance.gameOverAnimator.SetTrigger("revive");
            }
            StartOfRound.Instance.allPlayersDead = false;
            StartOfRound.Instance.livingPlayers++;
            StartOfRound.Instance.UpdatePlayerVoiceEffects();
            deadBodyInfo.DeactivateBody(setActive: false);

            player.thisPlayerBody.localScale = new Vector3(playerSize, playerSize, playerSize);
            if (player == localPlayer) { localPlayerSizeChangedFromSack = true; }
        }
    }
}