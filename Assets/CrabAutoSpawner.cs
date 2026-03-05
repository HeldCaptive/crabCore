using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CrabAutoSpawner : MonoBehaviour
{
    [Header("Crab Source")]
    [SerializeField] CrabPlayerInput crabPrefab;
    [SerializeField] bool includeSceneCrabs = true;
    [SerializeField] bool disableExtraSceneCrabs = true;
    [SerializeField] bool hotPlugSupport = true;

    [Header("Spawn")]
    [SerializeField] Transform[] spawnPoints;
    [SerializeField, Min(1)] int maxPlayers = 4;
    [SerializeField] float fallbackSpacing = 3f;

    readonly List<CrabPlayerInput> managedCrabs = new List<CrabPlayerInput>();
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
        SyncCrabsToConnectedGamepads();
    }

    void Update()
    {
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

        if (!includeSceneCrabs)
            return;

        EnsureSceneCrabsHavePlayerInput();

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

    void EnsureSceneCrabsHavePlayerInput()
    {
        ClawGrab2D[] clawGrabbers = FindObjectsByType<ClawGrab2D>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (ClawGrab2D grabber in clawGrabbers)
        {
            if (grabber == null)
                continue;

            if (!grabber.gameObject.scene.IsValid())
                continue;

            CrabPlayerInput existing = grabber.GetComponentInParent<CrabPlayerInput>();
            if (existing != null)
                continue;

            grabber.gameObject.AddComponent<CrabPlayerInput>();
        }
    }

    void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
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