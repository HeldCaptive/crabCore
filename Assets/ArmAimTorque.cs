using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(HingeJoint2D))]
public class ArmAimTorque : MonoBehaviour
{
    public float torqueStrength = 200f;
    public float damping = 10f;
    public float angleOffset = 0f;

    Camera cam;
    Rigidbody2D rb;
    HingeJoint2D hinge;

    void Awake()
    {
        rb.inertia = 1f;
        cam = Camera.main;
        rb = GetComponent<Rigidbody2D>();
        hinge = GetComponent<HingeJoint2D>();
    }

    void FixedUpdate()
    {
        if (Mouse.current == null || cam == null) return;

        Vector2 mouseWorld =
            cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());

        Vector2 pivot =
            hinge.connectedBody.transform.TransformPoint(hinge.connectedAnchor);

        Vector2 dir = mouseWorld - pivot;
        if (dir.sqrMagnitude < 0.001f) return;

        float targetWorldAngle =
            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + angleOffset;

        float connectedWorldAngle =
            hinge.connectedBody.rotation;

        float targetJointAngle =
            Mathf.DeltaAngle(connectedWorldAngle, targetWorldAngle);

        float currentJointAngle =
            hinge.jointAngle;

        float error =
            Mathf.DeltaAngle(currentJointAngle, targetJointAngle);

        float torque =
            error * torqueStrength
            - rb.angularVelocity * damping;

        rb.AddTorque(torque);
        Debug.Log($"{name} | BodyType: {rb.bodyType} | Simulated: {rb.simulated}"); 
    }
}