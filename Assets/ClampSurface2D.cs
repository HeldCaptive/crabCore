using UnityEngine;

public class ClampSurface2D : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] float slipperiness = 0f;

    public float Slipperiness => Mathf.Clamp01(slipperiness);
}
