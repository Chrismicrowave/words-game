using UnityEngine;

/// <summary>
/// Assigns a permanent GUID to a GameObject. Survives rename, reparent, and prefab override.
/// Does NOT survive deletion — intentional, so broken references surface loudly.
/// Place on any GameObject that the agent must address by stable identity.
/// </summary>
[DisallowMultipleComponent]
public class StableId : MonoBehaviour
{
    [SerializeField, HideInInspector] private string _id;

    /// <summary>Stable GUID string (32 hex chars, no dashes).</summary>
    public string Id => _id;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(_id))
        {
            _id = System.Guid.NewGuid().ToString("N");
#if UNITY_EDITOR
            // Mark dirty so the new ID is saved to the asset/scene immediately.
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
