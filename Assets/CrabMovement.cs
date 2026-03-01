using UnityEngine;
using UnityEngine.InputSystem;

public class CrabMovement : MonoBehaviour
{
    [SerializeField] float moveSpeed = 5f;

    Rigidbody2D rb;
    Vector2 moveInput;

    ContactPoint2D[] contacts = new ContactPoint2D[8];

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    void FixedUpdate()
    {
        if (!IsGrounded())
            return;

        rb.linearVelocity =
            new Vector2(moveInput.x * moveSpeed, rb.linearVelocity.y);
    }

    bool IsGrounded()
    {
        int count = rb.GetContacts(contacts);

        for (int i = 0; i < count; i++)
        {
            if (contacts[i].normal.y > 0.5f)
                return true;
        }

        return false;
    }
}