using System.Collections.Generic;
using UnityEngine;

public class CrabSpawnZone : MonoBehaviour
{
    [Header("Slots")]
    [SerializeField] bool useChildTransformsAsSlots = true;
    [SerializeField] Transform[] explicitSlots;

    [Header("Fallback Area")]
    [SerializeField] Vector2 areaCenterOffset = Vector2.zero;
    [SerializeField, Min(0.1f)] float fallbackSpacing = 6f;
    [SerializeField, Min(0.1f)] float fallbackHalfWidth = 12f;

    readonly List<Transform> cachedSlots = new List<Transform>();

    void Awake()
    {
        RebuildSlotCache();
    }

    void OnValidate()
    {
        RebuildSlotCache();
    }

    void RebuildSlotCache()
    {
        cachedSlots.Clear();

        if (explicitSlots != null)
        {
            for (int i = 0; i < explicitSlots.Length; i++)
            {
                Transform slot = explicitSlots[i];
                if (slot == null)
                    continue;

                if (!cachedSlots.Contains(slot))
                    cachedSlots.Add(slot);
            }
        }

        if (!useChildTransformsAsSlots)
            return;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null)
                continue;

            if (!cachedSlots.Contains(child))
                cachedSlots.Add(child);
        }
    }

    public bool TryGetSpawnPosition(ulong clientId, float defaultSpacing, out Vector2 spawnPosition)
    {
        if (cachedSlots.Count == 0)
            RebuildSlotCache();

        int slotIndex = Mathf.Max(0, (int)clientId);

        if (slotIndex < cachedSlots.Count && cachedSlots[slotIndex] != null)
        {
            spawnPosition = cachedSlots[slotIndex].position;
            return true;
        }

        float spacing = defaultSpacing > 0.001f ? defaultSpacing : fallbackSpacing;
        float offset = GetCenteredHorizontalOffset(clientId, spacing);
        offset = Mathf.Clamp(offset, -fallbackHalfWidth, fallbackHalfWidth);

        Vector2 center = (Vector2)transform.position + areaCenterOffset;
        spawnPosition = center + Vector2.right * offset;
        return true;
    }

    float GetCenteredHorizontalOffset(ulong clientId, float spacing)
    {
        float slot = (clientId / 2) + 1f;
        float direction = (clientId % 2 == 0) ? -1f : 1f;
        return slot * direction * spacing;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 center = (Vector2)transform.position + areaCenterOffset;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, new Vector3(fallbackHalfWidth * 2f, 1f, 0f));

        if (cachedSlots.Count == 0)
            RebuildSlotCache();

        Gizmos.color = Color.green;
        for (int i = 0; i < cachedSlots.Count; i++)
        {
            Transform slot = cachedSlots[i];
            if (slot == null)
                continue;

            Gizmos.DrawWireSphere(slot.position, 0.4f);
        }
    }
}
