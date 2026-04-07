using System.Collections.Generic;
using UnityEngine;

// Slot-based post-process filter toggles.
// Slot 0 = CRT filter. Future filters slot in without restructuring.
public class FilterManager : MonoBehaviour
{
    public static FilterManager Instance { get; private set; }

    [SerializeField] private List<GameObject> filterVolumes = new List<GameObject>();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SetFilter(int slot, bool enabled)
    {
        if (slot < 0 || slot >= filterVolumes.Count) return;
        if (filterVolumes[slot] != null)
            filterVolumes[slot].SetActive(enabled);
    }

    public bool GetFilter(int slot)
    {
        if (slot < 0 || slot >= filterVolumes.Count) return false;
        return filterVolumes[slot] != null && filterVolumes[slot].activeSelf;
    }
}
