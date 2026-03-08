using System;
using System.Collections.Generic;
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
    [Header("Obstacle Loading")]
    [SerializeField] bool delayObstacleActivationUntilModeSelected = true;
    [SerializeField] bool autoFindFloatingObstacles = true;
    [SerializeField] bool autoFindClampSurfaceObstacles = true;
    [SerializeField] GameObject[] obstacleRoots;

    bool showMainMenu = true;  // Show at start
    bool showHostUI;
    bool showHostCodeReadyUI;  // New state: code generated, waiting to start
    bool showJoinChoiceUI;
    bool showJoinRelayUI;
    bool showJoinCodeUI;

    string joinCodeInput = "";
    string activeJoinCode = "";
    string hostJoinCode = "";  // Generated relay code for host
    string connectionStatus = "";
    Allocation relayAllocation;  // Store allocation for later use

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
    GUIStyle selectedButtonStyle;
    GUIStyle codeButtonStyle;
    GUIStyle statusStyle;
    bool stylesInitialized;

    int selectedIndex = 0;
    float navigationCooldown = 0f;
    const float navigationDelay = 0.15f;
    readonly List<GameObject> cachedObstacleRoots = new List<GameObject>();
    bool obstaclesActivatedForSession;
    bool activateObstaclesOnClientConnected;

    void Awake()
    {
        CacheObstacleRoots();

        if (delayObstacleActivationUntilModeSelected)
            SetObstacleRootsActive(false);
    }

    void Update()
    {
        EnsureCallbacksBound();

        if (NetworkManager.Singleton == null)
            return;

        // Hide main menu once connected
        if (NetworkManager.Singleton.IsListening)
        {
            showMainMenu = false;
        }

        // Handle controller navigation
        HandleControllerInput();

        // ESC or B button to go back
        bool backPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                           (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        if ((showHostUI || showHostCodeReadyUI || showJoinChoiceUI || showJoinRelayUI) && backPressed)
        {
            if (showHostCodeReadyUI)
            {
                showHostCodeReadyUI = false;
                showHostUI = true;
                selectedIndex = 0;
            }
            else
            {
                showHostUI = false;
                showJoinChoiceUI = false;
                showJoinRelayUI = false;
                showMainMenu = true;  // Return to main menu
                selectedIndex = 0;
                if (!isConnectingClient)
                    connectionStatus = "";
            }
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

    void HandleControllerInput()
    {
        Gamepad gamepad = Gamepad.current;
        if (gamepad == null)
            return;

        // Update navigation cooldown
        if (navigationCooldown > 0f)
            navigationCooldown -= Time.unscaledDeltaTime;

        // Left stick navigation
        float verticalInput = gamepad.leftStick.ReadValue().y;
        
        if (navigationCooldown <= 0f && Mathf.Abs(verticalInput) > 0.5f)
        {
            if (verticalInput > 0.5f) // Up
            {
                selectedIndex--;
                navigationCooldown = navigationDelay;
            }
            else if (verticalInput < -0.5f) // Down
            {
                selectedIndex++;
                navigationCooldown = navigationDelay;
            }
        }

        // A button to confirm
        if (gamepad.buttonSouth.wasPressedThisFrame)
        {
            ConfirmSelection();
        }
    }

    void ConfirmSelection()
    {
        if (showMainMenu)
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, 1);
            if (selectedIndex == 0) // Host
            {
                showMainMenu = false;
                showHostUI = true;
                selectedIndex = 0;
                connectionStatus = "";
            }
            else if (selectedIndex == 1) // Join
            {
                showMainMenu = false;
                showJoinChoiceUI = true;
                selectedIndex = 0;
                connectionStatus = "";
                if (!string.IsNullOrEmpty(GUIUtility.systemCopyBuffer))
                    joinCodeInput = GUIUtility.systemCopyBuffer.Trim();
            }
        }
        else if (showHostUI)
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, 2);
            if (selectedIndex == 0) // Quick Local
                StartQuickLocalHost();
            else if (selectedIndex == 1) // Relay - generate code but don't start yet
            {
                if (!isBusy)
                    _ = GenerateRelayCodeAsync();
            }
            else if (selectedIndex == 2) // Back
            {
                showHostUI = false;
                showMainMenu = true;
                selectedIndex = 0;
            }
        }
        else if (showHostCodeReadyUI)
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, 2);
            if (selectedIndex == 0) // Copy code
            {
                GUIUtility.systemCopyBuffer = hostJoinCode;
                connectionStatus = "Code copied to clipboard!";
            }
            else if (selectedIndex == 1) // Start Game
            {
                StartRelayHostWithCode();
            }
            else if (selectedIndex == 2) // Back
            {
                showHostCodeReadyUI = false;
                showHostUI = true;
                selectedIndex = 0;
            }
        }
        else if (showJoinChoiceUI)
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, 2);
            if (selectedIndex == 0) // Quick Local
                StartQuickLocalClient();
            else if (selectedIndex == 1) // Join Relay
            {
                showJoinChoiceUI = false;
                showJoinRelayUI = true;
                selectedIndex = 0;
                if (!isConnectingClient)
                    connectionStatus = "";
            }
            else if (selectedIndex == 2) // Back
            {
                showJoinChoiceUI = false;
                showMainMenu = true;
                selectedIndex = 0;
            }
        }
        else if (showJoinRelayUI)
        {
            // Dynamic button count based on retry visibility
            bool showRetry = !isBusy && !string.IsNullOrWhiteSpace(joinCodeInput) && !string.IsNullOrEmpty(connectionStatus);
            int maxIndex = showRetry ? 2 : 1;
            selectedIndex = Mathf.Clamp(selectedIndex, 0, maxIndex);
            
            if (selectedIndex == 0) // Connect
            {
                if (!isBusy)
                    _ = JoinRelayAsync(joinCodeInput.Trim());
            }
            else if (selectedIndex == 1 && showRetry) // Retry
            {
                _ = JoinRelayAsync(joinCodeInput.Trim());
            }
            else if (selectedIndex == maxIndex) // Back
            {
                showJoinRelayUI = false;
                showJoinChoiceUI = true;
                selectedIndex = 0;
            }
        }
    }

    void OnGUI()
    {
        InitializeStyles();

        if (showMainMenu)
            DrawMainMenu();
        else if (showHostUI)
            DrawHostMenu();
        else if (showHostCodeReadyUI)
            DrawHostCodeReadyMenu();
        else if (showJoinChoiceUI)
            DrawJoinChoiceMenu();
        else if (showJoinRelayUI)
            DrawJoinRelayMenu();

        if (showJoinCodeUI && !string.IsNullOrEmpty(activeJoinCode))
            DrawJoinCodeOverlay();
    }

    void DrawMainMenu()
    {
        selectedIndex = Mathf.Clamp(selectedIndex, 0, 1);
        
        float width = 350f;
        float height = 200f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(10);
        GUILayout.Label("Multiplayer", GUI.skin.label);
        GUILayout.Label("Left Stick = Navigate | A = Confirm | B = Back", statusStyle);
        GUILayout.Space(15);

        if (GUILayout.Button("Host", selectedIndex == 0 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
        {
            showMainMenu = false;
            showHostUI = true;
            selectedIndex = 0;
            connectionStatus = "";
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Join", selectedIndex == 1 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
        {
            showMainMenu = false;
            showJoinChoiceUI = true;
            selectedIndex = 0;
            connectionStatus = "";
            if (!string.IsNullOrEmpty(GUIUtility.systemCopyBuffer))
                joinCodeInput = GUIUtility.systemCopyBuffer.Trim();
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawHostMenu()
    {
        selectedIndex = Mathf.Clamp(selectedIndex, 0, 2);
        
        float width = 420f;
        float height = 210f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(10);
        GUILayout.Label("Host - Choose Type", GUI.skin.label);
        GUILayout.Space(10);

        if (GUILayout.Button("Quick Test Local\nStart host on this machine", selectedIndex == 0 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
            StartQuickLocalHost();

        GUILayout.Space(5);

        if (GUILayout.Button(isBusy ? "Generating Join Code..." : "External (Relay Join Code)\nNo port forwarding needed", selectedIndex == 1 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
        {
            if (!isBusy)
                _ = GenerateRelayCodeAsync();
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Back (ESC)", selectedIndex == 2 ? selectedButtonStyle : buttonStyle, GUILayout.Height(25)))
        {
            showHostUI = false;
            showMainMenu = true;
            selectedIndex = 0;
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawHostCodeReadyMenu()
    {
        selectedIndex = Mathf.Clamp(selectedIndex, 0, 2);
        
        float width = 500f;
        float height = 280f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(10);
        GUILayout.Label("Relay Join Code Ready", GUI.skin.label);
        GUILayout.Space(5);
        GUILayout.Label("Share this code with players to join", statusStyle);
        GUILayout.Space(10);

        // Display join code as a button to copy
        if (GUILayout.Button(hostJoinCode, selectedIndex == 0 ? selectedButtonStyle : codeButtonStyle, GUILayout.Height(60)))
        {
            GUIUtility.systemCopyBuffer = hostJoinCode;
            connectionStatus = "Code copied to clipboard!";
        }
        
        GUILayout.Space(5);
        if (!string.IsNullOrEmpty(connectionStatus))
        {
            GUILayout.Label(connectionStatus, statusStyle);
            GUILayout.Space(5);
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Start Game", selectedIndex == 1 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
        {
            StartRelayHostWithCode();
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Back", selectedIndex == 2 ? selectedButtonStyle : buttonStyle, GUILayout.Height(25)))
        {
            showHostCodeReadyUI = false;
            showHostUI = true;
            selectedIndex = 0;
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawJoinChoiceMenu()
    {
        selectedIndex = Mathf.Clamp(selectedIndex, 0, 2);
        
        float width = 430f;
        float height = 210f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(10);
        GUILayout.Label("Join - Choose Type", GUI.skin.label);
        GUILayout.Space(10);

        if (GUILayout.Button("Quick Connect Local\nConnect to 127.0.0.1:7777", selectedIndex == 0 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
            StartQuickLocalClient();

        GUILayout.Space(5);

        if (GUILayout.Button("Join External (Relay Code)\nEnter code from host", selectedIndex == 1 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
        {
            showJoinChoiceUI = false;
            showJoinRelayUI = true;
            selectedIndex = 0;
            if (!isConnectingClient)
                connectionStatus = "";
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Back (ESC)", selectedIndex == 2 ? selectedButtonStyle : buttonStyle, GUILayout.Height(25)))
        {
            showJoinChoiceUI = false;
            showMainMenu = true;
            selectedIndex = 0;
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawJoinRelayMenu()
    {
        // Dynamic button count based on retry visibility
        int maxIndex = (!isBusy && !string.IsNullOrWhiteSpace(joinCodeInput) && !string.IsNullOrEmpty(connectionStatus)) ? 2 : 1;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, maxIndex);
        
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

        GUILayout.Label("Use left stick up/down, A to confirm, Paste code from clipboard", statusStyle);
        GUILayout.Space(5);

        if (GUILayout.Button(isBusy ? "Connecting..." : "Connect", selectedIndex == 0 ? selectedButtonStyle : buttonStyle, GUILayout.Height(40)))
        {
            if (!isBusy)
                _ = JoinRelayAsync(joinCodeInput.Trim());
        }

        GUILayout.Space(5);
        if (!string.IsNullOrEmpty(connectionStatus))
        {
            GUILayout.Label(connectionStatus, statusStyle);
            GUILayout.Space(5);
        }

        if (!isBusy && !string.IsNullOrWhiteSpace(joinCodeInput) && !string.IsNullOrEmpty(connectionStatus))
        {
            if (GUILayout.Button("Retry", selectedIndex == 1 ? selectedButtonStyle : buttonStyle, GUILayout.Height(25)))
                _ = JoinRelayAsync(joinCodeInput.Trim());
            GUILayout.Space(5);
        }

        if (GUILayout.Button("Back", selectedIndex == maxIndex ? selectedButtonStyle : buttonStyle, GUILayout.Height(25)))
        {
            showJoinRelayUI = false;
            showJoinChoiceUI = true;
            selectedIndex = 0;
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

        ActivateObstaclesForSelectedMode();

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

        PrepareClientObstacleActivation();

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
            activateObstaclesOnClientConnected = false;
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

        ActivateObstaclesForSelectedMode();

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

    async Task GenerateRelayCodeAsync()
    {
        if (NetworkManager.Singleton == null)
            return;

        isBusy = true;
        connectionStatus = "Generating join code...";

        try
        {
            await EnsureUnityServicesAsync();

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                connectionStatus = "UnityTransport missing on NetworkManager.";
                isBusy = false;
                return;
            }

            const int maxConnections = 3;
            relayAllocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            hostJoinCode = await RelayService.Instance.GetJoinCodeAsync(relayAllocation.AllocationId);

            // Show the code ready menu
            showHostUI = false;
            showHostCodeReadyUI = true;
            selectedIndex = 0;
            connectionStatus = "";
        }
        catch (Exception ex)
        {
            connectionStatus = $"Failed to generate join code: {ex.Message}";
            Debug.LogWarning(connectionStatus);
        }
        finally
        {
            isBusy = false;
        }
    }

    void StartRelayHostWithCode()
    {
        if (NetworkManager.Singleton == null || relayAllocation == null)
        {
            connectionStatus = "No relay allocation available.";
            return;
        }

        ActivateObstaclesForSelectedMode();

        try
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                connectionStatus = "UnityTransport missing on NetworkManager.";
                return;
            }

            transport.SetRelayServerData(new RelayServerData(relayAllocation, "dtls"));

            bool started = NetworkManager.Singleton.StartHost();
            if (!started)
            {
                connectionStatus = "Failed to start Relay host.";
                return;
            }

            showHostCodeReadyUI = false;
            connectionStatus = "Relay host started!";
        }
        catch (Exception ex)
        {
            connectionStatus = $"Failed to start host: {ex.Message}";
            Debug.LogWarning(connectionStatus);
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

        PrepareClientObstacleActivation();

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
                activateObstaclesOnClientConnected = false;
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
            activateObstaclesOnClientConnected = false;
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

        selectedButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = Color.yellow }
        };

        codeButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = Color.cyan }
        };

        statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            wordWrap = true,
            normal = { textColor = Color.white }
        };

        stylesInitialized = true;
    }

    void CacheObstacleRoots()
    {
        cachedObstacleRoots.Clear();

        if (obstacleRoots != null)
        {
            for (int i = 0; i < obstacleRoots.Length; i++)
                AddObstacleRoot(obstacleRoots[i]);
        }

        if (!autoFindFloatingObstacles)
        {
            if (!autoFindClampSurfaceObstacles)
                return;
        }

        if (autoFindFloatingObstacles)
        {
            FloatingPlatforms[] floatingObstacles = FindObjectsByType<FloatingPlatforms>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < floatingObstacles.Length; i++)
            {
                FloatingPlatforms obstacle = floatingObstacles[i];
                if (obstacle == null)
                    continue;

                AddObstacleRoot(obstacle.gameObject);
            }
        }

        if (autoFindClampSurfaceObstacles)
        {
            ClampSurface2D[] clampSurfaces = FindObjectsByType<ClampSurface2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < clampSurfaces.Length; i++)
            {
                ClampSurface2D surface = clampSurfaces[i];
                if (surface == null)
                    continue;

                AddObstacleRoot(surface.gameObject);
            }
        }
    }

    void AddObstacleRoot(GameObject root)
    {
        if (root == null)
            return;

        if (cachedObstacleRoots.Contains(root))
            return;

        cachedObstacleRoots.Add(root);
    }

    void SetObstacleRootsActive(bool active)
    {
        for (int i = 0; i < cachedObstacleRoots.Count; i++)
        {
            GameObject root = cachedObstacleRoots[i];
            if (root != null)
                root.SetActive(active);
        }
    }

    void ActivateObstaclesForSelectedMode()
    {
        if (!delayObstacleActivationUntilModeSelected)
            return;

        if (obstaclesActivatedForSession)
            return;

        SetObstacleRootsActive(true);
        obstaclesActivatedForSession = true;
    }

    void PrepareClientObstacleActivation()
    {
        if (!delayObstacleActivationUntilModeSelected)
            return;

        SetObstacleRootsActive(false);
        obstaclesActivatedForSession = false;
        activateObstaclesOnClientConnected = true;
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

        if (activateObstaclesOnClientConnected)
        {
            ActivateObstaclesForSelectedMode();
            activateObstaclesOnClientConnected = false;
        }

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
        activateObstaclesOnClientConnected = false;
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