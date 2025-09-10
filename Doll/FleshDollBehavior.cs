using System;
using System.Collections;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using Unity.Netcode.Samples;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public class NeedyCatProp : GrabbableObject
{
    public Animator animator;

    public AudioSource audioSource;

    public ClientNetworkTransform clientNetworkTransform;

    public NavMeshAgent agent;

    public Vector3 destination;

    private NavMeshPath navmeshPath;

    private Vector3 previousPosition;

    private Vector3 agentLocalVelocity;

    public override void Start()
    {
        base.Start();
        try
        {
            agent.updatePosition = false;
            destination = base.transform.position;
            navmeshPath = new NavMeshPath();
            AudioMixer diageticMixer = SoundManager.Instance.diageticMixer;
            audioSource.outputAudioMixerGroup = diageticMixer.FindMatchingGroups("SFX")[0];
        }
        catch (Exception arg)
        {
            Debug.LogError($"Error when initializing variables for {base.gameObject.name} : {arg}");
        }
    }

    public override void GrabItem()
    {

        base.GrabItem();
    }

    public override void DiscardItem()
    {
        base.DiscardItem();
    }

    public override void Update()
    {
        ((Behaviour)(object)clientNetworkTransform).enabled = !isHeld && !isHeldByEnemy;
        if (base.IsServer && !isHeld && !isHeldByEnemy && !base.IsOwner)
        {
            GetComponent<NetworkObject>().RemoveOwnership();
        }
        if (!isInElevator && StartOfRound.Instance.currentLevel.spawnEnemiesAndScrap)
        {
            agent.enabled = !isHeld && !isHeldByEnemy && reachedFloorTarget && !(fallTime < 1f);
            if (fallTime >= 1f && !reachedFloorTarget)
            {
                targetFloorPosition = base.transform.position;
                destination = base.transform.position;
                previousPosition = base.transform.position;
                agent.enabled = true;
            }
        }
        if (!isHeld && !isHeldByEnemy && fallTime >= 1f && !reachedFloorTarget && animator.GetBool("held"))
        {
            animator.SetBool("held", value: false);
        }
        if (!isFleeing)
        {
            if (isHeld || isHeldByEnemy || !reachedFloorTarget || fallTime < 1f || isInElevator)
            {
                base.Update();
            }
        }
        if (base.IsServer)
        {
            if (isSitting)
            {
                if (timeBeforeNextSitAnim <= 0f)
                {
                    SetCatSitAnimationServerRpc(UnityEngine.Random.Range(0, sitAnimationsLength));
                    timeBeforeNextSitAnim = UnityEngine.Random.Range(IntervalSitAnimChange.x, IntervalSitAnimChange.y);
                }
                timeBeforeNextSitAnim -= Time.deltaTime;
            }
            else
            {
                if (timeBeforeNextIdleAnim <= 0f)
                {
                    SetCatIdleAnimationServerRpc(UnityEngine.Random.Range(0, idleAnimationsLength));
                    timeBeforeNextIdleAnim = UnityEngine.Random.Range(IntervalIdleAnimChange.x, IntervalIdleAnimChange.y);
                }
                timeBeforeNextIdleAnim -= Time.deltaTime;
            }
            if (timeBeforeNextMeow <= 0f)
            {
                MakeCatMeowServerRpc();
                float num = CalculateCatMeowInterval();
                timeBeforeNextMeow = UnityEngine.Random.Range(IntervalMeow.x + num, IntervalMeow.y + num);
            }
            timeBeforeNextMeow -= Time.deltaTime;
            if (timeBeforeTryFlee >= 0f)
            {
                timeBeforeTryFlee -= Time.deltaTime;
            }
            if (!isFleeing)
            {
                if (!isHeld && !isHeldByEnemy && !isInElevator && !isBeingHoarded && StartOfRound.Instance.currentLevel.spawnEnemiesAndScrap)
                {
                    if (timeBeforeNextMove <= 0f)
                    {
                        SetRandomDestination();
                        timeBeforeNextMove = UnityEngine.Random.Range(IntervalMove.x, IntervalMove.y);
                    }
                    timeBeforeNextMove -= Time.deltaTime;
                    base.transform.position = agent.nextPosition;
                }
            }
            else if (isFleeing)
            {
                base.transform.position = agent.nextPosition;
                targetFloorPosition = base.transform.position;
            }
        }
        if (!isHeld && !isHeldByEnemy)
        {
            SynchronizeAnimator();
        }
    }

    public float CalculateCatMeowInterval()
    {
        float num = 0f;
        if (isInElevator)
        {
            PlayerControllerB[] allPlayerScripts = StartOfRound.Instance.allPlayerScripts;
            foreach (PlayerControllerB playerControllerB in allPlayerScripts)
            {
                if (playerControllerB.isInElevator)
                {
                    num += 6f;
                    break;
                }
            }
            (int, float)[] array = placeableMeowInterval;
            for (int j = 0; j < array.Length; j++)
            {
                (int, float) tuple = array[j];
                if (StartOfRound.Instance.SpawnedShipUnlockables.ContainsKey(tuple.Item1))
                {
                    num += tuple.Item2;
                }
            }
        }
        if (!NeedyCatsBase.Instance.CatFoodSilence.Value && CheckForCatFood())
        {
            num += 120f;
        }
        return num;
    }

    public bool CheckForCatFood()
    {
        foreach (CatFoodProp allCatFood in NeedyCatsBase.Instance.AllCatFoods)
        {
            if (allCatFood.reachedFloorTarget && allCatFood.IsFeeding && Vector3.Distance(allCatFood.transform.position, base.transform.position) < 10f)
            {
                return true;
            }
        }
        return false;
    }

    public void SetDestinationToPosition(Vector3 position, bool checkForPath = false)
    {
        if (checkForPath)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 1.75f);
            navmeshPath = new NavMeshPath();
            if (!agent.CalculatePath(position, navmeshPath))
            {
                Debug.Log(base.gameObject.name + " calculatepath returned false.");
                return;
            }
            if (Vector3.Distance(navmeshPath.corners[navmeshPath.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 2.7f)) > 1.55f)
            {
                Debug.Log(base.gameObject.name + " path calculation went wrong.");
                return;
            }
        }
        destination = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, -1f);
        agent.SetDestination(destination);
    }

    public override void ItemActivate(bool used, bool buttonDown = true)
    {
        base.ItemActivate(used, buttonDown);
        if (base.IsOwner)
        {
            MakeCatCalmServerRpc();
        }
    }

    public void SetRandomDestination()
    {
        Vector3 position = base.transform.position + UnityEngine.Random.insideUnitSphere * 5f;
        agent.speed = WalkingSpeed;
        SetDestinationToPosition(position);
    }

    private void PlayCatNoise(AudioClip[] array, bool audible = true)
    {
        int num = random.Next(0, array.Length);
        float num2 = (float)random.Next((int)(minLoudness * 100f), (int)(maxLoudness * 100f)) / 100f;
        float pitch = (float)random.Next((int)(minPitch * 100f), (int)(maxPitch * 100f)) / 100f;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(array[num], num2);
        WalkieTalkie.TransmitOneShotAudio(audioSource, array[num], num2 - 0.4f);
        if (audible)
        {
            float num3 = (isInElevator ? (noiseRange - 2.5f) : noiseRange);
            RoundManager.Instance.PlayAudibleNoise(base.transform.position, num3, num2, 0, isInElevator && StartOfRound.Instance.hangarDoorsClosed, 8881);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void MakeCatMeowServerRpc()
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager == null || !networkManager.IsListening)
        {
            return;
        }
        if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
        {
            ServerRpcParams serverRpcParams = default(ServerRpcParams);
            FastBufferWriter bufferWriter = __beginSendServerRpc(3454429685u, serverRpcParams, RpcDelivery.Reliable);
            __endSendServerRpc(ref bufferWriter, 3454429685u, serverRpcParams, RpcDelivery.Reliable);
        }
        if (__rpc_exec_stage != __RpcExecStage.Server || (!networkManager.IsServer && !networkManager.IsHost))
        {
            return;
        }
        if (!isFeeding && CheckForCatFood())
        {
            isFeeding = true;
            return;
        }
        if (isFeeding && !CheckForCatFood())
        {
            isFeeding = false;
        }
        if (!NeedyCatsBase.Instance.CatFoodSilence.Value || !CheckForCatFood())
        {
            MakeCatMeowClientRpc();
        }
    }

    [ClientRpc]
    public void MakeCatMeowClientRpc()
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
            {
                ClientRpcParams clientRpcParams = default(ClientRpcParams);
                FastBufferWriter bufferWriter = __beginSendClientRpc(1946573138u, clientRpcParams, RpcDelivery.Reliable);
                __endSendClientRpc(ref bufferWriter, 1946573138u, clientRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                PlayCatNoise(noiseSFX);
                animator.SetTrigger("meow");
            }
        }
    }

    [ClientRpc]
    public void MakeCatFleeClientRpc()
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
            {
                ClientRpcParams clientRpcParams = default(ClientRpcParams);
                FastBufferWriter bufferWriter = __beginSendClientRpc(2532932337u, clientRpcParams, RpcDelivery.Reliable);
                __endSendClientRpc(ref bufferWriter, 2532932337u, clientRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                animator.SetBool("sit", value: false);
                PlayCatNoise(fleeSFX);
                animator.SetTrigger("meow");
                base.transform.SetParent(null, worldPositionStays: true);
                isFleeing = true;
                isInElevator = false;
                isSitting = false;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void StopCatFleeServerRpc()
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
            {
                ServerRpcParams serverRpcParams = default(ServerRpcParams);
                FastBufferWriter bufferWriter = __beginSendServerRpc(3642870494u, serverRpcParams, RpcDelivery.Reliable);
                __endSendServerRpc(ref bufferWriter, 3642870494u, serverRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                StopCatFleeClientRpc();
            }
        }
    }

    [ClientRpc]
    public void StopCatFleeClientRpc()
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
            {
                ClientRpcParams clientRpcParams = default(ClientRpcParams);
                FastBufferWriter bufferWriter = __beginSendClientRpc(2708699245u, clientRpcParams, RpcDelivery.Reliable);
                __endSendClientRpc(ref bufferWriter, 2708699245u, clientRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                isFleeing = false;
            }
        }
    }

    [ServerRpc]
    public void MakeCatCalmServerRpc()
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager == null || !networkManager.IsListening)
        {
            return;
        }
        if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
        {
            if (base.OwnerClientId != networkManager.LocalClientId)
            {
                if (networkManager.LogLevel <= LogLevel.Normal)
                {
                    Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
                }
                return;
            }
            ServerRpcParams serverRpcParams = default(ServerRpcParams);
            FastBufferWriter bufferWriter = __beginSendServerRpc(2784153610u, serverRpcParams, RpcDelivery.Reliable);
            __endSendServerRpc(ref bufferWriter, 2784153610u, serverRpcParams, RpcDelivery.Reliable);
        }
        if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
        {
            float num = CalculateCatMeowInterval();
            timeBeforeNextMeow = UnityEngine.Random.Range(IntervalMeow.x + num + 3f, IntervalMeow.y + num + 6f);
            MakeCatCalmClientRpc();
        }
    }

    public void OnCollideWithDog(Collider other, MouthDogAI mouthDogAI)
    {
        if (!base.IsServer || !(timeBeforeTryFlee <= 0f))
        {
            return;
        }
        if (UnityEngine.Random.Range(0f, 100f) > NeedyCatsBase.Instance.CatFleeDogsChance.Value)
        {
            timeBeforeTryFlee = timeBeforeTryFleeLength;
            return;
        }
        Vector3 vector = other.transform.position - base.transform.position;
        agent.enabled = true;
        Vector3 position = base.transform.position - vector.normalized * 40f;
        GameObject[] array = allAINodes.OrderBy((GameObject x) => Vector3.Distance(position, x.transform.position)).ToArray();
        agent.nextPosition = base.transform.position;
        SetDestinationToPosition(array[0].transform.position);
        agent.speed = RunningSpeed;
        timeBeforeNextMove = UnityEngine.Random.Range(IntervalMove.x + 5f, IntervalMove.y + 10f);
        timeBeforeNextMeow = UnityEngine.Random.Range(IntervalMeow.x, IntervalMeow.y);
        timeBeforeTryFlee = timeBeforeTryFleeLength;
        if (fleeCoroutine != null)
        {
            StopCoroutine(fleeCoroutine);
        }
        fleeCoroutine = FleeCoroutine(timeBeforeNextMove);
        StartCoroutine(fleeCoroutine);
        MakeCatFleeClientRpc();
    }

    public IEnumerator FleeCoroutine(float time)
    {
        yield return new WaitForSeconds(time);
        StopCatFleeClientRpc();
    }

    [ClientRpc]
    public void MakeCatCalmClientRpc()
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
            {
                ClientRpcParams clientRpcParams = default(ClientRpcParams);
                FastBufferWriter bufferWriter = __beginSendClientRpc(2302897036u, clientRpcParams, RpcDelivery.Reliable);
                __endSendClientRpc(ref bufferWriter, 2302897036u, clientRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                playerHeldBy.doingUpperBodyEmote = 1.16f;
                playerHeldBy.playerBodyAnimator.SetTrigger("PullGrenadePin2");
                StartCoroutine(PlayCatCalmNoiseDelayed());
            }
        }
    }

    private IEnumerator PlayCatCalmNoiseDelayed()
    {
        yield return new WaitForSeconds(0.5f);
        PlayCatNoise(calmSFX, audible: false);
    }

    [ServerRpc]
    public void MakeCatSitServerRpc(bool sit)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager == null || !networkManager.IsListening)
        {
            return;
        }
        if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
        {
            if (base.OwnerClientId != networkManager.LocalClientId)
            {
                if (networkManager.LogLevel <= LogLevel.Normal)
                {
                    Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
                }
                return;
            }
            ServerRpcParams serverRpcParams = default(ServerRpcParams);
            FastBufferWriter bufferWriter = __beginSendServerRpc(2411027775u, serverRpcParams, RpcDelivery.Reliable);
            bufferWriter.WriteValueSafe(in sit, default(FastBufferWriter.ForPrimitives));
            __endSendServerRpc(ref bufferWriter, 2411027775u, serverRpcParams, RpcDelivery.Reliable);
        }
        if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
        {
            MakeCatSitClientRpc(sit);
        }
    }

    [ClientRpc]
    public void MakeCatSitClientRpc(bool sit)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
            {
                ClientRpcParams clientRpcParams = default(ClientRpcParams);
                FastBufferWriter bufferWriter = __beginSendClientRpc(3921800473u, clientRpcParams, RpcDelivery.Reliable);
                bufferWriter.WriteValueSafe(in sit, default(FastBufferWriter.ForPrimitives));
                __endSendClientRpc(ref bufferWriter, 3921800473u, clientRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                animator.SetBool("sit", sit);
                isSitting = sit;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetCatMaterialServerRpc(int index)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
            {
                ServerRpcParams serverRpcParams = default(ServerRpcParams);
                FastBufferWriter bufferWriter = __beginSendServerRpc(2046492207u, serverRpcParams, RpcDelivery.Reliable);
                BytePacker.WriteValueBitPacked(bufferWriter, index);
                __endSendServerRpc(ref bufferWriter, 2046492207u, serverRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                SetCatMaterialClientRpc(index);
            }
        }
    }

    [ClientRpc]
    public void SetCatMaterialClientRpc(int index)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
            {
                ClientRpcParams clientRpcParams = default(ClientRpcParams);
                FastBufferWriter bufferWriter = __beginSendClientRpc(3875721248u, clientRpcParams, RpcDelivery.Reliable);
                BytePacker.WriteValueBitPacked(bufferWriter, index);
                __endSendClientRpc(ref bufferWriter, 3875721248u, clientRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost) && skinnedMeshRenderer != null)
            {
                skinnedMeshRenderer.sharedMaterial = materials[index];
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetCatNameServerRpc(string name)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager == null || !networkManager.IsListening)
        {
            return;
        }
        if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
        {
            ServerRpcParams serverRpcParams = default(ServerRpcParams);
            FastBufferWriter bufferWriter = __beginSendServerRpc(3049925245u, serverRpcParams, RpcDelivery.Reliable);
            bool value = name != null;
            bufferWriter.WriteValueSafe(in value, default(FastBufferWriter.ForPrimitives));
            if (value)
            {
                bufferWriter.WriteValueSafe(name);
            }
            __endSendServerRpc(ref bufferWriter, 3049925245u, serverRpcParams, RpcDelivery.Reliable);
        }
        if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
        {
            SetCatNameClientRpc(name);
        }
    }

    [ClientRpc]
    public void SetCatNameClientRpc(string name)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager == null || !networkManager.IsListening)
        {
            return;
        }
        if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
        {
            ClientRpcParams clientRpcParams = default(ClientRpcParams);
            FastBufferWriter bufferWriter = __beginSendClientRpc(1321450034u, clientRpcParams, RpcDelivery.Reliable);
            bool value = name != null;
            bufferWriter.WriteValueSafe(in value, default(FastBufferWriter.ForPrimitives));
            if (value)
            {
                bufferWriter.WriteValueSafe(name);
            }
            __endSendClientRpc(ref bufferWriter, 1321450034u, clientRpcParams, RpcDelivery.Reliable);
        }
        if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
        {
            GetComponentInChildren<ScanNodeProperties>().headerText = "Cat (" + name + ")";
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetCatIdleAnimationServerRpc(int index)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
            {
                ServerRpcParams serverRpcParams = default(ServerRpcParams);
                FastBufferWriter bufferWriter = __beginSendServerRpc(1770904636u, serverRpcParams, RpcDelivery.Reliable);
                BytePacker.WriteValueBitPacked(bufferWriter, index);
                __endSendServerRpc(ref bufferWriter, 1770904636u, serverRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                SetCatIdleAnimationClientRpc(index);
            }
        }
    }

    [ClientRpc]
    public void SetCatIdleAnimationClientRpc(int index)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
            {
                ClientRpcParams clientRpcParams = default(ClientRpcParams);
                FastBufferWriter bufferWriter = __beginSendClientRpc(283245169u, clientRpcParams, RpcDelivery.Reliable);
                BytePacker.WriteValueBitPacked(bufferWriter, index);
                __endSendClientRpc(ref bufferWriter, 283245169u, clientRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                animator.SetInteger("idleAnimation", index);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetCatSitAnimationServerRpc(int index)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
            {
                ServerRpcParams serverRpcParams = default(ServerRpcParams);
                FastBufferWriter bufferWriter = __beginSendServerRpc(1062797608u, serverRpcParams, RpcDelivery.Reliable);
                BytePacker.WriteValueBitPacked(bufferWriter, index);
                __endSendServerRpc(ref bufferWriter, 1062797608u, serverRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                SetCatSitAnimationClientRpc(index);
            }
        }
    }

    [ClientRpc]
    public void SetCatSitAnimationClientRpc(int index)
    {
        NetworkManager networkManager = base.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
            {
                ClientRpcParams clientRpcParams = default(ClientRpcParams);
                FastBufferWriter bufferWriter = __beginSendClientRpc(4162097660u, clientRpcParams, RpcDelivery.Reliable);
                BytePacker.WriteValueBitPacked(bufferWriter, index);
                __endSendClientRpc(ref bufferWriter, 4162097660u, clientRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
            {
                animator.SetInteger("sitAnimation", index);
            }
        }
    }

    public virtual void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot, int noiseID)
    {
        if (isHeld || isHeldByEnemy || noiseID == 8881 || noiseID == 75 || noiseID == 5 || noiseID == 94 || isInShipRoom || !StartOfRound.Instance.currentLevel.spawnEnemiesAndScrap)
        {
            return;
        }
        Vector3 vector = noisePosition - base.transform.position;
        if (!(vector.magnitude < 5f) || !(noiseLoudness > 0.8f))
        {
            return;
        }
        PlayCatNoise(fleeSFX);
        if (base.IsServer)
        {
            Vector3 position = base.transform.position - vector.normalized * 20f;
            GameObject[] array = allAINodes.OrderBy((GameObject x) => Vector3.Distance(position, x.transform.position)).ToArray();
            SetDestinationToPosition(array[0].transform.position);
            agent.speed = RunningSpeed;
            timeBeforeNextMove = UnityEngine.Random.Range(IntervalMove.x + 1f, IntervalMove.y + 2f);
        }
    }

    protected override void __initializeVariables()
    {
        base.__initializeVariables();
    }

    [RuntimeInitializeOnLoadMethod]
    internal static void InitializeRPCS_NeedyCatProp()
    {
        NetworkManager.__rpc_func_table.Add(3454429685u, __rpc_handler_3454429685);
        NetworkManager.__rpc_func_table.Add(1946573138u, __rpc_handler_1946573138);
        NetworkManager.__rpc_func_table.Add(2532932337u, __rpc_handler_2532932337);
        NetworkManager.__rpc_func_table.Add(3642870494u, __rpc_handler_3642870494);
        NetworkManager.__rpc_func_table.Add(2708699245u, __rpc_handler_2708699245);
        NetworkManager.__rpc_func_table.Add(2784153610u, __rpc_handler_2784153610);
        NetworkManager.__rpc_func_table.Add(2302897036u, __rpc_handler_2302897036);
        NetworkManager.__rpc_func_table.Add(2411027775u, __rpc_handler_2411027775);
        NetworkManager.__rpc_func_table.Add(3921800473u, __rpc_handler_3921800473);
        NetworkManager.__rpc_func_table.Add(2046492207u, __rpc_handler_2046492207);
        NetworkManager.__rpc_func_table.Add(3875721248u, __rpc_handler_3875721248);
        NetworkManager.__rpc_func_table.Add(3049925245u, __rpc_handler_3049925245);
        NetworkManager.__rpc_func_table.Add(1321450034u, __rpc_handler_1321450034);
        NetworkManager.__rpc_func_table.Add(1770904636u, __rpc_handler_1770904636);
        NetworkManager.__rpc_func_table.Add(283245169u, __rpc_handler_283245169);
        NetworkManager.__rpc_func_table.Add(1062797608u, __rpc_handler_1062797608);
        NetworkManager.__rpc_func_table.Add(4162097660u, __rpc_handler_4162097660);
    }

    private static void __rpc_handler_3454429685(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            target.__rpc_exec_stage = __RpcExecStage.Server;
            ((NeedyCatProp)target).MakeCatMeowServerRpc();
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_1946573138(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            target.__rpc_exec_stage = __RpcExecStage.Client;
            ((NeedyCatProp)target).MakeCatMeowClientRpc();
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_2532932337(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            target.__rpc_exec_stage = __RpcExecStage.Client;
            ((NeedyCatProp)target).MakeCatFleeClientRpc();
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_3642870494(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            target.__rpc_exec_stage = __RpcExecStage.Server;
            ((NeedyCatProp)target).StopCatFleeServerRpc();
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_2708699245(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            target.__rpc_exec_stage = __RpcExecStage.Client;
            ((NeedyCatProp)target).StopCatFleeClientRpc();
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_2784153610(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager == null || !networkManager.IsListening)
        {
            return;
        }
        if (rpcParams.Server.Receive.SenderClientId != target.OwnerClientId)
        {
            if (networkManager.LogLevel <= LogLevel.Normal)
            {
                Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
            }
        }
        else
        {
            target.__rpc_exec_stage = __RpcExecStage.Server;
            ((NeedyCatProp)target).MakeCatCalmServerRpc();
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_2302897036(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            target.__rpc_exec_stage = __RpcExecStage.Client;
            ((NeedyCatProp)target).MakeCatCalmClientRpc();
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_2411027775(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager == null || !networkManager.IsListening)
        {
            return;
        }
        if (rpcParams.Server.Receive.SenderClientId != target.OwnerClientId)
        {
            if (networkManager.LogLevel <= LogLevel.Normal)
            {
                Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
            }
        }
        else
        {
            reader.ReadValueSafe(out bool value, default(FastBufferWriter.ForPrimitives));
            target.__rpc_exec_stage = __RpcExecStage.Server;
            ((NeedyCatProp)target).MakeCatSitServerRpc(value);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_3921800473(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            reader.ReadValueSafe(out bool value, default(FastBufferWriter.ForPrimitives));
            target.__rpc_exec_stage = __RpcExecStage.Client;
            ((NeedyCatProp)target).MakeCatSitClientRpc(value);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_2046492207(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            ByteUnpacker.ReadValueBitPacked(reader, out int value);
            target.__rpc_exec_stage = __RpcExecStage.Server;
            ((NeedyCatProp)target).SetCatMaterialServerRpc(value);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_3875721248(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            ByteUnpacker.ReadValueBitPacked(reader, out int value);
            target.__rpc_exec_stage = __RpcExecStage.Client;
            ((NeedyCatProp)target).SetCatMaterialClientRpc(value);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_3049925245(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            reader.ReadValueSafe(out bool value, default(FastBufferWriter.ForPrimitives));
            string s = null;
            if (value)
            {
                reader.ReadValueSafe(out s, oneByteChars: false);
            }
            target.__rpc_exec_stage = __RpcExecStage.Server;
            ((NeedyCatProp)target).SetCatNameServerRpc(s);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_1321450034(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            reader.ReadValueSafe(out bool value, default(FastBufferWriter.ForPrimitives));
            string s = null;
            if (value)
            {
                reader.ReadValueSafe(out s, oneByteChars: false);
            }
            target.__rpc_exec_stage = __RpcExecStage.Client;
            ((NeedyCatProp)target).SetCatNameClientRpc(s);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_1770904636(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            ByteUnpacker.ReadValueBitPacked(reader, out int value);
            target.__rpc_exec_stage = __RpcExecStage.Server;
            ((NeedyCatProp)target).SetCatIdleAnimationServerRpc(value);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_283245169(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            ByteUnpacker.ReadValueBitPacked(reader, out int value);
            target.__rpc_exec_stage = __RpcExecStage.Client;
            ((NeedyCatProp)target).SetCatIdleAnimationClientRpc(value);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_1062797608(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            ByteUnpacker.ReadValueBitPacked(reader, out int value);
            target.__rpc_exec_stage = __RpcExecStage.Server;
            ((NeedyCatProp)target).SetCatSitAnimationServerRpc(value);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    private static void __rpc_handler_4162097660(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
    {
        NetworkManager networkManager = target.NetworkManager;
        if ((object)networkManager != null && networkManager.IsListening)
        {
            ByteUnpacker.ReadValueBitPacked(reader, out int value);
            target.__rpc_exec_stage = __RpcExecStage.Client;
            ((NeedyCatProp)target).SetCatSitAnimationClientRpc(value);
            target.__rpc_exec_stage = __RpcExecStage.None;
        }
    }

    protected internal override string __getTypeName()
    {
        return "NeedyCatProp";
    }
}
