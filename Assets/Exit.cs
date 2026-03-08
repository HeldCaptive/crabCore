using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class Exit : MonoBehaviour
{
    [SerializeField] float holdDurationSeconds = 5f;
    [SerializeField] bool showHoldProgress = true;
    [SerializeField] bool disableHoldExitDuringOnlineSession = true;

    float escapeHoldTime;
    bool quitTriggered;

    void Update()
    {
        if (quitTriggered)
            return;

        if (ShouldSuppressHoldExit())
        {
            escapeHoldTime = 0f;
            return;
        }

        if (IsEscapePressed())
        {
            escapeHoldTime += Time.unscaledDeltaTime;

            if (escapeHoldTime >= holdDurationSeconds)
            {
                quitTriggered = true;

#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }

            return;
        }

        escapeHoldTime = 0f;
    }

    void OnGUI()
    {
        if (!showHoldProgress)
            return;

        if (ShouldSuppressHoldExit())
            return;

        if (quitTriggered)
            return;

        if (!IsEscapePressed())
            return;

        float duration = Mathf.Max(0.01f, holdDurationSeconds);
        float clampedTime = Mathf.Clamp(escapeHoldTime, 0f, duration);
        float percent = clampedTime / duration;

        string text = $"Hold Esc to Exit: {clampedTime:0.0}/{duration:0.0}s ({percent * 100f:0}%)";
        Rect rect = new Rect(20f, 20f, 420f, 30f);
        GUI.Label(rect, text);
    }

    bool IsEscapePressed()
    {
        return Keyboard.current != null
            && Keyboard.current.escapeKey.isPressed;
    }

    bool ShouldSuppressHoldExit()
    {
        if (!disableHoldExitDuringOnlineSession)
            return false;

        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && manager.IsListening;
    }
}
