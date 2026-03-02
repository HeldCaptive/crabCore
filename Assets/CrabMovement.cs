using UnityEngine;
using UnityEngine.InputSystem;

public class CrabMovement : MonoBehaviour
{
    [SerializeField] float walkSpeed = 6f;

    Rigidbody2D rb;
    ClawGrab2D grab;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        grab = GetComponentInParent<ClawGrab2D>();
    }

    void FixedUpdate()
    {
        if (Gamepad.current == null) return;

        if (grab != null && grab.AnyClamped)
        {
            rb.linearVelocity =
                new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        float move = 0f;

        if (Gamepad.current.leftShoulder.isPressed)
            move = -1f;

        if (Gamepad.current.rightShoulder.isPressed)
            move = 1f;

        rb.linearVelocity =
            new Vector2(move * walkSpeed, rb.linearVelocity.y);
    }
}