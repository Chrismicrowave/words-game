using UnityEngine;

/// <summary>
/// Controls GameObject active states at game start.
/// Set each GO's desired state in the Inspector; applied in Start()
/// after all Awake() calls have completed (Awake = self, Start = others).
/// </summary>
public class OnGameStartManager : MonoBehaviour
{
    [System.Serializable]
    public struct GameObjectToggle
    {
        public GameObject gameObject;
        public bool activeAtStart;
    }

    [SerializeField] private GameObjectToggle[] toggles;

    void Start()
    {
        foreach (var toggle in toggles)
        {
            if (toggle.gameObject != null)
                toggle.gameObject.SetActive(toggle.activeAtStart);
        }
    }
}
