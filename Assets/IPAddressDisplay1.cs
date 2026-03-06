using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;

public class IPAddressDisplay : MonoBehaviour
{
    string localIP = "";
    bool showIP = false;
    bool useExternal = false;
    Rect ipRect;
    GUIStyle buttonStyle;
    bool stylesInitialized = false;

    void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnHostStarted;
        }
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnHostStarted;
        }
    }

    void OnHostStarted()
    {
        // This is called automatically, but we'll wait for ShowIP to be called with the choice
    }

    public void ShowIP(bool external)
    {
        useExternal = external;
        showIP = true;
        
        if (useExternal)
        {
            StartCoroutine(FetchExternalIP());
        }
        else
        {
            localIP = GetLocalIPAddress();
            Debug.Log($"Host started. Local IP: {localIP}");
        }
    }

    void OnGUI()
    {
        if (!showIP || string.IsNullOrEmpty(localIP))
            return;

        InitializeStyles();

        // Position at top center of screen
        float width = 400f;
        float height = 80f;
        ipRect = new Rect(
            (Screen.width - width) * 0.5f,
            20f,
            width,
            height
        );

        // Draw background box
        GUI.Box(ipRect, "");

        // Draw label and button
        GUILayout.BeginArea(ipRect);
        GUILayout.BeginVertical();
        GUILayout.Space(5);
        
        string label = useExternal ? "External IP (click to copy):" : "Local IP (click to copy):";
        GUILayout.Label(label, GUI.skin.label);
        
        if (GUILayout.Button(localIP, buttonStyle, GUILayout.Height(30)))
        {
            CopyToClipboard(localIP);
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void InitializeStyles()
    {
        if (stylesInitialized)
            return;

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        stylesInitialized = true;
    }

    void CopyToClipboard(string text)
    {
        GUIUtility.systemCopyBuffer = text;
        Debug.Log($"IP Address copied to clipboard: {text}");
        showIP = false;  // Close the display after copying
    }

    IEnumerator FetchExternalIP()
    {
        localIP = "Fetching IP...";

        // Try primary service
        using (UnityWebRequest request = UnityWebRequest.Get("https://api.ipify.org"))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                localIP = request.downloadHandler.text.Trim();
                Debug.Log($"Host started. External IP: {localIP}");
                yield break;
            }
        }

        // Fallback service if primary fails
        using (UnityWebRequest request = UnityWebRequest.Get("https://checkip.amazonaws.com"))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                localIP = request.downloadHandler.text.Trim();
                Debug.Log($"Host started. External IP: {localIP}");
                yield break;
            }
        }

        // If both fail
        localIP = "Unable to get external IP";
        Debug.LogWarning("Could not fetch external IP address");
    }

    string GetLocalIPAddress()
    {
        try
        {
            // Get all network interfaces
            var host = Dns.GetHostEntry(Dns.GetHostName());
            
            // Find the first IPv4 address that's not loopback
            var ipAddress = host.AddressList
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork 
                                   && !IPAddress.IsLoopback(ip));

            if (ipAddress != null)
            {
                return ipAddress.ToString();
            }

            // Fallback: try to get IP by connecting to external address
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                if (endPoint != null)
                {
                    return endPoint.Address.ToString();
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Could not get local IP address: {ex.Message}");
        }

        return "Unable to get local IP";
    }
}
