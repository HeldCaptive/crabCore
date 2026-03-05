using UnityEngine;
using UnityEngine.InputSystem;

public class ClawGrab2D : MonoBehaviour
{
    [SerializeField] Rigidbody2D leftClaw;
    [SerializeField] Rigidbody2D rightClaw;

    [SerializeField] LayerMask grabbableLayers;
    [SerializeField] float grabRadius = 0.2f;
    [SerializeField] float jointFrequency = 30f;
    [SerializeField] float jointDampingRatio = 2f;  // Over-damping for stability
    [SerializeField] float clawLinearDamping = 10f;
    [SerializeField] float clawAngularDamping = 10f;

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
        if (playerInput == null)
            return;

        Gamepad gamepad = playerInput.AssignedGamepad;

        if (gamepad == null) return;

        float leftTrigger = gamepad.leftTrigger.ReadValue();
        float rightTrigger = gamepad.rightTrigger.ReadValue();

        if (leftTrigger > 0.5f)
            TryGrab(leftClaw, ref leftJoint);
        else
            Release(ref leftJoint);

        if (rightTrigger > 0.5f)
            TryGrab(rightClaw, ref rightJoint);
        else
            Release(ref rightJoint);
    }

    void TryGrab(Rigidbody2D claw, ref FixedJoint2D joint)
    {
        if (claw == null || joint != null) return;

        Collider2D hit =
            Physics2D.OverlapCircle(claw.position, grabRadius, grabbableLayers);

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
}