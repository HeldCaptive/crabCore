using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections.Generic;

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

    readonly List<Rigidbody2D> rigidbodies = new List<Rigidbody2D>();
    readonly List<RigidbodyState> lastAppliedStates = new List<RigidbodyState>();
    bool initialSpawnPositionApplied;
    readonly NetworkList<RigidbodyState> syncedStates = new NetworkList<RigidbodyState>(
        null,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

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
        ResolveRigidbodies();
    }

    public override void OnNetworkSpawn()
    {
        ResolveRigidbodies();

        if (IsOwner)
            TryApplyInitialSpawnSpread();

        EnsureStateListSize();
        ApplySimulationMode();

        if (rigidbodies.Count == 0)
            return;

        if (IsOwner)
            WriteStatesFromOwner();
    }

    void TryApplyInitialSpawnSpread()
    {
        if (initialSpawnPositionApplied)
            return;

        if (!spreadPlayersOnNetworkSpawn)
            return;

        if (!IsOwner)
            return;

        if (rigidbodies.Count == 0)
            return;

        float spacing = Mathf.Max(0.1f, networkSpawnSpacing);
        Vector2 targetPosition = networkSpawnCenter + Vector2.right * GetSpawnOffsetByClientId(OwnerClientId, spacing);
        Vector2 currentRootPosition = transform.position;
        Vector2 delta = targetPosition - currentRootPosition;

        if (delta.sqrMagnitude > 0.0001f)
        {
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

        initialSpawnPositionApplied = true;
    }

    float GetSpawnOffsetByClientId(ulong clientId, float spacing)
    {
        int pairIndex = (int)(clientId / 2);
        bool isEven = (clientId % 2) == 0;
        float halfStep = (pairIndex + 0.5f) * spacing;

        return isEven ? -halfStep : halfStep;
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

        for (int i = 0; i < rigidbodies.Count; i++)
        {
            Rigidbody2D rb = rigidbodies[i];
            if (rb == null)
                continue;

            RigidbodyState targetState = syncedStates[i];
            RigidbodyState lastState = lastAppliedStates[i];

            if (!disableRemotePhysicsSimulation)
            {
                Vector2 positionError = targetState.Position - rb.position;
                float rotationError = Mathf.DeltaAngle(rb.rotation, targetState.Rotation);

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
    }

    void FixedUpdate()
    {
        if (!IsSpawned || rigidbodies.Count == 0)
            return;

        if (IsOwner)
        {
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
