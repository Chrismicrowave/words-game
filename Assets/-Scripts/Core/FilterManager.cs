using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Slot-based post-process filter toggles.
// Slot 0 = CRT filter. Future filters slot in without restructuring.
public class FilterManager : MonoBehaviour
{
    public static FilterManager Instance { get; private set; }

    [SerializeField] private List<Behaviour> filterBehaviours = new List<Behaviour>();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SetFilter(int slot, bool active)
    {
        if (slot < 0 || slot >= filterBehaviours.Count) return;
        var b = filterBehaviours[slot];
        if (b == null) return;

        FieldInfo f = b.GetType().GetField("effectActive");
        if (f != null) f.SetValue(b, active);
        else b.enabled = active;

    }

    public bool GetFilter(int slot)
    {
        if (slot < 0 || slot >= filterBehaviours.Count) return false;
        var b = filterBehaviours[slot];
        if (b == null) return false;
        FieldInfo f = b.GetType().GetField("effectActive");
        if (f != null) return (bool)f.GetValue(b);
        return b.enabled;
    }
}
