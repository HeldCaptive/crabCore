using UnityEngine;
using UnityEngine.InputSystem;

public class CrabMovement : MonoBehaviour
{
    [SerializeField] float maxMoveSpeed = 6f;
    [SerializeField] float acceleration = 30f;
    [SerializeField] float deceleration = 8f;

    Rigidbody2D rb;
    ClawGrab2D grab;
    CrabPlayerInput playerInput;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        grab = GetComponentInParent<ClawGrab2D>();
        playerInput = GetComponentInParent<CrabPlayerInput>();
    }

    void FixedUpdate()
    {
        if (playerInput == null)
            return;

        Gamepad gamepad = playerInput.AssignedGamepad;

        if (gamepad == null) return;

        if (grab != null && grab.AnyClamped)
            return;

        float move = 0f;

        if (gamepad.leftShoulder.isPressed)
            move = -1f;

        if (gamepad.rightShoulder.isPressed)
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