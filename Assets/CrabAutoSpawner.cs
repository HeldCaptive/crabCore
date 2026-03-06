using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class CrabAutoSpawner : MonoBehaviour
{
    [Header("Crab Source")]
    [SerializeField] CrabPlayerInput crabPrefab;
    [SerializeField] bool includeSceneCrabs = true;
    [SerializeField] bool disableExtraSceneCrabs = true;
    [SerializeField] bool hotPlugSupport = true;
    [SerializeField] bool runDuringOnlineSession = false;
    [SerializeField] bool spawnOnJoinButton = true;

    [Header("Spawn")]
    [SerializeField] Transform[] spawnPoints;
    [SerializeField, Min(1)] int maxPlayers = 4;
    [SerializeField] float fallbackSpacing = 3f;

    readonly List<CrabPlayerInput> managedCrabs = new List<CrabPlayerInput>();
    readonly Dictionary<int, CrabPlayerInput> joinedCrabsByIndex = new Dictionary<int, CrabPlayerInput>();
    int lastKnownGamepadCount = -1;
    bool pendingResync;

    void Awake()
    {
        RebuildManagedCrabList();
    }

    void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    void Start()
    {
        if (!CanRunSpawner())
            return;

        if (spawnOnJoinButton)
        {
            lastKnownGamepadCount = Gamepad.all.Count;

            if (disableExtraSceneCrabs)
                SetManagedCrabsActive(false);

            return;
        }

        SyncCrabsToConnectedGamepads();
    }

    void Update()
    {
        if (!CanRunSpawner())
            return;

        if (spawnOnJoinButton)
        {
            ProcessJoinMode();
            return;
        }

        if (!hotPlugSupport)
            return;

        if (pendingResync || Gamepad.all.Count != lastKnownGamepadCount)
        {
            pendingResync = false;
            SyncCrabsToConnectedGamepads();
        }
    }

    public void SyncCrabsToConnectedGamepads()
    {
        if (spawnOnJoinButton)
            return;

        managedCrabs.RemoveAll(c => c == null);

        int targetPlayers = Mathf.Min(Gamepad.all.Count, maxPlayers);
        lastKnownGamepadCount = Gamepad.all.Count;

        for (int i = 0; i < targetPlayers; i++)
        {
            CrabPlayerInput crab;

            if (i < managedCrabs.Count)
            {
                crab = managedCrabs[i];
                crab.gameObject.SetActive(true);
            }
            else
            {
                if (crabPrefab == null)
                {
                    Debug.LogWarning($"{nameof(CrabAutoSpawner)} missing crab prefab. Could not spawn player {i + 1} crab.");
                    continue;
                }

                Vector3 spawnPosition = GetSpawnPosition(i);
                Quaternion spawnRotation = crabPrefab.transform.rotation;

                crab = Instantiate(crabPrefab, spawnPosition, spawnRotation);
                crab.GetComponent<NetworkObject>().Spawn();
                managedCrabs.Add(crab);
            }

            crab.SetPlayerNumber(i + 1);
        }

        if (!disableExtraSceneCrabs)
            return;

        for (int i = targetPlayers; i < managedCrabs.Count; i++)
        {
            if (managedCrabs[i] != null)
                managedCrabs[i].gameObject.SetActive(false);
        }
    }

    void RebuildManagedCrabList()
    {
        managedCrabs.Clear();
        joinedCrabsByIndex.Clear();

        if (!includeSceneCrabs)
            return;

        CrabPlayerInput[] sceneCrabs = FindObjectsByType<CrabPlayerInput>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (CrabPlayerInput crab in sceneCrabs)
        {
            if (crab == null)
                continue;

            if (!crab.gameObject.scene.IsValid())
                continue;

            managedCrabs.Add(crab);
        }

        managedCrabs.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
    }

    void ProcessJoinMode()
    {
        managedCrabs.RemoveAll(c => c == null);

        if (hotPlugSupport && (pendingResync || Gamepad.all.Count != lastKnownGamepadCount))
        {
            pendingResync = false;
            lastKnownGamepadCount = Gamepad.all.Count;

            if (disableExtraSceneCrabs)
                DisableMissingControllerCrabs();
        }

        int count = Mathf.Min(Gamepad.all.Count, maxPlayers);

        for (int i = 0; i < count; i++)
        {
            Gamepad gamepad = Gamepad.all[i];
            if (gamepad == null)
                continue;

            if (!gamepad.buttonSouth.wasPressedThisFrame)
                continue;

            JoinPlayer(i);
        }
    }

    void JoinPlayer(int playerIndex)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        int playerNumber = playerIndex + 1;

        if (joinedCrabsByIndex.TryGetValue(playerIndex, out CrabPlayerInput existing)
            && existing != null)
        {
            existing.SetPlayerNumber(playerNumber);
            existing.gameObject.SetActive(true);
            return;
        }

        CrabPlayerInput crab = FindManagedCrabForPlayer(playerNumber);

        if (crab == null)
        {
            if (crabPrefab == null)
            {
                Debug.LogWarning($"{nameof(CrabAutoSpawner)} missing crab prefab. Could not spawn player {playerNumber} crab.");
                return;
            }

            Vector3 spawnPosition = GetSpawnPosition(playerIndex);
            Quaternion spawnRotation = crabPrefab.transform.rotation;

            crab = Instantiate(crabPrefab, spawnPosition, spawnRotation);
            crab.GetComponent<NetworkObject>().Spawn();

            managedCrabs.Add(crab);
        }

        crab.SetPlayerNumber(playerNumber);
        crab.gameObject.SetActive(true);
        joinedCrabsByIndex[playerIndex] = crab;
    }
    CrabPlayerInput FindManagedCrabForPlayer(int playerNumber)
    {
        for (int i = 0; i < managedCrabs.Count; i++)
        {
            CrabPlayerInput crab = managedCrabs[i];
            if (crab == null)
                continue;

            if (crab.PlayerNumber == playerNumber)
                return crab;
        }

        return null;
    }

    void DisableMissingControllerCrabs()
    {
        List<int> missing = new List<int>();

        foreach (KeyValuePair<int, CrabPlayerInput> pair in joinedCrabsByIndex)
        {
            if (pair.Key < Gamepad.all.Count)
                continue;

            if (pair.Value != null)
                pair.Value.gameObject.SetActive(false);

            missing.Add(pair.Key);
        }

        for (int i = 0; i < missing.Count; i++)
            joinedCrabsByIndex.Remove(missing[i]);
    }

    void SetManagedCrabsActive(bool isActive)
    {
        for (int i = 0; i < managedCrabs.Count; i++)
        {
            if (managedCrabs[i] != null)
                managedCrabs[i].gameObject.SetActive(isActive);
        }
    }

    void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!CanRunSpawner())
            return;

        if (!hotPlugSupport)
            return;

        if (!(device is Gamepad))
            return;

        if (change == InputDeviceChange.Added
            || change == InputDeviceChange.Removed
            || change == InputDeviceChange.Disconnected
            || change == InputDeviceChange.Reconnected
            || change == InputDeviceChange.Enabled
            || change == InputDeviceChange.Disabled)
        {
            pendingResync = true;
        }
    }

    bool CanRunSpawner()
    {
        if (runDuringOnlineSession)
            return true;

        return NetworkManager.Singleton == null
            || !NetworkManager.Singleton.IsListening;
    }

    Vector3 GetSpawnPosition(int playerIndex)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int pointIndex = Mathf.Min(playerIndex, spawnPoints.Length - 1);
            return spawnPoints[pointIndex].position;
        }

        return transform.position + Vector3.right * (playerIndex * fallbackSpacing);
    }
}