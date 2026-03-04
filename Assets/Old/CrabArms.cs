using UnityEngine;
using UnityEngine.InputSystem;

public class CrabArms : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform leftArm;
    [SerializeField] Transform rightArm;
    [SerializeField] Transform leftClaw;
    [SerializeField] Transform rightClaw;

    [Header("Arm Settings")]
    [SerializeField] float armRotationSpeed = 360f;
    [SerializeField] float armSpreadAngle = 15f;
    [SerializeField] float armSmoothTime = 0.1f; // lower = snappier, higher = more wobble

    [Header("Claw Settings")]
    [SerializeField] float clawRotationSpeed = 720f;
    [SerializeField] float clawMaxBend = 45f;    // prevents hyperextension
    [SerializeField] float leftClawRotationOffset = -130f;
    [SerializeField] float rightClawRotationOffset = 140f;
    [SerializeField] float clawSmoothTime = 0.08f;

    Camera cam;

    // Velocity tracking for spring damping
    float leftArmVelocity;
    float rightArmVelocity;
    float leftClawVelocity;
    float rightClawVelocity;

    void Awake()
    {
        cam = Camera.main;
    }

    void Update()
    {
        if (Mouse.current == null) return;

        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
        mouseWorld.z = 0f;

        AimArm(leftArm, leftClaw, mouseWorld, true, ref leftArmVelocity, ref leftClawVelocity);
        AimArm(rightArm, rightClaw, mouseWorld, false, ref rightArmVelocity, ref rightClawVelocity);
    }

    void AimArm(Transform arm, Transform claw, Vector3 target, bool isLeftArm, ref float armVelocity, ref float clawVelocity)
    {
        Vector2 dir = target - arm.position;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // Spread so arms don't overlap
        if (isLeftArm)
            angle += 180f + armSpreadAngle;
        else
            angle -= armSpreadAngle;

        // Convert to local angle relative to parent
        float parentRotation = arm.parent.eulerAngles.z;
        float localAngle = Mathf.DeltaAngle(0f, angle - parentRotation);

        // Spring damped rotation for rubbery feel
        float currentAngle = arm.localEulerAngles.z;
        if (currentAngle > 180f) currentAngle -= 360f;
        
        float newAngle = Mathf.SmoothDampAngle(currentAngle, localAngle, ref armVelocity, armSmoothTime, armRotationSpeed);
        arm.localRotation = Quaternion.Euler(0f, 0f, newAngle);

        // -------- CLAW ROTATION (relative to arm) --------

        Vector2 clawDir = target - claw.position;
        float desiredWorldAngle = Mathf.Atan2(clawDir.y, clawDir.x) * Mathf.Rad2Deg;

        // Flip left claw to face correct direction
        if (isLeftArm)
            desiredWorldAngle += 180f;

        float armWorldAngle = arm.eulerAngles.z;
        float relativeAngle = Mathf.DeltaAngle(armWorldAngle, desiredWorldAngle);

        // Clamp wrist bend
        relativeAngle = Mathf.Clamp(relativeAngle, -clawMaxBend, clawMaxBend);

        // Apply sprite orientation offset
        relativeAngle += isLeftArm ? leftClawRotationOffset : rightClawRotationOffset;

        // Spring damped claw rotation for rubbery feel
        float currentClawAngle = claw.localEulerAngles.z;
        if (currentClawAngle > 180f) currentClawAngle -= 360f;
        
        float newClawAngle = Mathf.SmoothDampAngle(currentClawAngle, relativeAngle, ref clawVelocity, clawSmoothTime, clawRotationSpeed);
        claw.localRotation = Quaternion.Euler(0f, 0f, newClawAngle);
    }
}