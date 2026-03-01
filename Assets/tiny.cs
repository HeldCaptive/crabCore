using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(HingeJoint2D))]
public class ArmMotorAim : MonoBehaviour
{
    public float speedGain = 6f;
    public float maxSpeed = 250f;
    public float maxTorque = 300f;

    public float angleOffset = 0f;
    public bool invert = false;
    public bool flipHorizontal = true;

    HingeJoint2D hinge;
    Camera cam;

    void Awake()
    {
        hinge = GetComponent<HingeJoint2D>();
        cam = Camera.main;

        hinge.useMotor = true;
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

        if (flipHorizontal)
            dir.x = -dir.x;

        float targetWorld =
            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + angleOffset;

        float bodyAngle =
            hinge.connectedBody.rotation;

        float targetJoint =
            Mathf.DeltaAngle(bodyAngle, targetWorld);

        float error =
            Mathf.DeltaAngle(hinge.jointAngle, targetJoint);

        if (invert)
            error = -error;

        JointMotor2D m = hinge.motor;
        m.motorSpeed = Mathf.Clamp(error * speedGain, -maxSpeed, maxSpeed);
        m.maxMotorTorque = maxTorque;
        hinge.motor = m;
    }
}