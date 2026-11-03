using System.Collections.Generic;
using UnityEngine;

// Slot-based post-process filter toggles.
// Slot 0 = CRT filter. Future filters slot in without restructuring.
public class FilterManager : SingletonBehaviour<FilterManager>
{
    [SerializeField] private List<Behaviour> filterBehaviours = new List<Behaviour>();

    public void SetFilter(int slot, bool active)
    {
        if (slot < 0 || slot >= filterBehaviours.Count) return;
        var b = filterBehaviours[slot];
        if (b != null) b.enabled = active;
    }

    public bool GetFilter(int slot)
    {
        if (slot < 0 || slot >= filterBehaviours.Count) return false;
        var b = filterBehaviours[slot];
        return b != null && b.enabled;
    }
}
