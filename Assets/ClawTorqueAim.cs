using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(HingeJoint2D))]
public class ClawTorqueAim : MonoBehaviour
{
    public float torqueStrength = 8f;
    public float damping = 6f;

    public float pullStrength = 12f;
    public float liftMultiplier = 3f;
    public float maxPullForce = 50f;

    public float angleOffset = 0f;
    public bool invert = false;

    Rigidbody2D rb;
    HingeJoint2D hinge;
    Camera cam;
    ClawGrab2D grab;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        hinge = GetComponent<HingeJoint2D>();
        cam = Camera.main;
        grab = GetComponentInParent<ClawGrab2D>();
    }

    void FixedUpdate()
    {
        if (Mouse.current == null || cam == null) return;

        Vector2 mouse =
            cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());

        Vector2 pivot =
            hinge.connectedBody.transform.TransformPoint(hinge.connectedAnchor);

        Vector2 dir = mouse - pivot;
        if (dir.sqrMagnitude < 0.001f) return;

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
            float strength = pullStrength;

            if (grab != null && grab.AnyClamped)
                strength *= liftMultiplier;

            Vector2 pullDir =
                (mouse - rb.position).normalized;

            Vector2 pull =
                Vector2.ClampMagnitude(pullDir * strength, maxPullForce);

            rb.AddForce(pull);
        }
    }
}