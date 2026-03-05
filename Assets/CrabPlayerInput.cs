using UnityEngine;
using UnityEngine.InputSystem;

public class CrabPlayerInput : MonoBehaviour
{
    [SerializeField, Min(1)] int playerNumber = 1;

    public int PlayerNumber => Mathf.Max(1, playerNumber);
    public int PlayerIndex => PlayerNumber - 1;

    public Gamepad AssignedGamepad
    {
        get
        {
            int index = PlayerIndex;

            if (index < 0 || index >= Gamepad.all.Count)
                return null;

            return Gamepad.all[index];
        }
    }

    public void SetPlayerNumber(int value)
    {
        playerNumber = Mathf.Max(1, value);
    }

    void OnValidate()
    {
        playerNumber = Mathf.Max(1, playerNumber);
    }
}