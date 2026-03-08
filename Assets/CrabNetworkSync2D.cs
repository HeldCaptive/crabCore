using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using System;
using System.Collections.Generic;

[DefaultExecutionOrder(500)]
[RequireComponent(typeof(NetworkObject))]
public class CrabNetworkSync2D : NetworkBehaviour
{
    [SerializeField] bool disableRemotePhysicsSimulation = false;
    [SerializeField] float interpolationLerp = 0.15f; // Smoothing speed for remote movement
    [SerializeField] float remotePositionCorrection = 8f;
    [SerializeField] float remoteRotationCorrection = 8f;
    [SerializeField] bool spreadPlayersOnNetworkSpawn = true;
    [SerializeField] Vector2 networkSpawnCenter = Vector2.zero;
    [SerializeField] float networkSpawnSpacing = 6f;
    [SerializeField] bool tossInFromAboveOnNetworkSpawn = true;
    [SerializeField] float tossHeightAboveCamera = 4f;
    [SerializeField] float tossInitialVerticalSpeed = 0f;
    [SerializeField] bool tossTowardGround = true;
    [SerializeField, Min(0)] int enforceOwnerTossFrames = 8;
    [SerializeField] bool preferSceneSpawnZone = true;
    [SerializeField] string sceneSpawnZoneName = "CrabSpawnZone";
    [SerializeField] bool requireSceneSpawnZone = true;
    [SerializeField, HideInInspector] bool preferSceneTossSpawnPoint = true;
    [SerializeField, HideInInspector] string sceneTossSpawnPointName = "TossSpawn";

    readonly List<Rigidbody2D> rigidbodies = new List<Rigidbody2D>();
    readonly List<RigidbodyState> lastAppliedStates = new List<RigidbodyState>();
    Rigidbody2D rootBody;
    NetworkTransform networkTransform;
    bool initialSpawnPositionApplied;
    int remainingOwnerTossFrames;
    Renderer[] cachedRenderers;
    bool[] baseRendererEnabled;
    Collider2D[] cachedColliders;
    bool[] baseColliderEnabled;
    bool preMatchHidden;
    bool hasRemoteStateAfterMatchStart;
    bool warnedMissingSpawnZone;
    readonly NetworkList<RigidbodyState> syncedStates = new NetworkList<RigidbodyState>(
        null,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public bool IsOwnerInSpawnTossPhase => IsOwner && remainingOwnerTossFrames > 0;
    public float TossInitialVerticalSpeed => tossInitialVerticalSpeed;
    public float AppliedTossVerticalSpeed => GetAppliedTossVerticalSpeed();

    struct RigidbodyState : INetworkSerializable, IEquatable<RigidbodyState>
    {
        public Vector2 Position;
        public float Rotation;
        public Vector2 LinearVelocity;
        public float AngularVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref LinearVelocity);
            serializer.SerializeValue(ref AngularVelocity);
        }

        public bool Equals(RigidbodyState other)
        {
            return Position == other.Position
                && Mathf.Approximately(Rotation, other.Rotation)
                && LinearVelocity == other.LinearVelocity
                && Mathf.Approximately(AngularVelocity, other.AngularVelocity);
        }
    }

    void Awake()
    {
        rootBody = GetComponent<Rigidbody2D>();
        networkTransform = GetComponent<NetworkTransform>();

        if (networkTransform != null && networkTransform.enabled)
            networkTransform.enabled = false;

        ResolveRigidbodies();
        CachePresentationTargets();
    }

    public override void OnNetworkSpawn()
    {
        ResolveRigidbodies();

        if (IsInitialSpawnDelayActive())
            PlaceAtResolvedSpawnWithoutLaunch();
        else if (IsOwner)
            TryApplyInitialSpawnSpread();

        EnsureStateListSize();
        ApplySimulationMode();
        UpdatePreMatchPresentation();

        if (IsOwner && tossInFromAboveOnNetworkSpawn && Mathf.Abs(GetAppliedTossVerticalSpeed()) > 0.001f)
            remainingOwnerTossFrames = enforceOwnerTossFrames;

        if (rigidbodies.Count == 0)
            return;

        if (IsOwner)
            WriteStatesFromOwner();
    }

    void CachePresentationTargets()
    {
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        baseRendererEnabled = new bool[cachedRenderers.Length];
        for (int i = 0; i < cachedRenderers.Length; i++)
            baseRendererEnabled[i] = cachedRenderers[i] != null && cachedRenderers[i].enabled;

        cachedColliders = GetComponentsInChildren<Collider2D>(true);
        baseColliderEnabled = new bool[cachedColliders.Length];
        for (int i = 0; i < cachedColliders.Length; i++)
            baseColliderEnabled[i] = cachedColliders[i] != null && cachedColliders[i].enabled;
    }

    bool ShouldHideUntilMatchStart()
    {
        if (IsPreMatchLocked())
            return true;

        if (IsWaitingForRemoteFirstState())
            return true;

        return false;
    }

    bool IsPreMatchLocked()
    {
        if (!IsSpawned)
            return false;

        if (!NetworkStart.DelayInitialSpawnUntilHostStart)
            return false;

        return !NetworkStart.IsMatchStarted;
    }

    bool IsWaitingForRemoteFirstState()
    {
        if (!IsSpawned)
            return false;

        if (IsOwner)
            return false;

        if (!NetworkStart.DelayInitialSpawnUntilHostStart)
            return false;

        if (!NetworkStart.IsMatchStarted)
            return false;

        return !hasRemoteStateAfterMatchStart;
    }

    void UpdatePreMatchPresentation()
    {
        if (NetworkStart.DelayInitialSpawnUntilHostStart && !NetworkStart.IsMatchStarted)
            hasRemoteStateAfterMatchStart = false;

        bool shouldHide = ShouldHideUntilMatchStart();
        if (shouldHide == preMatchHidden)
            return;

        preMatchHidden = shouldHide;

        if (cachedRenderers != null)
        {
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                Renderer renderer = cachedRenderers[i];
                if (renderer == null)
                    continue;

                renderer.enabled = shouldHide ? false : baseRendererEnabled[i];
            }
        }

        if (cachedColliders != null)
        {
            for (int i = 0; i < cachedColliders.Length; i++)
            {
                Collider2D collider2D = cachedColliders[i];
                if (collider2D == null)
                    continue;

                collider2D.enabled = shouldHide ? false : baseColliderEnabled[i];
            }
        }

        if (shouldHide)
        {
            if (IsPreMatchLocked())
            {
                for (int i = 0; i < rigidbodies.Count; i++)
                {
                    Rigidbody2D rb = rigidbodies[i];
                    if (rb == null)
                        continue;

                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                    rb.simulated = false;
                }
            }
            else
            {
                ApplySimulationMode();
            }

            return;
        }

        ApplySimulationMode();
    }

    void TryApplyInitialSpawnSpread(bool ignoreMatchStartDelay = false)
    {
        if (initialSpawnPositionApplied)
            return;

        if (!spreadPlayersOnNetworkSpawn)
            return;

        if (!ignoreMatchStartDelay && IsInitialSpawnDelayActive())
            return;

        if (!IsOwner)
            return;

        if (rigidbodies.Count == 0)
            return;

        if (!TryGetResolvedSpawnPosition(out Vector2 targetPosition))
            return;

        Vector2 currentRootPosition = transform.position;
        Vector2 delta = targetPosition - currentRootPosition;

        for (int i = 0; i < rigidbodies.Count; i++)
        {
            Rigidbody2D rb = rigidbodies[i];
            if (rb == null)
                continue;

            if (delta.sqrMagnitude > 0.0001f)
                rb.position += delta;

            if (tossInFromAboveOnNetworkSpawn)
                rb.linearVelocity = new Vector2(0f, GetAppliedTossVerticalSpeed());

            rb.angularVelocity = 0f;
            rb.WakeUp();
        }

        if (tossInFromAboveOnNetworkSpawn)
            ApplyRootLaunchVelocity();

        if (delta.sqrMagnitude > 0.0001f)
            transform.position = targetPosition;

        if (IsOwner && tossInFromAboveOnNetworkSpawn && Mathf.Abs(GetAppliedTossVerticalSpeed()) > 0.001f)
            remainingOwnerTossFrames = Mathf.Max(remainingOwnerTossFrames, enforceOwnerTossFrames);

        initialSpawnPositionApplied = true;
    }

    void PlaceAtResolvedSpawnWithoutLaunch()
    {
        if (rigidbodies.Count == 0)
            return;

        if (!TryGetResolvedSpawnPosition(out Vector2 targetPosition))
            return;

        Vector2 currentRootPosition = transform.position;
        Vector2 delta = targetPosition - currentRootPosition;

        if (delta.sqrMagnitude <= 0.0001f)
            return;

        for (int i = 0; i < rigidbodies.Count; i++)
        {
            Rigidbody2D rb = rigidbodies[i];
            if (rb == null)
                continue;

            rb.position += delta;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        transform.position = targetPosition;
    }

    bool TryGetResolvedSpawnPosition(out Vector2 targetPosition)
    {
        targetPosition = transform.position;

        float spacing = Mathf.Max(0.1f, networkSpawnSpacing);
        Vector2 spawnBase = networkSpawnCenter;

        if (tossInFromAboveOnNetworkSpawn)
        {
            if (TryGetSceneSpawnZonePosition(out Vector2 spawnZonePoint))
            {
                spawnBase = spawnZonePoint;
            }
            else
            {
                if (requireSceneSpawnZone)
                {
                    WarnMissingSpawnZoneOnce();
                    return false;
                }

                if (TryGetSceneTossSpawnPoint(out Vector2 tossSpawnPoint))
                {
                    spawnBase = tossSpawnPoint;
                    targetPosition = spawnBase + Vector2.right * GetSpawnOffsetByClientId(OwnerClientId, spacing);
                    return true;
                }

                Camera cam = Camera.main;

                if (cam == null)
                {
                    Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    if (cameras.Length > 0)
                        cam = cameras[0];
                }

                if (cam != null)
                {
                    float topY;

                    if (cam.orthographic)
                        topY = cam.transform.position.y + cam.orthographicSize;
                    else
                    {
                        Vector3 topCenter = cam.ViewportToWorldPoint(new Vector3(0.5f, 1f, Mathf.Abs(cam.transform.position.z)));
                        topY = topCenter.y;
                    }

                    spawnBase = new Vector2(cam.transform.position.x, topY + tossHeightAboveCamera);
                }
                else
                {
                    spawnBase = new Vector2(networkSpawnCenter.x, networkSpawnCenter.y + tossHeightAboveCamera);
                }
            }
        }

        targetPosition = spawnBase + Vector2.right * GetSpawnOffsetByClientId(OwnerClientId, spacing);
        return true;
    }

    bool TryGetSceneSpawnZonePosition(out Vector2 spawnPoint)
    {
        spawnPoint = default;

        if (!preferSceneSpawnZone)
            return false;

        CrabSpawnZone[] zones = FindObjectsByType<CrabSpawnZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (zones == null || zones.Length == 0)
            return false;

        if (!string.IsNullOrWhiteSpace(sceneSpawnZoneName))
        {
            for (int i = 0; i < zones.Length; i++)
            {
                CrabSpawnZone zone = zones[i];
                if (zone == null)
                    continue;

                if (zone.name != sceneSpawnZoneName)
                    continue;

                return zone.TryGetSpawnPosition(OwnerClientId, networkSpawnSpacing, out spawnPoint);
            }
        }

        return zones[0].TryGetSpawnPosition(OwnerClientId, networkSpawnSpacing, out spawnPoint);
    }

    void WarnMissingSpawnZoneOnce()
    {
        if (warnedMissingSpawnZone)
            return;

        warnedMissingSpawnZone = true;
        if (requireSceneSpawnZone)
            Debug.LogWarning($"[{nameof(CrabNetworkSync2D)}] No CrabSpawnZone found in active scene. Spawn is blocked until a zone is present.");
        else
            Debug.LogWarning($"[{nameof(CrabNetworkSync2D)}] No CrabSpawnZone found in active scene. Falling back to toss-point/camera spawn.");
    }

    bool IsInitialSpawnDelayActive()
    {
        if (!NetworkStart.DelayInitialSpawnUntilHostStart)
            return false;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening)
            return false;

        return !NetworkStart.IsMatchStarted;
    }

    public void TriggerCoordinatedSpawnFromHost()
    {
        if (!IsSpawned)
            return;

        if (IsOwner)
        {
            UpdatePreMatchPresentation();

            initialSpawnPositionApplied = false;
            TryApplyInitialSpawnSpread(true);

            if (tossInFromAboveOnNetworkSpawn && Mathf.Abs(GetAppliedTossVerticalSpeed()) > 0.001f)
                remainingOwnerTossFrames = Mathf.Max(remainingOwnerTossFrames, enforceOwnerTossFrames);

            WriteStatesFromOwner();

            return;
        }

        if (!IsServer)
            return;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };

        TriggerOwnerCoordinatedSpawnClientRpc(clientRpcParams);
    }

    [ClientRpc]
    void TriggerOwnerCoordinatedSpawnClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
            return;

        UpdatePreMatchPresentation();

        initialSpawnPositionApplied = false;
        TryApplyInitialSpawnSpread(true);

        if (tossInFromAboveOnNetworkSpawn && Mathf.Abs(GetAppliedTossVerticalSpeed()) > 0.001f)
            remainingOwnerTossFrames = Mathf.Max(remainingOwnerTossFrames, enforceOwnerTossFrames);

        WriteStatesFromOwner();
    }

    bool TryGetSceneTossSpawnPoint(out Vector2 spawnPoint)
    {
        spawnPoint = default;

        if (!preferSceneTossSpawnPoint)
            return false;

        if (string.IsNullOrWhiteSpace(sceneTossSpawnPointName))
            return false;

        GameObject tossPoint = GameObject.Find(sceneTossSpawnPointName);
        if (tossPoint == null)
            return false;

        spawnPoint = tossPoint.transform.position;
        return true;
    }

    void ApplyOwnerTossVelocity()
    {
        if (!tossInFromAboveOnNetworkSpawn)
            return;

        float appliedTossSpeed = GetAppliedTossVerticalSpeed();
        if (Mathf.Abs(appliedTossSpeed) <= 0.001f)
            return;

        for (int i = 0; i < rigidbodies.Count; i++)
        {
            Rigidbody2D rb = rigidbodies[i];
            if (rb == null)
                continue;

            Vector2 velocity = rb.linearVelocity;
            velocity.y = appliedTossSpeed;
            rb.linearVelocity = velocity;
            rb.WakeUp();
        }

        ApplyRootLaunchVelocity();
    }

    void ApplyRootLaunchVelocity()
    {
        if (rootBody == null)
            rootBody = GetComponent<Rigidbody2D>();

        if (rootBody == null)
            return;

        float appliedTossSpeed = GetAppliedTossVerticalSpeed();
        Vector2 rootVelocity = rootBody.linearVelocity;
        rootVelocity.y = appliedTossSpeed;
        rootBody.linearVelocity = rootVelocity;
        rootBody.WakeUp();
    }

    float GetAppliedTossVerticalSpeed()
    {
        if (!tossTowardGround)
            return tossInitialVerticalSpeed;

        return -Mathf.Abs(tossInitialVerticalSpeed);
    }

    float GetSpawnOffsetByClientId(ulong clientId, float spacing)
    {
        int pairIndex = (int)(clientId / 2);
        bool isEven = (clientId % 2) == 0;
        float fullStep = (pairIndex + 1f) * spacing;

        return isEven ? -fullStep : fullStep;
    }

    public override void OnGainedOwnership()
    {
        ApplySimulationMode();
        WriteStatesFromOwner();
    }

    public override void OnLostOwnership()
    {
        ApplySimulationMode();
    }

    void ResolveRigidbodies()
    {
        if (rigidbodies.Count > 0)
            return;

        Rigidbody2D[] found = GetComponentsInChildren<Rigidbody2D>(true);
        Array.Sort(found, (a, b) => string.CompareOrdinal(GetTransformPath(a.transform), GetTransformPath(b.transform)));

        for (int i = 0; i < found.Length; i++)
            rigidbodies.Add(found[i]);

        // Populate lastAppliedStates with current state for interpolation
        lastAppliedStates.Clear();
        for (int i = 0; i < rigidbodies.Count; i++)
        {
            Rigidbody2D rb = rigidbodies[i];
            lastAppliedStates.Add(new RigidbodyState
            {
                Position = rb.position,
                Rotation = rb.rotation,
                LinearVelocity = rb.linearVelocity,
                AngularVelocity = rb.angularVelocity
            });
        }
    }

    string GetTransformPath(Transform target)
    {
        string path = target.name;
        Transform current = target.parent;

        while (current != null && current != transform)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    void EnsureStateListSize()
    {
        if (!IsOwner)
            return;

        if (syncedStates.Count == rigidbodies.Count)
            return;

        syncedStates.Clear();
        for (int i = 0; i < rigidbodies.Count; i++)
            syncedStates.Add(default);
    }

    void ApplySimulationMode()
    {
        bool shouldSimulate = !IsSpawned || IsOwner || !disableRemotePhysicsSimulation;

        for (int i = 0; i < rigidbodies.Count; i++)
        {
            Rigidbody2D rb = rigidbodies[i];
            if (rb != null)
                rb.simulated = shouldSimulate;
        }
    }

    void WriteStatesFromOwner()
    {
        if (!IsOwner)
            return;

        EnsureStateListSize();
        if (syncedStates.Count != rigidbodies.Count)
            return;

        for (int i = 0; i < rigidbodies.Count; i++)
        {
            Rigidbody2D rb = rigidbodies[i];
            if (rb == null)
                continue;

            syncedStates[i] = new RigidbodyState
            {
                Position = rb.position,
                Rotation = rb.rotation,
                LinearVelocity = rb.linearVelocity,
                AngularVelocity = rb.angularVelocity
            };
        }
    }

    void ApplyStatesToRemote()
    {
        if (IsOwner)
            return;

        if (syncedStates.Count != rigidbodies.Count)
            return;

        // Ensure lastAppliedStates has correct size
        while (lastAppliedStates.Count < rigidbodies.Count)
            lastAppliedStates.Add(default);

        bool waitingForFirstStateAfterStart = IsWaitingForRemoteFirstState();
        bool receivedMeaningfulState = false;

        for (int i = 0; i < rigidbodies.Count; i++)
        {
            Rigidbody2D rb = rigidbodies[i];
            if (rb == null)
                continue;

            RigidbodyState targetState = syncedStates[i];
            RigidbodyState lastState = lastAppliedStates[i];
            Vector2 positionError = targetState.Position - rb.position;
            float rotationError = Mathf.DeltaAngle(rb.rotation, targetState.Rotation);

            if (positionError.sqrMagnitude > 0.0001f
                || Mathf.Abs(rotationError) > 0.1f
                || targetState.LinearVelocity.sqrMagnitude > 0.0001f
                || Mathf.Abs(targetState.AngularVelocity) > 0.01f)
            {
                receivedMeaningfulState = true;
            }

            if (waitingForFirstStateAfterStart)
            {
                rb.position = targetState.Position;
                rb.rotation = targetState.Rotation;
                rb.linearVelocity = targetState.LinearVelocity;
                rb.angularVelocity = targetState.AngularVelocity;

                lastAppliedStates[i] = new RigidbodyState
                {
                    Position = targetState.Position,
                    Rotation = targetState.Rotation,
                    LinearVelocity = targetState.LinearVelocity,
                    AngularVelocity = targetState.AngularVelocity
                };

                receivedMeaningfulState = true;
                continue;
            }

            if (!disableRemotePhysicsSimulation)
            {
                rb.linearVelocity = targetState.LinearVelocity + positionError * remotePositionCorrection;
                rb.angularVelocity = targetState.AngularVelocity + rotationError * remoteRotationCorrection;

                lastAppliedStates[i] = new RigidbodyState
                {
                    Position = rb.position,
                    Rotation = rb.rotation,
                    LinearVelocity = rb.linearVelocity,
                    AngularVelocity = rb.angularVelocity
                };

                continue;
            }

            // Lerp position for smooth movement between network updates
            Vector2 smoothedPos = Vector2.Lerp(lastState.Position, targetState.Position, interpolationLerp);
            
            // Lerp rotation for smooth turning
            float smoothedRot = Mathf.LerpAngle(lastState.Rotation, targetState.Rotation, interpolationLerp);

            rb.transform.SetPositionAndRotation(smoothedPos, Quaternion.Euler(0f, 0f, smoothedRot));
            rb.linearVelocity = targetState.LinearVelocity;
            rb.angularVelocity = targetState.AngularVelocity;

            // Update lastAppliedStates for next frame's lerp
            lastAppliedStates[i] = new RigidbodyState
            {
                Position = smoothedPos,
                Rotation = smoothedRot,
                LinearVelocity = targetState.LinearVelocity,
                AngularVelocity = targetState.AngularVelocity
            };
        }

        if (receivedMeaningfulState && !hasRemoteStateAfterMatchStart)
        {
            hasRemoteStateAfterMatchStart = true;
            UpdatePreMatchPresentation();
        }
    }

    void FixedUpdate()
    {
        if (!IsSpawned || rigidbodies.Count == 0)
            return;

        UpdatePreMatchPresentation();
        if (preMatchHidden && (IsOwner || IsPreMatchLocked()))
            return;

        if (!initialSpawnPositionApplied && IsOwner && !IsInitialSpawnDelayActive())
            TryApplyInitialSpawnSpread();

        if (IsOwner)
        {
            if (remainingOwnerTossFrames > 0)
            {
                ApplyOwnerTossVelocity();
                remainingOwnerTossFrames--;
            }

            WriteStatesFromOwner();
            return;
        }

        ApplyStatesToRemote();
    }

    public override void OnDestroy()
    {
        if (syncedStates != null)
            syncedStates.Dispose();

        base.OnDestroy();
    }
}
