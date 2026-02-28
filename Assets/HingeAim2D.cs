using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(HingeJoint2D))]
public class HingeAim2D : MonoBehaviour
{
    [Header("Joint Setup")]
    [SerializeField] bool autoConnectToParent = true;
    [SerializeField] bool allowAncestorFallback;
    [SerializeField] Rigidbody2D connectedBodyOverride;

    [Header("Motor")]
    [SerializeField] float motorStrength = 120f;
    [SerializeField] float maxMotorSpeed = 220f;
    [SerializeField] float aimResponsiveness = 6f;
    [SerializeField] float angularDamping = 0.35f;
    [SerializeField] float stopDeadzone = 1.5f;

    [Header("Aim")]
    [SerializeField] bool invert;
    [SerializeField] float angleOffset;

    Camera cam;
    HingeJoint2D hinge;
    Rigidbody2D rb;

    Vector3 targetWorld;

    void Awake()
    {
        cam = Camera.main;
        hinge = GetComponent<HingeJoint2D>();
        rb = GetComponent<Rigidbody2D>();
        ConfigureJointConnection();
        hinge.useMotor = true;
    }

    void ConfigureJointConnection()
    {
        Rigidbody2D selectedConnectedBody = connectedBodyOverride;
        int parentDepth = -1;

        if (selectedConnectedBody == null && autoConnectToParent)
        {
            Transform parent = transform.parent;
            if (parent != null)
                selectedConnectedBody = parent.GetComponent<Rigidbody2D>();

            if (selectedConnectedBody != null)
            {
                parentDepth = 1;
            }
            else if (allowAncestorFallback)
            {
                selectedConnectedBody = GetComponentInParentExcludingSelf<Rigidbody2D>(out parentDepth);
                if (selectedConnectedBody != null)
                {
                    Debug.LogWarning($"{name}: Direct parent has no Rigidbody2D. Falling back to ancestor body '{selectedConnectedBody.name}' (depth {parentDepth}). This can cause stretching; prefer a Rigidbody2D on the direct parent or set connectedBodyOverride.", this);
                }
            }
            else
            {
                Debug.LogWarning($"{name}: Direct parent has no Rigidbody2D. Add one on the parent or set connectedBodyOverride. Joint may anchor to world.", this);
            }
        }

        if (selectedConnectedBody != null && hinge.connectedBody != selectedConnectedBody)
        {
            Vector2 worldAnchor = rb != null
                ? rb.GetRelativePoint(hinge.anchor)
                : (Vector2)transform.TransformPoint(hinge.anchor);

            hinge.connectedBody = selectedConnectedBody;
            hinge.autoConfigureConnectedAnchor = false;
            hinge.connectedAnchor = selectedConnectedBody.transform.InverseTransformPoint(worldAnchor);
        }

        if (hinge.connectedBody == null)
            Debug.LogWarning($"{name}: HingeJoint2D has no connected body. Joint is anchored to world and may rubberband.", this);
    }

    T GetComponentInParentExcludingSelf<T>(out int depth) where T : Component
    {
        Transform current = transform.parent;
        depth = 1;
        while (current != null)
        {
            T result = current.GetComponent<T>();
            if (result != null) return result;
            current = current.parent;
            depth++;
        }

        depth = -1;
        return null;
    }

    void Update()
    {
        if (Mouse.current == null || cam == null) return;

        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        targetWorld = cam.ScreenToWorldPoint(mouseScreen);
        targetWorld.z = 0f;
    }

    void FixedUpdate()
    {
        if (Mouse.current == null || cam == null) return;

        Vector2 dir = targetWorld - transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        float desiredAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (invert)
            desiredAngle += 180f;

        desiredAngle += angleOffset;

        float currentAngle = rb != null ? rb.rotation : transform.eulerAngles.z;

        float error = Mathf.DeltaAngle(currentAngle, desiredAngle);

        if (Mathf.Abs(error) <= stopDeadzone)
        {
            JointMotor2D stopMotor = hinge.motor;
            stopMotor.motorSpeed = 0f;
            stopMotor.maxMotorTorque = motorStrength;
            hinge.motor = stopMotor;
            return;
        }

        float connectedAngularVelocity = hinge.connectedBody != null ? hinge.connectedBody.angularVelocity : 0f;
        float relativeAngularVelocity = (rb != null ? rb.angularVelocity : 0f) - connectedAngularVelocity;

        float speed = error * aimResponsiveness - relativeAngularVelocity * angularDamping;
        speed = Mathf.Clamp(speed, -maxMotorSpeed, maxMotorSpeed);

        JointMotor2D m = hinge.motor;
        m.motorSpeed = speed;
        m.maxMotorTorque = motorStrength;
        hinge.motor = m;
    }
}