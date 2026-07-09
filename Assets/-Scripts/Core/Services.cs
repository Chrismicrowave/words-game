using System;
using System.Collections.Generic;

/// <summary>
/// Service Locator registry. Systems register in Awake, consumers
/// call Services.Get<T>() instead of SingletonBehaviour<T>.Instance.
///
/// Supports testing and extension without third-party dependencies.
/// </summary>
public static class Services
{
    private static readonly Dictionary<Type, object> Registry = new();

    /// <summary>Register an instance. Called once per service in Awake.</summary>
    public static void Register<T>(T instance) where T : class
    {
        Registry[typeof(T)] = instance;
    }

    /// <summary>Retrieve a registered service. Returns null if not registered yet.</summary>
    public static T Get<T>() where T : class
    {
        return Registry.TryGetValue(typeof(T), out var obj) ? obj as T : null;
    }
}
