using UnityEngine;

public class AutoCameraSize : MonoBehaviour
{
    public float baseOrthographicSize = 20f;
    public float referenceAspect = 16f / 9f;

    void Start()
    {
        Camera cam = GetComponent<Camera>();
        float aspect = (float)Screen.width / Screen.height;

        cam.orthographicSize = baseOrthographicSize * (referenceAspect / aspect);
    }
}