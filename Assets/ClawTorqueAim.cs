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
    CrabNetworkSync2D networkSync;

    Vector2 aimInput;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        hinge = GetComponent<HingeJoint2D>();
        grab = GetComponentInParent<ClawGrab2D>();
        playerInput = GetComponentInParent<CrabPlayerInput>();
        networkSync = GetComponentInParent<CrabNetworkSync2D>();
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
        if (playerInput != null && !playerInput.HasInputAuthority)
            return;

        if (networkSync != null && networkSync.IsOwnerInSpawnTossPhase)
            return;

        bool isIdleAim = aimInput.sqrMagnitude < 0.05f;
        bool isAnyClamped = grab != null && grab.AnyClamped;

        if (isIdleAim && isAnyClamped)
            return;

        Vector2 dir;

        if (isIdleAim)
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

            if (isIdleAim)
                strength = idleStrength;
            else
                strength = pullStrength;

            if (!isIdleAim && isAnyClamped)
                strength *= liftMultiplier;

            Vector2 pull =
                Vector2.ClampMagnitude(dir * strength, maxPullForce);

            rb.AddForce(pull);
        }
    }
}