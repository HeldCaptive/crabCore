using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class CrabPlayerInput : MonoBehaviour
{
    [SerializeField, Min(1)] int playerNumber = 1;
    [SerializeField] bool useNetcodeOwnership = true;
    [SerializeField, Min(1)] int onlineGamepadNumber = 1;

    NetworkObject cachedNetworkObject;

    public int PlayerNumber => Mathf.Max(1, playerNumber);
    public int PlayerIndex => PlayerNumber - 1;

    public bool IsOnlineSessionActive
    {
        get
        {
            return NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsListening;
        }
    }

    public bool HasInputAuthority
    {
        get
        {
            if (!useNetcodeOwnership || !IsOnlineSessionActive)
                return true;

            NetworkObject networkObject = GetNetworkObject();
            return networkObject != null && networkObject.IsOwner;
        }
    }

    public Gamepad AssignedGamepad
    {
        get
        {
            if (!HasInputAuthority)
                return null;

            int index = IsOnlineSessionActive
                ? onlineGamepadNumber - 1
                : PlayerIndex;

            if (index < 0 || index >= Gamepad.all.Count)
                return null;

            return Gamepad.all[index];
        }
    }

    public void SetPlayerNumber(int value)
    {
        playerNumber = Mathf.Max(1, value);
    }

    public void SetOnlineGamepadNumber(int value)
    {
        onlineGamepadNumber = Mathf.Max(1, value);
    }

    NetworkObject GetNetworkObject()
    {
        if (cachedNetworkObject == null)
            cachedNetworkObject = GetComponentInParent<NetworkObject>();

        return cachedNetworkObject;
    }

    void OnValidate()
    {
        playerNumber = Mathf.Max(1, playerNumber);
        onlineGamepadNumber = Mathf.Max(1, onlineGamepadNumber);
    }
}