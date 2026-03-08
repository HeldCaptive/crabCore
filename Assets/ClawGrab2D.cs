using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ClawGrab2D : MonoBehaviour
{
    [SerializeField] Rigidbody2D leftClaw;
    [SerializeField] Rigidbody2D rightClaw;
    [SerializeField] Transform leftClawTip;   // Actual pinch point for left claw
    [SerializeField] Transform rightClawTip;  // Actual pinch point for right claw
    [SerializeField] Vector2 leftClawGrabOffset = Vector2.zero;   // Offset for left claw grab point
    [SerializeField] Vector2 rightClawGrabOffset = Vector2.zero;  // Offset for right claw grab point
 
    [SerializeField] LayerMask grabbableLayers;
    [SerializeField] float grabRadius = 0.2f;
    [SerializeField] float jointFrequency = 30f;
    [SerializeField] float jointDampingRatio = 2f;  // Over-damping for stability
    [SerializeField] float clawLinearDamping = 10f;
    [SerializeField] float clawAngularDamping = 10f;

    [Header("Slippery Clamp")]
    [SerializeField] float normalClampBreakForce = 100000f;
    [SerializeField] float slipperyClampBreakForce = 200f;
    [SerializeField] float normalClampBreakTorque = 100000f;
    [SerializeField] float slipperyClampBreakTorque = 200f;
    [SerializeField, Range(0.05f, 1f)] float slipperyJointFrequencyScale = 0.4f;
    [SerializeField, Range(0.05f, 1f)] float slipperyJointDampingScale = 0.4f;
    [SerializeField] float slipperySlideForce = 40f;

    [SerializeField] bool showDebugRadius = true;  // Toggle debug visualization

    FixedJoint2D leftJoint;
    FixedJoint2D rightJoint;
    float leftJointSlipperiness;
    float rightJointSlipperiness;
    CrabPlayerInput playerInput;
    Transform ownerRoot;
    Collider2D[] ownColliders = new Collider2D[0];

    public bool AnyClamped => leftJoint != null || rightJoint != null;

    void Awake()
    {
        ResolveReferencesIfMissing();
        playerInput = GetComponentInParent<CrabPlayerInput>();
        ownerRoot = playerInput != null ? playerInput.transform : transform.root;
        ConfigureCollisionBehavior();
    }

    void ResolveReferencesIfMissing()
    {
        if (leftClaw == null)
            leftClaw = FindRigidbody2DByName("LeftClaw");

        if (rightClaw == null)
            rightClaw = FindRigidbody2DByName("RightClaw");

        if (leftClawTip == null)
            leftClawTip = FindChildTransformByName("LeftClawTip");

        if (rightClawTip == null)
            rightClawTip = FindChildTransformByName("RightClawTip");
    }

    Rigidbody2D FindRigidbody2DByName(string targetName)
    {
        Rigidbody2D[] bodies = GetComponentsInChildren<Rigidbody2D>(true);

        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody2D body = bodies[i];
            if (body == null)
                continue;

            if (body.name == targetName)
                return body;
        }

        return null;
    }

    Transform FindChildTransformByName(string targetName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null)
                continue;

            if (child.name == targetName)
                return child;
        }

        return null;
    }

    void Update()
    {
        Gamepad gamepad = playerInput != null
            ? playerInput.AssignedGamepad
            : null;

        if (gamepad == null) return;

        float leftTrigger = gamepad.leftTrigger.ReadValue();
        float rightTrigger = gamepad.rightTrigger.ReadValue();

        if (leftTrigger > 0.5f)
            TryGrab(leftClaw, leftClawTip, ref leftJoint, leftClawGrabOffset, true);
        else
            Release(ref leftJoint, true);

        if (rightTrigger > 0.5f)
            TryGrab(rightClaw, rightClawTip, ref rightJoint, rightClawGrabOffset, false);
        else
            Release(ref rightJoint, false);
    }

    void TryGrab(Rigidbody2D claw, Transform clawTip, ref FixedJoint2D joint, Vector2 grabOffset, bool isLeftClaw)
    {
        if (claw == null || joint != null) return;

        // Use tip position if assigned, otherwise fall back to rigidbody position
        Vector2 basePosition = clawTip != null ? (Vector2)clawTip.position : claw.position;
        Vector2 grabPoint = basePosition + grabOffset;
        Collider2D hit = FindGrabTarget(grabPoint, claw);

        if (hit == null) return;

        float slipperiness = GetSurfaceSlipperiness(hit);

        joint = claw.gameObject.AddComponent<FixedJoint2D>();
        joint.enableCollision = false;
        joint.breakForce = Mathf.Lerp(normalClampBreakForce, slipperyClampBreakForce, slipperiness);
        joint.breakTorque = Mathf.Lerp(normalClampBreakTorque, slipperyClampBreakTorque, slipperiness);
        joint.dampingRatio = jointDampingRatio * Mathf.Lerp(1f, slipperyJointDampingScale, slipperiness);
        joint.frequency = jointFrequency * Mathf.Lerp(1f, slipperyJointFrequencyScale, slipperiness);
        joint.autoConfigureConnectedAnchor = true;

        SetJointSlipperiness(isLeftClaw, slipperiness);

        // Add high velocity damping to claw to prevent stretching
        claw.linearDamping = clawLinearDamping;
        claw.angularDamping = clawAngularDamping;

        if (hit.attachedRigidbody != null)
            joint.connectedBody = hit.attachedRigidbody;
        else
            joint.connectedBody = null;
    }

    float GetSurfaceSlipperiness(Collider2D hit)
    {
        if (hit == null)
            return 0f;

        ClampSurface2D clampSurface = hit.GetComponentInParent<ClampSurface2D>();
        if (clampSurface == null)
            return 0f;

        return clampSurface.Slipperiness;
    }

    void SetJointSlipperiness(bool isLeftClaw, float slipperiness)
    {
        if (isLeftClaw)
            leftJointSlipperiness = slipperiness;
        else
            rightJointSlipperiness = slipperiness;
    }

    void ConfigureCollisionBehavior()
    {
        ownColliders = GetComponentsInChildren<Collider2D>(true);
        if (ownColliders.Length == 0)
            return;

        HashSet<int> crabLayers = new HashSet<int>();

        for (int i = 0; i < ownColliders.Length; i++)
        {
            Collider2D collider = ownColliders[i];
            if (collider == null)
                continue;

            crabLayers.Add(collider.gameObject.layer);
        }

        int[] layers = new int[crabLayers.Count];
        crabLayers.CopyTo(layers);

        for (int i = 0; i < layers.Length; i++)
        {
            for (int j = i; j < layers.Length; j++)
                Physics2D.IgnoreLayerCollision(layers[i], layers[j], false);
        }

        for (int i = 0; i < ownColliders.Length; i++)
        {
            Collider2D first = ownColliders[i];
            if (first == null)
                continue;

            for (int j = i + 1; j < ownColliders.Length; j++)
            {
                Collider2D second = ownColliders[j];
                if (second == null)
                    continue;

                Physics2D.IgnoreCollision(first, second, true);
            }
        }
    }

    Collider2D FindGrabTarget(Vector2 grabPoint, Rigidbody2D claw)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(grabPoint, grabRadius, grabbableLayers);
        Collider2D bestHit = null;
        float bestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D candidate = hits[i];
            if (candidate == null || !candidate.enabled)
                continue;

            if (IsSelfCollider(candidate))
                continue;

            if (candidate.attachedRigidbody == claw)
                continue;

            Vector2 candidatePoint = candidate.ClosestPoint(grabPoint);
            float distanceSqr = (candidatePoint - grabPoint).sqrMagnitude;

            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                bestHit = candidate;
            }
        }

        return bestHit;
    }

    bool IsSelfCollider(Collider2D candidate)
    {
        if (candidate == null)
            return false;

        if (ownerRoot != null && candidate.transform.IsChildOf(ownerRoot))
            return true;

        if (playerInput == null)
            return false;

        CrabPlayerInput candidateOwner = candidate.GetComponentInParent<CrabPlayerInput>();
        return candidateOwner != null && candidateOwner == playerInput;
    }

    void Release(ref FixedJoint2D joint, bool isLeftClaw)
    {
        if (joint == null) return;
        // Reset damping when releasing
        Rigidbody2D rb = joint.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
        }
        Destroy(joint);
        joint = null;

        if (isLeftClaw)
            leftJointSlipperiness = 0f;
        else
            rightJointSlipperiness = 0f;
    }

    void FixedUpdate()
    {
        // Limit velocity of grabbed claws to prevent joint stretching
        ConstrainClawVelocity(leftClaw, leftJoint);
        ConstrainClawVelocity(rightClaw, rightJoint);

        ApplySlipperySlide(leftClaw, leftJoint, leftJointSlipperiness);
        ApplySlipperySlide(rightClaw, rightJoint, rightJointSlipperiness);
    }

    void ApplySlipperySlide(Rigidbody2D claw, FixedJoint2D joint, float slipperiness)
    {
        if (claw == null || joint == null)
            return;

        if (slipperiness <= 0f)
            return;

        Vector2 gravityDir = Physics2D.gravity.sqrMagnitude > 0.0001f
            ? Physics2D.gravity.normalized
            : Vector2.down;

        float force = slipperySlideForce * slipperiness;
        claw.AddForce(gravityDir * force, ForceMode2D.Force);
    }

    void ConstrainClawVelocity(Rigidbody2D claw, FixedJoint2D joint)
    {
        if (claw == null || joint == null) return;

        // Clamp linear velocity to prevent excessive pulling
        float maxVelocity = 15f;
        if (claw.linearVelocity.sqrMagnitude > maxVelocity * maxVelocity)
        {
            claw.linearVelocity = claw.linearVelocity.normalized * maxVelocity;
        }

        // Clamp angular velocity to prevent excessive rotation
        float maxAngular = 360f;
        if (Mathf.Abs(claw.angularVelocity) > maxAngular)
        {
            claw.angularVelocity = Mathf.Sign(claw.angularVelocity) * maxAngular;
        }
    }

    void OnDrawGizmos()
    {
        // Show gizmos during play mode
        if (!showDebugRadius || !Application.isPlaying)
            return;

        if (NetworkStart.DelayInitialSpawnUntilHostStart && !NetworkStart.IsMatchStarted)
            return;

        if (playerInput != null && !playerInput.HasInputAuthority)
            return;

        // Draw grab radius for left claw
        if (leftClaw != null)
        {
            Vector2 leftPos = leftClawTip != null ? (Vector2)leftClawTip.position : leftClaw.position;
            Gizmos.color = leftJoint != null ? Color.red : Color.green;
            DrawGizmoCircle(leftPos + leftClawGrabOffset, grabRadius);
        }

        // Draw grab radius for right claw
        if (rightClaw != null)
        {
            Vector2 rightPos = rightClawTip != null ? (Vector2)rightClawTip.position : rightClaw.position;
            Gizmos.color = rightJoint != null ? Color.red : Color.green;
            DrawGizmoCircle(rightPos + rightClawGrabOffset, grabRadius);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugRadius)
            return;

        if (NetworkStart.DelayInitialSpawnUntilHostStart && !NetworkStart.IsMatchStarted)
            return;

        if (Application.isPlaying && playerInput != null && !playerInput.HasInputAuthority)
            return;

        // Draw grab radius for left claw
        if (leftClaw != null)
        {
            Vector2 leftPos = leftClawTip != null ? (Vector2)leftClawTip.position : leftClaw.position;
            Gizmos.color = leftJoint != null ? Color.red : Color.green;
            DrawGizmoCircle(leftPos + leftClawGrabOffset, grabRadius);
        }

        // Draw grab radius for right claw
        if (rightClaw != null)
        {
            Vector2 rightPos = rightClawTip != null ? (Vector2)rightClawTip.position : rightClaw.position;
            Gizmos.color = rightJoint != null ? Color.red : Color.green;
            DrawGizmoCircle(rightPos + rightClawGrabOffset, grabRadius);
        }
    }

    void DrawGizmoCircle(Vector2 center, float radius)
    {
        const int segments = 20;
        float angleStep = 360f / segments;

        Vector2 lastPos = center + new Vector2(radius, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector2 newPos = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(lastPos, newPos);
            lastPos = newPos;
        }
    }
}