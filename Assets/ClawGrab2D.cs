using UnityEngine;
using UnityEngine.InputSystem;

public class ClawGrab2D : MonoBehaviour
{
    [SerializeField] Rigidbody2D leftClaw;
    [SerializeField] Rigidbody2D rightClaw;

    [SerializeField] Collider2D leftClawCollider;
    [SerializeField] Collider2D rightClawCollider;

    [SerializeField] LayerMask grabbableLayers;
    [SerializeField] float grabRadius = 0.2f;

    FixedJoint2D leftJoint;
    FixedJoint2D rightJoint;

    public bool AnyClamped => leftJoint != null || rightJoint != null;

    void Update()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
            TryGrab(leftClaw, leftClawCollider, ref leftJoint);

        if (Mouse.current.leftButton.wasReleasedThisFrame)
            Release(ref leftJoint);

        if (Mouse.current.rightButton.wasPressedThisFrame)
            TryGrab(rightClaw, rightClawCollider, ref rightJoint);

        if (Mouse.current.rightButton.wasReleasedThisFrame)
            Release(ref rightJoint);
    }

    void TryGrab(Rigidbody2D claw, Collider2D col, ref FixedJoint2D joint)
    {
        if (claw == null || col == null || joint != null) return;

        // Use collider bounds edge instead of center
        Vector2 tipPoint = col.bounds.center + (Vector3)(claw.transform.right * col.bounds.extents.x);

        Collider2D hit =
            Physics2D.OverlapCircle(tipPoint, grabRadius, grabbableLayers);

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

    void OnDrawGizmosSelected()
    {
        if (leftClawCollider != null)
        {
            Vector2 tip =
                leftClawCollider.bounds.center +
                (Vector3)(leftClaw.transform.right * leftClawCollider.bounds.extents.x);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(tip, grabRadius);
        }

        if (rightClawCollider != null)
        {
            Vector2 tip =
                rightClawCollider.bounds.center +
                (Vector3)(rightClaw.transform.right * rightClawCollider.bounds.extents.x);

            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(tip, grabRadius);
        }
    }
}