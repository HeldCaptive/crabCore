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
    [SerializeField] bool showDebugRadius = true;  // Toggle debug visualization

    FixedJoint2D leftJoint;
    FixedJoint2D rightJoint;
    CrabPlayerInput playerInput;

    public bool AnyClamped => leftJoint != null || rightJoint != null;

    void Awake()
    {
        playerInput = GetComponentInParent<CrabPlayerInput>();
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
            TryGrab(leftClaw, leftClawTip, ref leftJoint, leftClawGrabOffset);
        else
            Release(ref leftJoint);

        if (rightTrigger > 0.5f)
            TryGrab(rightClaw, rightClawTip, ref rightJoint, rightClawGrabOffset);
        else
            Release(ref rightJoint);
    }

    void TryGrab(Rigidbody2D claw, Transform clawTip, ref FixedJoint2D joint, Vector2 grabOffset)
    {
        if (claw == null || joint != null) return;

        // Use tip position if assigned, otherwise fall back to rigidbody position
        Vector2 basePosition = clawTip != null ? (Vector2)clawTip.position : claw.position;
        Vector2 grabPoint = basePosition + grabOffset;
        Collider2D hit =
            Physics2D.OverlapCircle(grabPoint, grabRadius, grabbableLayers);

        if (hit == null) return;

        joint = claw.gameObject.AddComponent<FixedJoint2D>();
        joint.enableCollision = false;
        joint.breakForce = Mathf.Infinity;
        joint.breakTorque = Mathf.Infinity;
        joint.dampingRatio = jointDampingRatio;  // Over-damping prevents oscillation
        joint.frequency = jointFrequency;     // Higher frequency = tighter constraint
        joint.autoConfigureConnectedAnchor = true;

        // Add high velocity damping to claw to prevent stretching
        claw.linearDamping = clawLinearDamping;
        claw.angularDamping = clawAngularDamping;

        if (hit.attachedRigidbody != null)
            joint.connectedBody = hit.attachedRigidbody;
        else
            joint.connectedBody = null;
    }

    void Release(ref FixedJoint2D joint)
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
    }

    void FixedUpdate()
    {
        // Limit velocity of grabbed claws to prevent joint stretching
        ConstrainClawVelocity(leftClaw, leftJoint);
        ConstrainClawVelocity(rightClaw, rightJoint);
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