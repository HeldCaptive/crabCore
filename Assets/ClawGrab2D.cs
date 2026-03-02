using UnityEngine;
using UnityEngine.InputSystem;

public class ClawGrab2D : MonoBehaviour
{
    [SerializeField] Rigidbody2D leftClaw;
    [SerializeField] Rigidbody2D rightClaw;

    [SerializeField] LayerMask grabbableLayers;
    [SerializeField] float grabRadius = 0.2f;

    FixedJoint2D leftJoint;
    FixedJoint2D rightJoint;

    public bool AnyClamped => leftJoint != null || rightJoint != null;

    void Update()
    {
        if (Gamepad.current == null) return;

        float leftTrigger = Gamepad.current.leftTrigger.ReadValue();
        float rightTrigger = Gamepad.current.rightTrigger.ReadValue();

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

        if (hit.attachedRigidbody != null)
            joint.connectedBody = hit.attachedRigidbody;
        else
            joint.connectedBody = null;
    }

    void Release(ref FixedJoint2D joint)
    {
        if (joint == null) return;
        Destroy(joint);
        joint = null;
    }
}