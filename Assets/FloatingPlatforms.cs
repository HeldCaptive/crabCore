using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class FloatingPlatforms : MonoBehaviour
{
    const string ObstacleSyncMessageName = "FloatingPlatforms_State";
    static bool messageHandlerRegistered;
    static NetworkManager messageHandlerOwner;
    static readonly Dictionary<ulong, FloatingPlatforms> instancesById = new Dictionary<ulong, FloatingPlatforms>();

    [Header("Anchor")]
    [SerializeField] Transform anchorPoint;
    [SerializeField] Rigidbody2D obstacleBody;
    [SerializeField] LayerMask obstacleLayers = ~0;
    [SerializeField, Min(0.01f)] float obstacleSearchRadius = 0.25f;
    [SerializeField] bool requireBoxCollider = true;
    [SerializeField] bool attachOnStart = true;

    [Header("Buoyancy")]
    [SerializeField, Range(0f, 1.5f)] float gravityCompensation = 0.95f;
    [SerializeField] bool useWaterLine = true;
    [SerializeField] bool waterLineFollowsObject = true;
    [SerializeField] Transform waterLineReference;
    [SerializeField] float waterLineLocalOffset = 0f;
    [SerializeField] float waterLineY = 0f;

    [Header("Bobbing")]
    [SerializeField] bool enableBobbing = true;
    [SerializeField, Min(0f)] float bobDistance = 0.35f;
    [SerializeField, Min(0f)] float bobFrequency = 0.25f;
    [SerializeField] bool randomizeBobPhaseOnStart = true;
    [SerializeField, Range(0f, 360f)] float bobPhaseDegrees = 0f;

    [SerializeField, Min(0f)] float buoyancySpring = 20f;
    [SerializeField, Min(0f)] float buoyancyDamping = 6f;
    [SerializeField, Min(0f)] float waterLinearDamping = 2f;
    [SerializeField, Min(0f)] float waterAngularDamping = 2f;

    [Header("Visual")]
    [SerializeField] Transform visualTransform;
    [SerializeField] bool autoFindVisualFromSprite = true;
    [SerializeField] bool detachVisualFromPhysics = true;

    [Header("Network Sync")]
    [SerializeField] bool hostAuthoritativeSync = true;
    [SerializeField, Min(0f)] float clientPositionLerp = 18f;
    [SerializeField, Min(0f)] float clientRotationLerp = 18f;

    Rigidbody2D rb;
    HingeJoint2D hinge;
    float baseLinearDamping;
    float baseAngularDamping;
    float initialPlacementY;
    bool visualDetached;
    Vector3 visualOffsetLocal;
    float visualAngleOffset;
    Vector3 detachedVisualScale = Vector3.one;
    RigidbodyType2D authorityBodyType;
    bool authoritySimulated;
    ulong obstacleSyncId;
    bool networkIdentityInitialized;
    bool clientReplicaMode;
    bool hasReceivedNetworkState;
    Vector2 networkTargetPosition;
    float networkTargetRotation;
    Vector2 networkTargetLinearVelocity;
    float networkTargetAngularVelocity;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        hinge = GetComponent<HingeJoint2D>();
        initialPlacementY = GetCurrentBodyY();
        authorityBodyType = rb.bodyType;
        authoritySimulated = rb.simulated;

        EnsureNetworkIdentity();
        RegisterInstance();
        TryRegisterNetworkHandler();

        ClearInvalidCopiedWaterLineReference();

        baseLinearDamping = rb.linearDamping;
        baseAngularDamping = rb.angularDamping;

        SetupVisualFollower();

        if (randomizeBobPhaseOnStart)
            bobPhaseDegrees = Random.Range(0f, 360f);

        if (attachOnStart)
            AttachToAnchor();
    }

    void OnEnable()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        initialPlacementY = GetCurrentBodyY();

        EnsureNetworkIdentity();
        RegisterInstance();
        TryRegisterNetworkHandler();
    }

    void OnDisable()
    {
        UnregisterInstance();
    }

    void FixedUpdate()
    {
        TryRegisterNetworkHandler();

        if (IsClientReplica())
        {
            ApplyClientReplicaState();
            return;
        }

        SetClientReplicaMode(false);
        ApplyBuoyancy();
        ApplyWaterDamping();

        if (ShouldBroadcastState())
            BroadcastState();
    }

    void LateUpdate()
    {
        SyncDetachedVisual();
    }

    void SetupVisualFollower()
    {
        if (visualTransform == null && autoFindVisualFromSprite)
            visualTransform = FindVisualTransformFromChildren();

        if (visualTransform == null || visualTransform == transform)
            return;

        visualOffsetLocal = transform.InverseTransformPoint(visualTransform.position);
        visualAngleOffset = Mathf.DeltaAngle(transform.eulerAngles.z, visualTransform.eulerAngles.z);

        if (!detachVisualFromPhysics)
            return;

        detachedVisualScale = visualTransform.lossyScale;
        visualTransform.SetParent(null, true);
        visualDetached = true;
    }

    Transform FindVisualTransformFromChildren()
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = renderers[i];
            if (spriteRenderer == null)
                continue;

            if (spriteRenderer.transform == transform)
                continue;

            return spriteRenderer.transform;
        }

        return null;
    }

    void SyncDetachedVisual()
    {
        if (!visualDetached || visualTransform == null)
            return;

        Vector3 worldPosition = transform.TransformPoint(visualOffsetLocal);
        visualTransform.position = new Vector3(worldPosition.x, worldPosition.y, visualTransform.position.z);

        float zRotation = transform.eulerAngles.z + visualAngleOffset;
        visualTransform.rotation = Quaternion.Euler(0f, 0f, zRotation);
        visualTransform.localScale = detachedVisualScale;
    }

    public bool AttachToAnchor()
    {
        if (anchorPoint == null)
            return false;

        if (obstacleBody == null)
            obstacleBody = FindObstacleBodyAtAnchor();

        if (obstacleBody == null || obstacleBody == rb)
            return false;

        if (hinge == null)
            hinge = gameObject.AddComponent<HingeJoint2D>();

        Vector2 worldAnchor = anchorPoint.position;

        hinge.connectedBody = obstacleBody;
        hinge.autoConfigureConnectedAnchor = false;
        hinge.anchor = transform.InverseTransformPoint(worldAnchor);
        hinge.connectedAnchor = obstacleBody.transform.InverseTransformPoint(worldAnchor);
        hinge.enableCollision = false;

        return true;
    }

    void ApplyBuoyancy()
    {
        Vector2 gravityForce = Physics2D.gravity * rb.gravityScale * rb.mass;
        rb.AddForce(-gravityForce * gravityCompensation, ForceMode2D.Force);

        if (!useWaterLine)
            return;

        float depth = GetEffectiveWaterLineY() - rb.worldCenterOfMass.y;
        if (depth <= 0f)
            return;

        float springForce = depth * buoyancySpring * rb.mass;
        float dampingForce = -rb.linearVelocity.y * buoyancyDamping * rb.mass;
        float upwardForce = springForce + dampingForce;

        rb.AddForce(Vector2.up * upwardForce, ForceMode2D.Force);
    }

    void ApplyWaterDamping()
    {
        rb.linearDamping = baseLinearDamping + waterLinearDamping;
        rb.angularDamping = baseAngularDamping + waterAngularDamping;
    }

    Rigidbody2D FindObstacleBodyAtAnchor()
    {
        if (anchorPoint == null)
            return null;

        Vector2 anchorWorld = anchorPoint.position;
        Collider2D[] hits = Physics2D.OverlapCircleAll(anchorWorld, obstacleSearchRadius, obstacleLayers);

        float bestDistance = float.PositiveInfinity;
        Rigidbody2D bestBody = null;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null)
                continue;

            if (hit.transform.IsChildOf(transform))
                continue;

            if (requireBoxCollider && !(hit is BoxCollider2D))
                continue;

            Rigidbody2D hitBody = hit.attachedRigidbody;
            if (hitBody == null)
                continue;

            float distance = ((Vector2)hit.transform.position - anchorWorld).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestBody = hitBody;
            }
        }

        return bestBody;
    }

    float GetEffectiveWaterLineY()
    {
        float bobOffset = GetBobOffsetY();

        if (waterLineFollowsObject)
        {
            if (waterLineReference != null)
                return waterLineReference.position.y + waterLineLocalOffset + bobOffset;

            float baselineY = Application.isPlaying
                ? initialPlacementY
                : GetCurrentBodyY();

            return baselineY + waterLineLocalOffset + bobOffset;
        }

        return waterLineY + bobOffset;
    }

    float GetCurrentBodyY()
    {
        Rigidbody2D body = rb != null ? rb : GetComponent<Rigidbody2D>();
        if (body != null)
            return body.worldCenterOfMass.y;

        return transform.position.y;
    }

    float GetBobOffsetY()
    {
        if (!enableBobbing || bobDistance <= 0f || bobFrequency <= 0f)
            return 0f;

        float phaseRadians = bobPhaseDegrees * Mathf.Deg2Rad;
        float angle = (Time.time * bobFrequency * Mathf.PI * 2f) + phaseRadians;
        return Mathf.Sin(angle) * bobDistance;
    }

    void ClearInvalidCopiedWaterLineReference()
    {
        if (waterLineReference == null)
            return;

        FloatingPlatforms owner = waterLineReference.GetComponentInParent<FloatingPlatforms>();
        if (owner != null && owner != this)
            waterLineReference = null;
    }

    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        if (waterLineReference == transform)
            waterLineReference = null;
    }

    void OnDrawGizmosSelected()
    {
        if (anchorPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(anchorPoint.position, obstacleSearchRadius);
        }

        if (useWaterLine)
        {
            Gizmos.color = Color.blue;
            float lineY = GetEffectiveWaterLineY();
            float lineCenterX = rb != null ? rb.worldCenterOfMass.x : transform.position.x;
            Vector3 left = new Vector3(lineCenterX - 5f, lineY, 0f);
            Vector3 right = new Vector3(lineCenterX + 5f, lineY, 0f);
            Gizmos.DrawLine(left, right);
        }
    }

    void OnDestroy()
    {
        UnregisterInstance();

        if (!visualDetached || visualTransform == null)
            return;

        if (Application.isPlaying)
            Destroy(visualTransform.gameObject);
    }

    void EnsureNetworkIdentity()
    {
        if (networkIdentityInitialized)
            return;

        obstacleSyncId = ComputeStableId(transform);
        networkIdentityInitialized = true;
    }

    static ulong ComputeStableId(Transform target)
    {
        const ulong fnvOffsetBasis = 1469598103934665603UL;
        const ulong fnvPrime = 1099511628211UL;

        ulong hash = fnvOffsetBasis;
        string sceneName = target.gameObject.scene.name;

        for (int c = 0; c < sceneName.Length; c++)
        {
            hash ^= sceneName[c];
            hash *= fnvPrime;
        }

        var chain = new List<Transform>(8);
        Transform current = target;

        while (current != null)
        {
            chain.Add(current);
            current = current.parent;
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            string segment = chain[i].name;
            for (int c = 0; c < segment.Length; c++)
            {
                hash ^= segment[c];
                hash *= fnvPrime;
            }

            hash ^= '/';
            hash *= fnvPrime;
        }

        Vector3 worldPos = target.position;
        int posX = Mathf.RoundToInt(worldPos.x * 1000f);
        int posY = Mathf.RoundToInt(worldPos.y * 1000f);
        int posZ = Mathf.RoundToInt(worldPos.z * 1000f);

        hash ^= (uint)posX;
        hash *= fnvPrime;
        hash ^= (uint)posY;
        hash *= fnvPrime;
        hash ^= (uint)posZ;
        hash *= fnvPrime;

        return hash;
    }

    void RegisterInstance()
    {
        if (!networkIdentityInitialized)
            EnsureNetworkIdentity();

        instancesById[obstacleSyncId] = this;
    }

    void UnregisterInstance()
    {
        if (!networkIdentityInitialized)
            return;

        if (instancesById.TryGetValue(obstacleSyncId, out FloatingPlatforms current) && current == this)
            instancesById.Remove(obstacleSyncId);
    }

    void TryRegisterNetworkHandler()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.CustomMessagingManager == null)
            return;

        if (messageHandlerRegistered && messageHandlerOwner == manager)
            return;

        if (messageHandlerRegistered && messageHandlerOwner != null && messageHandlerOwner.CustomMessagingManager != null)
            messageHandlerOwner.CustomMessagingManager.UnregisterNamedMessageHandler(ObstacleSyncMessageName);

        manager.CustomMessagingManager.RegisterNamedMessageHandler(ObstacleSyncMessageName, OnObstacleStateMessage);
        messageHandlerRegistered = true;
        messageHandlerOwner = manager;
    }

    static void OnObstacleStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong obstacleId);
        reader.ReadValueSafe(out Vector2 position);
        reader.ReadValueSafe(out float rotation);
        reader.ReadValueSafe(out Vector2 linearVelocity);
        reader.ReadValueSafe(out float angularVelocity);

        if (!instancesById.TryGetValue(obstacleId, out FloatingPlatforms instance) || instance == null)
            return;

        instance.ReceiveNetworkState(position, rotation, linearVelocity, angularVelocity);
    }

    void ReceiveNetworkState(Vector2 position, float rotation, Vector2 linearVelocity, float angularVelocity)
    {
        networkTargetPosition = position;
        networkTargetRotation = rotation;
        networkTargetLinearVelocity = linearVelocity;
        networkTargetAngularVelocity = angularVelocity;
        hasReceivedNetworkState = true;
    }

    bool IsClientReplica()
    {
        if (!hostAuthoritativeSync)
            return false;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening)
            return false;

        return !manager.IsServer;
    }

    bool ShouldBroadcastState()
    {
        if (!hostAuthoritativeSync)
            return false;

        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && manager.IsListening && manager.IsServer;
    }

    void SetClientReplicaMode(bool enabled)
    {
        if (clientReplicaMode == enabled)
            return;

        clientReplicaMode = enabled;

        if (rb == null)
            return;

        if (enabled)
        {
            rb.simulated = true;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        else
        {
            rb.bodyType = authorityBodyType;
            rb.simulated = authoritySimulated;
        }
    }

    void ApplyClientReplicaState()
    {
        SetClientReplicaMode(true);

        if (!hasReceivedNetworkState || rb == null)
            return;

        float positionT = clientPositionLerp <= 0f ? 1f : Mathf.Clamp01(clientPositionLerp * Time.fixedDeltaTime);
        float rotationT = clientRotationLerp <= 0f ? 1f : Mathf.Clamp01(clientRotationLerp * Time.fixedDeltaTime);

        Vector2 nextPosition = Vector2.Lerp(rb.position, networkTargetPosition, positionT);
        float nextRotation = Mathf.LerpAngle(rb.rotation, networkTargetRotation, rotationT);

        rb.MovePosition(nextPosition);
        rb.MoveRotation(nextRotation);
    }

    void BroadcastState()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.CustomMessagingManager == null)
            return;

        const int messageSizeBytes = sizeof(ulong) + (sizeof(float) * 6);
        using var writer = new FastBufferWriter(messageSizeBytes, Allocator.Temp);
        writer.WriteValueSafe(obstacleSyncId);
        writer.WriteValueSafe(rb.position);
        writer.WriteValueSafe(rb.rotation);
        writer.WriteValueSafe(rb.linearVelocity);
        writer.WriteValueSafe(rb.angularVelocity);
        manager.CustomMessagingManager.SendNamedMessageToAll(
            ObstacleSyncMessageName,
            writer,
            NetworkDelivery.UnreliableSequenced);
    }
}
