using UnityEngine;
using UnityEngine.InputSystem;

public class CrabMovement : MonoBehaviour
{
    [SerializeField] float maxMoveSpeed = 6f;
    [SerializeField] float acceleration = 30f;
    [SerializeField] float deceleration = 8f;

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
            return;

        float move = 0f;

        if (Gamepad.current.leftShoulder.isPressed)
            move = -1f;

        if (Gamepad.current.rightShoulder.isPressed)
            move = 1f;

        float targetSpeed = move * maxMoveSpeed;
        float rate = Mathf.Abs(move) > 0.01f
            ? acceleration
            : deceleration;

        float nextX = Mathf.MoveTowards(
            rb.linearVelocity.x,
            targetSpeed,
            rate * Time.fixedDeltaTime);

        rb.linearVelocity =
            new Vector2(nextX, rb.linearVelocity.y);
    }
}