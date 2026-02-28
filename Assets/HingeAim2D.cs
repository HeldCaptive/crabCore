using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(HingeJoint2D))]
public class HingeAim2D : MonoBehaviour
{
    [SerializeField] float motorStrength = 200f;
    [SerializeField] float maxMotorSpeed = 200f;
    [SerializeField] bool invert;

    Camera cam;
    HingeJoint2D hinge;

    void Awake()
    {
        cam = Camera.main;
        hinge = GetComponent<HingeJoint2D>();
        hinge.useMotor = true;
    }

    void FixedUpdate()
    {
        if (Mouse.current == null) return;

        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
        mouseWorld.z = 0f;

        Vector2 dir = mouseWorld - transform.position;

        float desiredAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (invert)
            desiredAngle += 180f;

        float currentAngle = hinge.jointAngle;

        float error = Mathf.DeltaAngle(currentAngle, desiredAngle);

        float speed = Mathf.Clamp(error * 5f, -maxMotorSpeed, maxMotorSpeed);

        JointMotor2D m = hinge.motor;
        m.motorSpeed = speed;
        m.maxMotorTorque = motorStrength;
        hinge.motor = m;
    }
}