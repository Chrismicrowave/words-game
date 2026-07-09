using UnityEngine;

/// <summary>
/// Generic MonoBehaviour singleton using Service Locator.
/// Registers itself with Services in Awake.
/// Destroys duplicate GameObjects.
/// </summary>
public abstract class SingletonBehaviour<T> : MonoBehaviour where T : MonoBehaviour
{
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
