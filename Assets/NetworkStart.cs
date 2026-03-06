using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkStart : MonoBehaviour
{
    void Update()
    {
        if (Keyboard.current == null)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.IsListening)
            return;

        if (Keyboard.current.hKey.wasPressedThisFrame)
        {
            Debug.Log("H pressed");
            NetworkManager.Singleton.StartHost();
        }

        if (Keyboard.current.jKey.wasPressedThisFrame)
        {
            Debug.Log("J pressed");
            NetworkManager.Singleton.StartClient();
        }
    }
}