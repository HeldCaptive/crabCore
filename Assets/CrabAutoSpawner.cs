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
    [SerializeField] int maxPlayers = 0;
    [SerializeField] float fallbackSpacing = 3f;
    [SerializeField, Min(0.1f)] float minSpawnDistance = 2.5f;
    [SerializeField, Min(1)] int maxSpawnSearchSteps = 6;

    [Header("Toss-In Spawn")]
    [SerializeField] bool useTossInSpawn = true;
    [SerializeField] Transform tossSpawnPoint;
    [SerializeField, Min(0f)] float tossHeightAboveCamera = 4f;
    [SerializeField, Min(0.1f)] float tossHorizontalSpacing = 2.5f;

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

        int targetPlayers = Mathf.Min(Gamepad.all.Count, GetMaxPlayers());
        lastKnownGamepadCount = Gamepad.all.Count;

        for (int i = 0; i < targetPlayers; i++)
        {
            CrabPlayerInput crab;

            if (i < managedCrabs.Count)
            {
                crab = managedCrabs[i];
                PlaceCrabAtSpawn(crab, i);
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

                PlaceCrabAtSpawn(crab, i);
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

        int count = Mathf.Min(Gamepad.all.Count, GetMaxPlayers());

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
            PlaceCrabAtSpawn(existing, playerIndex);
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

        PlaceCrabAtSpawn(crab, playerIndex);
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

    int GetMaxPlayers()
    {
        if (maxPlayers <= 0)
            return int.MaxValue;

        return maxPlayers;
    }

    Vector3 GetSpawnPosition(int playerIndex)
    {
        if (useTossInSpawn)
            return FindClearSpawnPosition(GetTossSpawnPosition(playerIndex));

        Vector3 basePosition;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int pointIndex = Mathf.Min(playerIndex, spawnPoints.Length - 1);
            basePosition = spawnPoints[pointIndex].position;
        }
        else
        {
            basePosition = transform.position + Vector3.right * (playerIndex * fallbackSpacing);
        }

        return FindClearSpawnPosition(basePosition);
    }

    Vector3 GetTossSpawnPosition(int playerIndex)
    {
        if (tossSpawnPoint != null)
        {
            float xOffset = GetCenteredHorizontalOffset(playerIndex, tossHorizontalSpacing);
            return tossSpawnPoint.position + Vector3.right * xOffset;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            float centerX = cam.transform.position.x;
            float topY;

            if (cam.orthographic)
                topY = cam.transform.position.y + cam.orthographicSize;
            else
            {
                Vector3 topCenter = cam.ViewportToWorldPoint(new Vector3(0.5f, 1f, Mathf.Abs(cam.transform.position.z)));
                topY = topCenter.y;
            }

            float xOffset = GetCenteredHorizontalOffset(playerIndex, tossHorizontalSpacing);
            return new Vector3(centerX + xOffset, topY + tossHeightAboveCamera, transform.position.z);
        }

        return transform.position + Vector3.up * tossHeightAboveCamera + Vector3.right * GetCenteredHorizontalOffset(playerIndex, tossHorizontalSpacing);
    }

    float GetCenteredHorizontalOffset(int playerIndex, float spacing)
    {
        float effectiveSpacing = spacing > 0.001f
            ? spacing
            : Mathf.Max(0.1f, fallbackSpacing);

        float slot = (playerIndex / 2) + 1f;
        float direction = (playerIndex % 2 == 0) ? -1f : 1f;
        return slot * direction * effectiveSpacing;
    }

    Vector3 FindClearSpawnPosition(Vector3 desiredPosition)
    {
        if (IsSpawnPositionClear(desiredPosition))
            return desiredPosition;

        float spacing = Mathf.Max(0.1f, fallbackSpacing);

        for (int step = 1; step <= maxSpawnSearchSteps; step++)
        {
            float offset = spacing * step;

            Vector3 rightCandidate = desiredPosition + Vector3.right * offset;
            if (IsSpawnPositionClear(rightCandidate))
                return rightCandidate;

            Vector3 leftCandidate = desiredPosition - Vector3.right * offset;
            if (IsSpawnPositionClear(leftCandidate))
                return leftCandidate;
        }

        return desiredPosition + Vector3.right * (spacing * (maxSpawnSearchSteps + 1));
    }

    bool IsSpawnPositionClear(Vector3 position)
    {
        float minDistanceSqr = minSpawnDistance * minSpawnDistance;

        for (int i = 0; i < managedCrabs.Count; i++)
        {
            CrabPlayerInput crab = managedCrabs[i];
            if (crab == null)
                continue;

            if (!crab.gameObject.activeInHierarchy)
                continue;

            Vector3 delta = crab.transform.position - position;
            if (delta.sqrMagnitude < minDistanceSqr)
                return false;
        }

        return true;
    }

    void PlaceCrabAtSpawn(CrabPlayerInput crab, int playerIndex)
    {
        if (crab == null)
            return;

        crab.transform.position = GetSpawnPosition(playerIndex);
        float tossSpeed = GetUnifiedTossVerticalSpeed(crab);

        Rigidbody2D[] rigidbodies = crab.GetComponentsInChildren<Rigidbody2D>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody2D rb = rigidbodies[i];
            if (rb == null)
                continue;

            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;

            if (useTossInSpawn)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, tossSpeed);
        }
    }

    float GetUnifiedTossVerticalSpeed(CrabPlayerInput crab)
    {
        CrabNetworkSync2D crabSync = null;

        if (crab != null)
            crabSync = crab.GetComponentInParent<CrabNetworkSync2D>();

        if (crabSync == null && crabPrefab != null)
            crabSync = crabPrefab.GetComponentInParent<CrabNetworkSync2D>();

        if (crabSync == null)
            return 0f;

        return crabSync.AppliedTossVerticalSpeed;
    }
}