using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(HingeJoint2D))]
public class ClawTorqueAim : MonoBehaviour
{
    public enum StickSide
    {
        Left,
        Right
    }

    public float torqueStrength = 8f;
    public float damping = 6f;

    public float pullStrength = 10f;
    public float liftMultiplier = 3f;   // restored
    public float maxPullForce = 40f;

    [Header("Idle")]
    public Vector2 idleDirection = Vector2.down;
    public float idleStrength = 4f;

    public float angleOffset = 0f;
    public bool invert = false;

    [Header("Input")]
    public StickSide stickSide = StickSide.Right;

    Rigidbody2D rb;
    HingeJoint2D hinge;
    ClawGrab2D grab;
    CrabPlayerInput playerInput;

    Vector2 aimInput;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        hinge = GetComponent<HingeJoint2D>();
        grab = GetComponentInParent<ClawGrab2D>();
        playerInput = GetComponentInParent<CrabPlayerInput>();
    }

    void Update()
    {
        Gamepad gamepad = playerInput != null
            ? playerInput.AssignedGamepad
            : null;

        if (gamepad == null)
        {
            aimInput = Vector2.zero;
            return;
        }

        if (stickSide == StickSide.Left)
            aimInput = gamepad.leftStick.ReadValue();
        else
            aimInput = gamepad.rightStick.ReadValue();
    }

    void FixedUpdate()
    {
        Vector2 dir;

        if (aimInput.sqrMagnitude < 0.05f)
            dir = idleDirection.normalized;
        else
            dir = aimInput.normalized;

        float targetAngle =
            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + angleOffset;

        float error =
            Mathf.DeltaAngle(rb.rotation, targetAngle);

        if (invert)
            error = -error;

        float torque =
            error * torqueStrength - rb.angularVelocity * damping;

        rb.AddTorque(torque);

        // Only free claw pulls
        if (GetComponent<FixedJoint2D>() == null)
        {
            float strength;

            if (aimInput.sqrMagnitude < 0.05f)
                strength = idleStrength;
            else
                strength = pullStrength;

            if (grab != null && grab.AnyClamped)
                strength *= liftMultiplier;

            Vector2 pull =
                Vector2.ClampMagnitude(dir * strength, maxPullForce);

            rb.AddForce(pull);
        }
    }
}