using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static SCP4666.Plugin;

namespace SCP4666
{
    public class ChildSackBehavior : PhysicsProp
    {
        System.Random random;
        public static bool localPlayerSizeChangedFromSack;

        // Configs
        bool makePlayersChildOnRevive = true;
        float minSize = 0.6f;
        float maxSize = 1f;
        bool usingRandomMode = false;

        public override void Start()
        {
            base.Start();
            usingRandomMode = configRandomSack.Value;
            makePlayersChildOnRevive = configMakePlayersChildOnRevive.Value;
            minSize = configChildMinSize.Value;
            maxSize = configChildMaxSize.Value;
            if (!usingRandomMode) { return; }
            random = new System.Random(scrapValue);
        }

        public void Activate()
        {
            StartCoroutine(ActivateCoroutine());
        }

        public IEnumerator ActivateCoroutine() // TODO: Make sure this is synced to all clients
        {
            LoggerInstance.LogDebug("In Activate()");
            yield return new WaitForSeconds(5f);

            if (playerHeldBy != null && localPlayer == playerHeldBy) { playerHeldBy.DropAllHeldItemsAndSync(); } // TODO: Log this, 

            if (usingRandomMode)
            {
                LoggerInstance.LogDebug("Using random mode for child sack");
                int playersRespawned = 0;
                foreach (var player in StartOfRound.Instance.allPlayerScripts)
                {
                    if (!player.isPlayerDead) { continue; }

                    if (playersRespawned == 0)
                    {
                        RevivePlayer(player);
                        playersRespawned++;
                        continue;
                    }

                    int num = random.Next(0, 2);
                    if (num == 0)
                    {
                        RevivePlayer(player);
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
                LoggerInstance.LogDebug("Reviving dead players"); // TODO: This is not being logged for clients

                foreach (var player in StartOfRound.Instance.allPlayerScripts)
                {
                    if (!player.isPlayerDead) { continue; }

                    RevivePlayer(player);
                }
                //StartOfRound.Instance.ReviveDeadPlayers();
            }

            if (IsServerOrHost) { NetworkObject.Despawn(true); }
        }

        public void RevivePlayer(PlayerControllerB player)
        {
            PlayerControllerB[] allPlayerScripts = StartOfRound.Instance.allPlayerScripts;
            int i = -1;

            StartOfRound.Instance.allPlayersDead = false;
            for (int _i = 0; _i < allPlayerScripts.Length; _i++)
            {
                if (allPlayerScripts[_i] == player)
                {
                    i = _i;
                    break;
                }
            }

            if (i == -1)
            {
                LoggerInstance.LogError("Cant find player to revive");
                return;
            }

            Debug.Log("Reviving players A");
            allPlayerScripts[i].ResetPlayerBloodObjects(allPlayerScripts[i].isPlayerDead);
            if (!allPlayerScripts[i].isPlayerDead && !allPlayerScripts[i].isPlayerControlled)
            {
                return;
            }
            allPlayerScripts[i].isClimbingLadder = false;
            allPlayerScripts[i].clampLooking = false;
            allPlayerScripts[i].inVehicleAnimation = false;
            allPlayerScripts[i].disableMoveInput = false;
            allPlayerScripts[i].ResetZAndXRotation();
            allPlayerScripts[i].thisController.enabled = true;
            allPlayerScripts[i].health = 100;
            allPlayerScripts[i].hasBeenCriticallyInjured = false;
            allPlayerScripts[i].disableLookInput = false;
            allPlayerScripts[i].disableInteract = false;
            Debug.Log("Reviving players B");
            if (allPlayerScripts[i].isPlayerDead)
            {
                allPlayerScripts[i].isPlayerDead = false;
                allPlayerScripts[i].isPlayerControlled = true;
                allPlayerScripts[i].isInElevator = true;
                allPlayerScripts[i].isInHangarShipRoom = true;
                allPlayerScripts[i].isInsideFactory = false;
                allPlayerScripts[i].parentedToElevatorLastFrame = false;
                allPlayerScripts[i].overrideGameOverSpectatePivot = null;
                StartOfRound.Instance.SetPlayerObjectExtrapolate(enable: false);
                allPlayerScripts[i].TeleportPlayer(StartOfRound.Instance.GetPlayerSpawnPosition(i));
                allPlayerScripts[i].setPositionOfDeadPlayer = false;
                allPlayerScripts[i].DisablePlayerModel(StartOfRound.Instance.allPlayerObjects[i], enable: true, disableLocalArms: true);
                allPlayerScripts[i].helmetLight.enabled = false;
                Debug.Log("Reviving players C");
                allPlayerScripts[i].Crouch(crouch: false);
                allPlayerScripts[i].criticallyInjured = false;
                if (allPlayerScripts[i].playerBodyAnimator != null)
                {
                    allPlayerScripts[i].playerBodyAnimator.SetBool("Limp", value: false);
                }
                allPlayerScripts[i].bleedingHeavily = false;
                allPlayerScripts[i].activatingItem = false;
                allPlayerScripts[i].twoHanded = false;
                allPlayerScripts[i].inShockingMinigame = false;
                allPlayerScripts[i].inSpecialInteractAnimation = false;
                allPlayerScripts[i].freeRotationInInteractAnimation = false;
                allPlayerScripts[i].disableSyncInAnimation = false;
                allPlayerScripts[i].inAnimationWithEnemy = null;
                allPlayerScripts[i].holdingWalkieTalkie = false;
                allPlayerScripts[i].speakingToWalkieTalkie = false;
                Debug.Log("Reviving players D");
                allPlayerScripts[i].isSinking = false;
                allPlayerScripts[i].isUnderwater = false;
                allPlayerScripts[i].sinkingValue = 0f;
                allPlayerScripts[i].statusEffectAudio.Stop();
                allPlayerScripts[i].DisableJetpackControlsLocally();
                allPlayerScripts[i].health = 100;
                Debug.Log("Reviving players E");
                allPlayerScripts[i].mapRadarDotAnimator.SetBool("dead", value: false);
                allPlayerScripts[i].externalForceAutoFade = Vector3.zero;
                if (allPlayerScripts[i].IsOwner)
                {
                    HUDManager.Instance.gasHelmetAnimator.SetBool("gasEmitting", value: false);
                    allPlayerScripts[i].hasBegunSpectating = false;
                    HUDManager.Instance.RemoveSpectateUI();
                    HUDManager.Instance.gameOverAnimator.SetTrigger("revive");
                    allPlayerScripts[i].hinderedMultiplier = 1f;
                    allPlayerScripts[i].isMovementHindered = 0;
                    allPlayerScripts[i].sourcesCausingSinking = 0;
                    Debug.Log("Reviving players E2");
                    allPlayerScripts[i].reverbPreset = StartOfRound.Instance.shipReverb;
                    
                    if (makePlayersChildOnRevive)
                    {
                        localPlayerSizeChangedFromSack = true;
                        float size = UnityEngine.Random.Range(minSize, maxSize);
                        ChangePlayerSizeServerRpc(allPlayerScripts[i].actualClientId, size);
                    }
                }
            }
            Debug.Log("Reviving players F");
            SoundManager.Instance.earsRingingTimer = 0f;
            allPlayerScripts[i].voiceMuffledByEnemy = false;
            SoundManager.Instance.playerVoicePitchTargets[i] = 1f;
            SoundManager.Instance.SetPlayerPitch(1f, i);
            if (allPlayerScripts[i].currentVoiceChatIngameSettings == null)
            {
                StartOfRound.Instance.RefreshPlayerVoicePlaybackObjects();
            }
            if (allPlayerScripts[i].currentVoiceChatIngameSettings != null)
            {
                if (allPlayerScripts[i].currentVoiceChatIngameSettings.voiceAudio == null)
                {
                    allPlayerScripts[i].currentVoiceChatIngameSettings.InitializeComponents();
                }
                if (allPlayerScripts[i].currentVoiceChatIngameSettings.voiceAudio == null)
                {
                    return;
                }
                allPlayerScripts[i].currentVoiceChatIngameSettings.voiceAudio.GetComponent<OccludeAudio>().overridingLowPass = false;
            }
            Debug.Log("Reviving players G");

            if (localPlayer == player)
            {
                player.bleedingHeavily = false;
                player.criticallyInjured = false;
                player.playerBodyAnimator.SetBool("Limp", value: false);
                player.health = 100;
                HUDManager.Instance.UpdateHealthUI(100, hurtPlayer: false);
                player.spectatedPlayerScript = null;
                HUDManager.Instance.audioListenerLowPass.enabled = false;
                Debug.Log("Reviving players H");
                StartOfRound.Instance.SetSpectateCameraToGameOverMode(enableGameOver: false, player);
            }

            /*RagdollGrabbableObject[] array = Object.FindObjectsOfType<RagdollGrabbableObject>();
            for (int j = 0; j < array.Length; j++)
            {
                if (!array[j].isHeld)
                {
                    if (base.IsServer)
                    {
                        if (array[j].NetworkObject.IsSpawned)
                        {
                            array[j].NetworkObject.Despawn();
                        }
                        else
                        {
                            Object.Destroy(array[j].gameObject);
                        }
                    }
                }
                else if (array[j].isHeld && array[j].playerHeldBy != null)
                {
                    array[j].playerHeldBy.DropAllHeldItems();
                }
            }*/
            DeadBodyInfo deadBody = Object.FindObjectsOfType<DeadBodyInfo>().Where(x => x.playerScript == player).FirstOrDefault();
            if (deadBody != null)
            {
                if (deadBody.grabBodyObject.playerHeldBy != null)
                {
                    deadBody.grabBodyObject.playerHeldBy.DropAllHeldItems();
                }

                if (IsServerOrHost)
                {
                    deadBody.grabBodyObject.NetworkObject.Despawn();
                }
                else
                {
                    LoggerInstance.LogDebug("Destroying dead body");
                    Object.Destroy(deadBody.grabBodyObject.gameObject);
                }

                LoggerInstance.LogDebug("Destroying dead body");
                Object.Destroy(deadBody.gameObject);
            }
            /*DeadBodyInfo[] array2 = Object.FindObjectsOfType<DeadBodyInfo>();
            for (int k = 0; k < array2.Length; k++)
            {
                Object.Destroy(array2[k].gameObject);
            }*/
            StartOfRound.Instance.livingPlayers++;
            StartOfRound.Instance.allPlayersDead = false;
            if (player == localPlayer)
            {
                StartOfRound.Instance.UpdatePlayerVoiceEffects();
            }
            StartOfRound.Instance.ResetMiscValues();
        }

        public void SpawnPresent()
        {
            if (!IsServerOrHost) { return; }
            Item giftItem = StartOfRound.Instance.allItemsList.itemsList.Where(x => x.name == "GiftBox").FirstOrDefault();
            GiftBoxItem gift = GameObject.Instantiate(giftItem.spawnPrefab, transform.position, Quaternion.identity).GetComponentInChildren<GiftBoxItem>();
            gift.NetworkObject.Spawn();
        }

        [ClientRpc]
        public void ActivateClientRpc()
        {
            Activate();
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangePlayerSizeServerRpc(ulong clientId, float size)
        {
            if (!IsServerOrHost) { return; }
            ChangePlayerSizeClientRpc(clientId, size);
        }

        [ClientRpc]
        public void ChangePlayerSizeClientRpc(ulong clientId, float size)
        {
            PlayerControllerB player = PlayerFromId(clientId);
            player.thisPlayerBody.localScale = new Vector3(size, size, size);
        }
    }
}