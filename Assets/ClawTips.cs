using UnityEngine;

public class ClawTipGizmo : MonoBehaviour
{
    public float radius = 0.15f;
    public Color color = Color.green;

    void OnDrawGizmos()
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}