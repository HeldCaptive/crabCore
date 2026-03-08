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
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkStart : MonoBehaviour
{
    [Header("Network Limits")]
    [SerializeField, Min(2)] int maxTotalRelayPlayers = 16;

    [Header("Match Flow")]
    [SerializeField] bool requireHostStartForCoordinatedInitialSpawn = true;
    [SerializeField] bool showHostLobbyPanel = true;

    [Header("Diagnostics")]
    [SerializeField] bool showAuditConsole = false;
    [SerializeField, Min(4)] int auditLineCount = 12;

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
    bool showInGamePauseMenu;
    bool returningToMainMenu;
    bool returnShutdownRequested;

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
    readonly Queue<string> auditEntries = new Queue<string>();
    float nextMatchStateBroadcastTime;
    bool hasReceivedMatchStateMessage;
    bool lastReceivedMatchStarted;

    const string MatchStateMessageName = "NetworkStart_MatchState";
    static bool matchStateHandlerRegistered;
    static NetworkManager matchStateHandlerOwner;
    static bool isMatchStarted = true;
    static bool delayInitialSpawnUntilHostStart;

    public static bool IsMatchStarted => isMatchStarted;
    public static bool DelayInitialSpawnUntilHostStart => delayInitialSpawnUntilHostStart;

    void Awake()
    {
        delayInitialSpawnUntilHostStart = requireHostStartForCoordinatedInitialSpawn;
        isMatchStarted = !requireHostStartForCoordinatedInitialSpawn;

        CacheObstacleRoots();

        if (delayObstacleActivationUntilModeSelected)
            SetObstacleRootsActive(false);

        Audit($"Boot: match gate {(requireHostStartForCoordinatedInitialSpawn ? "enabled" : "disabled")}");
    }

    void Update()
    {
        EnsureCallbacksBound();
        TryRegisterMatchStateMessageHandler();

        if (returningToMainMenu)
            ProcessReturnToMainMenu();

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.IsServer
            && NetworkManager.Singleton.IsListening
            && Time.unscaledTime >= nextMatchStateBroadcastTime)
        {
            BroadcastMatchStateToAll(isMatchStarted);
            nextMatchStateBroadcastTime = Time.unscaledTime + 1f;
        }

        // Hide main menu once connected
        if (NetworkManager.Singleton.IsListening && !returningToMainMenu)
        {
            showMainMenu = false;
        }

        // Handle controller navigation
        HandleControllerInput();

        if (Keyboard.current != null
            && ((Keyboard.current.digit9Key != null && Keyboard.current.digit9Key.wasPressedThisFrame)
                || (Keyboard.current.numpad9Key != null && Keyboard.current.numpad9Key.wasPressedThisFrame)))
        {
            showAuditConsole = !showAuditConsole;

            if (showAuditConsole)
                Audit("Audit console enabled (key 9).");
        }

        // ESC or B button to go back
        bool backPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                           (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        if (showInGamePauseMenu)
        {
            if (backPressed)
            {
                showInGamePauseMenu = false;
                selectedIndex = 0;
            }

            return;
        }

        if (CanOpenInGamePauseMenu() && backPressed)
        {
            showInGamePauseMenu = true;
            selectedIndex = 0;
            return;
        }

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
        if (showInGamePauseMenu)
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, 1);

            if (selectedIndex == 0)
            {
                showInGamePauseMenu = false;
                selectedIndex = 0;
            }
            else
            {
                ReturnToHostJoinMenu();
            }

            return;
        }

        if (showMainMenu)
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, 2);
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
            else if (selectedIndex == 2) // Exit Game
            {
                ExitGameFromMainMenu();
            }
        }
        else if (showHostUI)
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, 1);
            if (selectedIndex == 0) // Relay - generate code but don't start yet
            {
                if (!isBusy)
                    _ = GenerateRelayCodeAsync();
            }
            else if (selectedIndex == 1) // Back
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
            selectedIndex = Mathf.Clamp(selectedIndex, 0, 1);
            if (selectedIndex == 0) // Join Relay
            {
                showJoinChoiceUI = false;
                showJoinRelayUI = true;
                selectedIndex = 0;
                if (!isConnectingClient)
                    connectionStatus = "";
            }
            else if (selectedIndex == 1) // Back
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

        DrawHostLobbyPanelIfNeeded();
        DrawAuditConsoleIfNeeded();

        if (showInGamePauseMenu)
            DrawInGamePauseMenu();
    }

    bool CanOpenInGamePauseMenu()
    {
        if (returningToMainMenu)
            return false;

        if (showMainMenu || showHostUI || showHostCodeReadyUI || showJoinChoiceUI || showJoinRelayUI)
            return false;

        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && manager.IsListening;
    }

    void DrawInGamePauseMenu()
    {
        selectedIndex = Mathf.Clamp(selectedIndex, 0, 1);

        float width = 360f;
        float height = 190f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();

        GUILayout.Space(10);
        GUILayout.Label("Paused", GUI.skin.label);
        GUILayout.Label("ESC/B = Resume", statusStyle);
        GUILayout.Space(14);

        if (GUILayout.Button("Resume", selectedIndex == 0 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(45)))
        {
            showInGamePauseMenu = false;
            selectedIndex = 0;
        }

        GUILayout.Space(8);

        if (GUILayout.Button("Return to Host / Join Menu", selectedIndex == 1 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(45)))
            ReturnToHostJoinMenu();

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void ReturnToHostJoinMenu()
    {
        returningToMainMenu = true;
        returnShutdownRequested = false;

        showInGamePauseMenu = false;
        showMainMenu = true;
        showHostUI = false;
        showHostCodeReadyUI = false;
        showJoinChoiceUI = false;
        showJoinRelayUI = false;
        showJoinCodeUI = false;

        selectedIndex = 0;
        isBusy = false;
        isConnectingClient = false;
        connectionStatus = "";
        activeJoinCode = "";
        hostJoinCode = "";
        relayAllocation = null;

        activateObstaclesOnClientConnected = false;

        if (delayObstacleActivationUntilModeSelected)
        {
            SetObstacleRootsActive(false);
            obstaclesActivatedForSession = false;
        }

        hasReceivedMatchStateMessage = false;
        delayInitialSpawnUntilHostStart = requireHostStartForCoordinatedInitialSpawn;
        isMatchStarted = !requireHostStartForCoordinatedInitialSpawn;

        Audit("Returned to Host/Join menu from pause menu.");
    }

    void ExitGameFromMainMenu()
    {
        Audit("Exit Game selected from main menu.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void ProcessReturnToMainMenu()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (!returnShutdownRequested && manager != null && manager.IsListening)
        {
            manager.Shutdown();
            returnShutdownRequested = true;
        }

        showMainMenu = true;
        showHostUI = false;
        showHostCodeReadyUI = false;
        showJoinChoiceUI = false;
        showJoinRelayUI = false;
        showJoinCodeUI = false;
        showInGamePauseMenu = false;

        if (manager != null && manager.IsListening)
            return;

        returningToMainMenu = false;
    }

    void DrawMainMenu()
    {
        selectedIndex = Mathf.Clamp(selectedIndex, 0, 2);
        
        float width = 350f;
        float height = 270f;
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

        GUILayout.Space(10);

        if (GUILayout.Button("Exit Game", selectedIndex == 2 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
            ExitGameFromMainMenu();

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawHostMenu()
    {
        selectedIndex = Mathf.Clamp(selectedIndex, 0, 1);
        
        float width = 420f;
        float height = 170f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(10);
        GUILayout.Label("Host", GUI.skin.label);
        GUILayout.Space(10);

        if (GUILayout.Button(isBusy ? "Generating Join Code..." : "External (Relay Join Code)\nNo port forwarding needed", selectedIndex == 1 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
        {
            if (!isBusy)
                _ = GenerateRelayCodeAsync();
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Back (ESC)", selectedIndex == 1 ? selectedButtonStyle : buttonStyle, GUILayout.Height(25)))
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
        selectedIndex = Mathf.Clamp(selectedIndex, 0, 1);
        
        float width = 430f;
        float height = 170f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();
        GUILayout.Space(10);
        GUILayout.Label("Join", GUI.skin.label);
        GUILayout.Space(10);

        if (GUILayout.Button("Join External (Relay Code)\nEnter code from host", selectedIndex == 1 ? selectedButtonStyle : bigButtonStyle, GUILayout.Height(50)))
        {
            showJoinChoiceUI = false;
            showJoinRelayUI = true;
            selectedIndex = 0;
            if (!isConnectingClient)
                connectionStatus = "";
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Back (ESC)", selectedIndex == 1 ? selectedButtonStyle : buttonStyle, GUILayout.Height(25)))
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

    void DrawHostLobbyPanelIfNeeded()
    {
        if (!showHostLobbyPanel)
            return;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer)
            return;

        if (!requireHostStartForCoordinatedInitialSpawn || isMatchStarted)
            return;

        float width = 360f;
        float height = 220f;
        Rect rect = new Rect(Screen.width - width - 20f, 20f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();

        GUILayout.Space(6);
        GUILayout.Label("Lobby (Host)", GUI.skin.label);
        int connectedCount = manager.ConnectedClientsIds.Count;
        GUILayout.Label($"Connected Players: {connectedCount}", statusStyle);
        GUILayout.Label("Press Start Game when current players are ready.", statusStyle);

        GUILayout.Space(6);
        GUILayout.Label("Client IDs:", statusStyle);
        int shown = 0;
        foreach (ulong id in manager.ConnectedClientsIds)
        {
            if (shown >= 6)
                break;

            GUILayout.Label($"- {id}", statusStyle);
            shown++;
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Start Game (Spawn Connected Players)", bigButtonStyle, GUILayout.Height(36f)))
            StartGameFromHost();

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawAuditConsoleIfNeeded()
    {
        if (!showAuditConsole)
            return;

        float width = 520f;
        float height = 220f;
        Rect rect = new Rect(20f, Screen.height - height - 20f, width, height);

        GUI.Box(rect, "");
        GUILayout.BeginArea(rect);
        GUILayout.BeginVertical();

        NetworkManager manager = NetworkManager.Singleton;
        string role = "Offline";
        int connected = 0;

        if (manager != null && manager.IsListening)
        {
            if (manager.IsServer)
                role = "Host";
            else if (manager.IsClient)
                role = "Client";

            connected = manager.ConnectedClientsIds.Count;
        }

        GUILayout.Label($"Audit Console | Role: {role} | MatchStarted: {isMatchStarted} | Connected: {connected}", statusStyle);
        GUILayout.Space(4f);

        foreach (string entry in auditEntries)
            GUILayout.Label(entry, statusStyle);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void StartGameFromHost()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer)
            return;

        if (isMatchStarted)
            return;

        isMatchStarted = true;
        BroadcastMatchStateToAll(isMatchStarted);
        TriggerCoordinatedSpawnForConnectedPlayers();
        Audit("Host pressed Start Game. Spawn wave triggered for connected players.");
    }

    void TriggerCoordinatedSpawnForConnectedPlayers()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer)
            return;

        CrabNetworkSync2D[] crabs = FindObjectsByType<CrabNetworkSync2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int triggered = 0;

        for (int i = 0; i < crabs.Length; i++)
        {
            CrabNetworkSync2D crab = crabs[i];
            if (crab == null || !crab.IsSpawned)
                continue;

            bool ownerIsConnected = false;
            for (int clientIndex = 0; clientIndex < manager.ConnectedClientsIds.Count; clientIndex++)
            {
                if (manager.ConnectedClientsIds[clientIndex] != crab.OwnerClientId)
                    continue;

                ownerIsConnected = true;
                break;
            }

            if (!ownerIsConnected)
                continue;

            crab.TriggerCoordinatedSpawnFromHost();
            triggered++;
        }

        Audit($"Coordinated spawn sent to {triggered} crabs.");
    }

    void OnHostStartedSuccessfully()
    {
        if (requireHostStartForCoordinatedInitialSpawn)
        {
            isMatchStarted = false;
            Audit("Host online. Waiting for Start Game to spawn connected players.");
        }
        else
        {
            isMatchStarted = true;
            Audit("Host online. Immediate spawn mode active.");
        }

        TryRegisterMatchStateMessageHandler();
        BroadcastMatchStateToAll(isMatchStarted);
    }

    void BroadcastMatchStateToAll(bool started)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer || !manager.IsListening || manager.CustomMessagingManager == null)
            return;

        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe(started);
        manager.CustomMessagingManager.SendNamedMessageToAll(MatchStateMessageName, writer, NetworkDelivery.ReliableSequenced);
    }

    void TryRegisterMatchStateMessageHandler()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.CustomMessagingManager == null)
            return;

        if (matchStateHandlerRegistered && matchStateHandlerOwner == manager)
            return;

        if (matchStateHandlerRegistered && matchStateHandlerOwner != null && matchStateHandlerOwner.CustomMessagingManager != null)
            matchStateHandlerOwner.CustomMessagingManager.UnregisterNamedMessageHandler(MatchStateMessageName);

        manager.CustomMessagingManager.RegisterNamedMessageHandler(MatchStateMessageName, OnMatchStateMessageReceived);
        matchStateHandlerRegistered = true;
        matchStateHandlerOwner = manager;
    }

    void UnregisterMatchStateMessageHandler()
    {
        if (!matchStateHandlerRegistered)
            return;

        if (matchStateHandlerOwner != null && matchStateHandlerOwner.CustomMessagingManager != null)
            matchStateHandlerOwner.CustomMessagingManager.UnregisterNamedMessageHandler(MatchStateMessageName);

        matchStateHandlerRegistered = false;
        matchStateHandlerOwner = null;
    }

    void OnMatchStateMessageReceived(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out bool started);
        isMatchStarted = started;

        bool hasChanged = !hasReceivedMatchStateMessage || lastReceivedMatchStarted != started;
        hasReceivedMatchStateMessage = true;
        lastReceivedMatchStarted = started;

        if (!hasChanged)
            return;

        if (started)
            Audit($"Match state update from {senderClientId}: STARTED");
        else
            Audit($"Match state update from {senderClientId}: WAITING");
    }

    void Audit(string message)
    {
        string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        auditEntries.Enqueue(entry);

        while (auditEntries.Count > Mathf.Max(4, auditLineCount))
            auditEntries.Dequeue();

        Debug.Log($"[NetworkStart] {entry}");
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
        {
            connectionStatus = "Failed to start local host. Port 7777 may already be in use.";
            return;
        }

        OnHostStartedSuccessfully();
        Audit("Quick local host started.");
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
        isMatchStarted = !requireHostStartForCoordinatedInitialSpawn;

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
        Audit($"Local client connection started -> {address}:7777");
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

            int maxConnections = Mathf.Max(1, maxTotalRelayPlayers - 1);
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));

            bool started = NetworkManager.Singleton.StartHost();
            if (!started)
            {
                connectionStatus = "Failed to start Relay host.";
                return;
            }

            OnHostStartedSuccessfully();

            activeJoinCode = joinCode;
            showJoinCodeUI = true;
            showHostUI = false;
            connectionStatus = "Relay host started.";
            Audit($"Relay host started with join code {joinCode}");
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

            int maxConnections = Mathf.Max(1, maxTotalRelayPlayers - 1);
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

            OnHostStartedSuccessfully();

            showHostCodeReadyUI = false;
            connectionStatus = "Relay host started!";
            Audit($"Relay host started with prepared join code {hostJoinCode}");
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
        isMatchStarted = !requireHostStartForCoordinatedInitialSpawn;

        isBusy = true;
        isConnectingClient = false;
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
            isConnectingClient = true;
            connectStartTime = Time.unscaledTime;
            connectionStatus = "Trying to connect via Relay...";
            Audit($"Relay client connection started using code {joinCodeInput}");
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
        UnregisterMatchStateMessageHandler();

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

        if (NetworkManager.Singleton.IsServer)
        {
            Audit($"Client connected: {clientId}. Connected total: {NetworkManager.Singleton.ConnectedClientsIds.Count}");
            BroadcastMatchStateToAll(isMatchStarted);
        }

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        TryRegisterMatchStateMessageHandler();

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
        Audit("Local client connected.");
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager manager = NetworkManager.Singleton;

        if (manager.IsServer)
            Audit($"Client disconnected: {clientId}. Connected total: {manager.ConnectedClientsIds.Count}");

        bool isRemoteClientWhoseHostDisconnected = !manager.IsServer && manager.IsClient && clientId == NetworkManager.ServerClientId;
        if (isRemoteClientWhoseHostDisconnected)
        {
            ReturnToHostJoinMenu();
            connectionStatus = "Host disconnected. Returned to Host/Join menu.";
            Audit("Host disconnected. Returning local client to Host/Join menu.");
            return;
        }

        if (clientId != manager.LocalClientId)
            return;

        if (!isConnectingClient)
        {
            ReturnToHostJoinMenu();
            connectionStatus = "Disconnected. Returned to Host/Join menu.";
            Audit("Local client disconnected. Returned to Host/Join menu.");
            return;
        }

        isBusy = false;
        isConnectingClient = false;
        activateObstaclesOnClientConnected = false;
        showJoinChoiceUI = true;
        showJoinRelayUI = true;
        connectionStatus = "Connection failed. Check host/join code and try again.";
        Audit("Local client disconnected during connection flow.");
    }

    void OnApplicationQuit()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    void OnDestroy()
    {
        OnDisable();

        isMatchStarted = !requireHostStartForCoordinatedInitialSpawn;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }
}