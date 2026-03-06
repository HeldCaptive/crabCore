using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkStart : MonoBehaviour
{
    bool showHostUI;
    bool showJoinChoiceUI;
    bool showJoinRelayUI;
    bool showJoinCodeUI;

    string joinCodeInput = "";
    string activeJoinCode = "";
    string connectionStatus = "";

    bool isBusy;
    bool isConnectingClient;
    float connectStartTime;
    const float connectTimeoutSeconds = 12f;

    bool callbacksBound;
    bool servicesReady;
    bool servicesInitializing;

    GUIStyle textFieldStyle;
    GUIStyle buttonStyle;
    GUIStyle bigButtonStyle;
    GUIStyle statusStyle;
    bool stylesInitialized;

    void Update()
    {
        EnsureCallbacksBound();

        if (Keyboard.current == null || NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsListening)
        {
            if (!isBusy && Keyboard.current.hKey.wasPressedThisFrame)
            {
                showHostUI = true;
                showJoinChoiceUI = false;
                showJoinRelayUI = false;
                connectionStatus = "";
            }

            if (!isBusy && Keyboard.current.jKey.wasPressedThisFrame)
            {
                showJoinChoiceUI = true;
                showHostUI = false;
                showJoinRelayUI = false;
                connectionStatus = "";
                if (!string.IsNullOrEmpty(GUIUtility.systemCopyBuffer))
                    joinCodeInput = GUIUtility.systemCopyBuffer.Trim();
            }
        }

        if ((showHostUI || showJoinChoiceUI || showJoinRelayUI) && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            showHostUI = false;
            showJoinChoiceUI = false;
            showJoinRelayUI = false;
            if (!isConnectingClient)
                connectionStatus = "";
        }

        if (isConnectingClient && Time.unscaledTime - connectStartTime > connectTimeoutSeconds)
        {
            if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsConnectedClient)
                NetworkManager.Singleton.Shutdown();

            isConnectingClient = false;
            isBusy = false;
            showJoinChoiceUI = true;
            showJoinRelayUI = true;
            connectionStatus = "Connection timed out. Verify host is online and try again.";
        }
    }

    void OnGUI()
    {
        InitializeStyles();

        if (showHostUI)
            DrawHostMenu();
        else if (showJoinChoiceUI)
            DrawJoinChoiceMenu();
        else if (showJoinRelayUI)
            DrawJoinRelayMenu();

        if (showJoinCodeUI && !string.IsNullOrEmpty(activeJoinCode))
            DrawJoinCodeOverlay();
    }

    void DrawHostMenu()
    {
        float width = 420f;
        float height = 210f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(10);
        GUILayout.Label("Host - Choose Type", GUI.skin.label);
        GUILayout.Space(10);

        if (GUILayout.Button("Quick Test Local\nStart host on this machine", bigButtonStyle, GUILayout.Height(50)))
            StartQuickLocalHost();

        GUILayout.Space(5);

        if (GUILayout.Button(isBusy ? "Starting Relay Host..." : "External (Relay Join Code)\nNo port forwarding needed", bigButtonStyle, GUILayout.Height(50)))
        {
            if (!isBusy)
                _ = StartRelayHostAsync();
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Cancel (ESC)", buttonStyle, GUILayout.Height(25)))
            showHostUI = false;

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawJoinChoiceMenu()
    {
        float width = 430f;
        float height = 210f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(10);
        GUILayout.Label("Join - Choose Type", GUI.skin.label);
        GUILayout.Space(10);

        if (GUILayout.Button("Quick Connect Local\nConnect to 127.0.0.1:7777", bigButtonStyle, GUILayout.Height(50)))
            StartQuickLocalClient();

        GUILayout.Space(5);

        if (GUILayout.Button("Join External (Relay Code)\nEnter code from host", bigButtonStyle, GUILayout.Height(50)))
        {
            showJoinChoiceUI = false;
            showJoinRelayUI = true;
            if (!isConnectingClient)
                connectionStatus = "";
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Cancel (ESC)", buttonStyle, GUILayout.Height(25)))
            showJoinChoiceUI = false;

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawJoinRelayMenu()
    {
        float width = 460f;
        float height = 250f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(10);

        GUILayout.Label("Join External - Enter Relay Join Code", GUI.skin.label);
        GUILayout.Space(5);
        joinCodeInput = GUILayout.TextField(joinCodeInput, textFieldStyle, GUILayout.Height(35));
        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Paste", buttonStyle, GUILayout.Height(30)))
            joinCodeInput = GUIUtility.systemCopyBuffer.Trim();

        if (GUILayout.Button(isBusy ? "Connecting..." : "Connect", buttonStyle, GUILayout.Height(30)))
        {
            if (!isBusy)
                _ = JoinRelayAsync(joinCodeInput.Trim());
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(5);
        if (!string.IsNullOrEmpty(connectionStatus))
        {
            GUILayout.Label(connectionStatus, statusStyle);
            GUILayout.Space(5);
        }

        if (!isBusy && !string.IsNullOrWhiteSpace(joinCodeInput) && GUILayout.Button("Retry", buttonStyle, GUILayout.Height(25)))
            _ = JoinRelayAsync(joinCodeInput.Trim());

        GUILayout.Space(5);
        if (GUILayout.Button("Back", buttonStyle, GUILayout.Height(25)))
        {
            showJoinRelayUI = false;
            showJoinChoiceUI = true;
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawJoinCodeOverlay()
    {
        float width = 430f;
        float height = 105f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, 20f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(5);
        GUILayout.Label("Relay Join Code (share this):", GUI.skin.label);

        if (GUILayout.Button(activeJoinCode, bigButtonStyle, GUILayout.Height(34)))
        {
            GUIUtility.systemCopyBuffer = activeJoinCode;
            showJoinCodeUI = false;
            connectionStatus = "Join code copied.";
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void StartQuickLocalHost()
    {
        if (NetworkManager.Singleton == null)
            return;

        isBusy = false;
        isConnectingClient = false;
        showHostUI = false;
        showJoinCodeUI = false;
        activeJoinCode = "";
        connectionStatus = "";

        if (NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData("0.0.0.0", 7777, "0.0.0.0");

        bool started = NetworkManager.Singleton.StartHost();
        if (!started)
            connectionStatus = "Failed to start local host. Port 7777 may already be in use.";
    }

    void StartQuickLocalClient()
    {
        showJoinChoiceUI = false;
        showJoinRelayUI = false;
        connectionStatus = "";
        ConnectLocalClient("127.0.0.1");
    }

    void ConnectLocalClient(string address)
    {
        if (NetworkManager.Singleton == null)
            return;

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            connectionStatus = "UnityTransport missing on NetworkManager.";
            return;
        }

        if (NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        transport.SetConnectionData(address, 7777);
        bool started = NetworkManager.Singleton.StartClient();
        if (!started)
        {
            connectionStatus = "Could not start local client. Is host running?";
            showJoinChoiceUI = true;
            return;
        }

        isConnectingClient = true;
        isBusy = true;
        connectStartTime = Time.unscaledTime;
        connectionStatus = "Trying local connection...";
    }

    async Task StartRelayHostAsync()
    {
        if (NetworkManager.Singleton == null)
            return;

        isBusy = true;
        connectionStatus = "Initializing Relay services...";

        try
        {
            await EnsureUnityServicesAsync();

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                connectionStatus = "UnityTransport missing on NetworkManager.";
                return;
            }

            const int maxConnections = 3;
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));

            bool started = NetworkManager.Singleton.StartHost();
            if (!started)
            {
                connectionStatus = "Failed to start Relay host.";
                return;
            }

            activeJoinCode = joinCode;
            showJoinCodeUI = true;
            showHostUI = false;
            connectionStatus = "Relay host started.";
        }
        catch (Exception ex)
        {
            connectionStatus = $"Relay host failed: {ex.Message}";
            Debug.LogWarning(connectionStatus);
        }
        finally
        {
            isBusy = false;
        }
    }

    async Task JoinRelayAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            connectionStatus = "Enter a relay join code.";
            return;
        }

        if (NetworkManager.Singleton == null)
            return;

        isBusy = true;
        isConnectingClient = true;
        connectStartTime = Time.unscaledTime;
        connectionStatus = "Joining Relay...";

        try
        {
            await EnsureUnityServicesAsync();

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                connectionStatus = "UnityTransport missing on NetworkManager.";
                isConnectingClient = false;
                return;
            }

            if (NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode.ToUpperInvariant());
            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));

            bool started = NetworkManager.Singleton.StartClient();
            if (!started)
            {
                isConnectingClient = false;
                connectionStatus = "Could not start client for this join code. Please retry.";
                showJoinChoiceUI = true;
                showJoinRelayUI = true;
                return;
            }

            joinCodeInput = joinCode.ToUpperInvariant();
            connectionStatus = "Trying to connect via Relay...";
        }
        catch (Exception ex)
        {
            isConnectingClient = false;
            connectionStatus = $"Join failed: {ex.Message}. Check the code and try again.";
            showJoinChoiceUI = true;
            showJoinRelayUI = true;
            Debug.LogWarning(connectionStatus);
        }
        finally
        {
            isBusy = false;
        }
    }

    async Task EnsureUnityServicesAsync()
    {
        if (servicesReady)
            return;

        if (servicesInitializing)
        {
            while (servicesInitializing)
                await Task.Yield();
            return;
        }

        servicesInitializing = true;
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            servicesReady = true;
        }
        finally
        {
            servicesInitializing = false;
        }
    }

    void InitializeStyles()
    {
        if (stylesInitialized)
            return;

        textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 10, 5, 5)
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        bigButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            wordWrap = true,
            normal = { textColor = Color.white }
        };

        stylesInitialized = true;
    }

    void OnEnable()
    {
        EnsureCallbacksBound();
    }

    void OnDisable()
    {
        if (!callbacksBound || NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        callbacksBound = false;
    }

    void EnsureCallbacksBound()
    {
        if (callbacksBound || NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        callbacksBound = true;
    }

    void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        isBusy = false;
        isConnectingClient = false;
        showJoinChoiceUI = false;
        showJoinRelayUI = false;
        connectionStatus = "Connected.";
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        if (!isConnectingClient)
            return;

        isBusy = false;
        isConnectingClient = false;
        showJoinChoiceUI = true;
        showJoinRelayUI = true;
        connectionStatus = "Connection failed. Check host/join code and try again.";
    }

    void OnApplicationQuit()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    void OnDestroy()
    {
        OnDisable();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }
}