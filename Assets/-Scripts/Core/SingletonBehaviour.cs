using UnityEngine;

/// <summary>
/// Generic MonoBehaviour singleton. Ensures only one instance exists.
/// Destroys duplicate GameObjects on Awake.
/// </summary>
public abstract class SingletonBehaviour<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        var instance = this as T;
        if (Services.Get<T>() != null && Services.Get<T>() != instance)
        {
            Destroy(gameObject);
            return;
        }
        Services.Register(instance);
    }
}
