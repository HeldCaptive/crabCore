using UnityEngine;
using UnityEngine.InputSystem;

public class ClawGrab2D : MonoBehaviour
{
    [SerializeField] Rigidbody2D leftClaw;
    [SerializeField] Rigidbody2D rightClaw;

    [SerializeField] LayerMask grabbableLayers;

    FixedJoint2D leftJoint;
    FixedJoint2D rightJoint;

    void Update()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
            TryGrab(leftClaw, ref leftJoint);

        if (Mouse.current.leftButton.wasReleasedThisFrame)
            Release(ref leftJoint);

        if (Mouse.current.rightButton.wasPressedThisFrame)
            TryGrab(rightClaw, ref rightJoint);

        if (Mouse.current.rightButton.wasReleasedThisFrame)
            Release(ref rightJoint);
    }

    void TryGrab(Rigidbody2D clawRb, ref FixedJoint2D joint)
    {
        if (clawRb == null || joint != null) return;

        Collider2D hit = Physics2D.OverlapPoint(clawRb.position, grabbableLayers);
        if (hit == null) return;

        joint = clawRb.gameObject.AddComponent<FixedJoint2D>();

        if (hit.attachedRigidbody != null)
            joint.connectedBody = hit.attachedRigidbody;
        else
            joint.connectedBody = null; // attaches to world

        joint.breakForce = Mathf.Infinity;
        joint.breakTorque = Mathf.Infinity;
    }

    void Release(ref FixedJoint2D joint)
    {
        if (joint == null) return;
        Destroy(joint);
        joint = null;
    }
}