using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Steamworks;
using Words.Community;

[assembly: InternalsVisibleTo("SteamCommunity.EditModeTests")]

internal sealed class ManagedCompletionGate
{
    private int claimed;
    public bool TryClaim() => Interlocked.CompareExchange(ref claimed, 1, 0) == 0;
}

internal sealed class ManagedDownloadFlightTracker<T>
{
    private readonly Dictionary<FlightKey, Flight> flights = new Dictionary<FlightKey, Flight>();

    public bool TryBegin(uint appId, ulong itemId, ulong accountId, long generation,
        object session, T value)
    {
        if (!IsValid(appId, itemId, accountId, generation, session)) return false;
        var key = new FlightKey(appId, itemId, accountId, generation, session);
        if (flights.ContainsKey(key)) return false;
        flights.Add(key, new Flight(value));
        return true;
    }

    public bool Contains(uint appId, ulong itemId, ulong accountId, long generation, object session) =>
        IsValid(appId, itemId, accountId, generation, session)
        && flights.ContainsKey(new FlightKey(appId, itemId, accountId, generation, session));

    public bool TryMarkTimedOut(uint appId, ulong itemId, ulong accountId, long generation,
        object session, out T value)
    {
        if (TryGet(appId, itemId, accountId, generation, session, out Flight flight))
        {
            flight.TimedOut = true;
            value = flight.Value;
            return true;
        }
        value = default;
        return false;
    }

    public bool TryComplete(uint appId, ulong itemId, ulong accountId, long generation,
        object session, out T value, out bool wasTimedOut)
    {
        if (!IsValid(appId, itemId, accountId, generation, session))
        {
            value = default;
            wasTimedOut = false;
            return false;
        }
        var key = new FlightKey(appId, itemId, accountId, generation, session);
        if (flights.TryGetValue(key, out Flight flight))
        {
            flights.Remove(key);
            value = flight.Value;
            wasTimedOut = flight.TimedOut;
            return true;
        }
        value = default;
        wasTimedOut = false;
        return false;
    }

    public bool TryRemove(uint appId, ulong itemId, ulong accountId, long generation,
        object session, out T value)
    {
        bool found = TryComplete(appId, itemId, accountId, generation, session,
            out value, out _);
        return found;
    }

    public List<T> RemoveExcept(uint appId, ulong accountId, long generation, object session)
    {
        var removed = new List<T>();
        var keys = new List<FlightKey>();
        foreach (KeyValuePair<FlightKey, Flight> pair in flights)
        {
            if (pair.Key.Matches(appId, accountId, generation, session)) continue;
            keys.Add(pair.Key);
            removed.Add(pair.Value.Value);
        }
        foreach (FlightKey key in keys) flights.Remove(key);
        return removed;
    }

    public List<T> Clear()
    {
        var removed = new List<T>();
        foreach (Flight flight in flights.Values) removed.Add(flight.Value);
        flights.Clear();
        return removed;
    }

    private bool TryGet(uint appId, ulong itemId, ulong accountId, long generation,
        object session, out Flight flight)
    {
        if (IsValid(appId, itemId, accountId, generation, session)
            && flights.TryGetValue(new FlightKey(appId, itemId, accountId, generation, session), out flight))
            return true;
        flight = null;
        return false;
    }

    private static bool IsValid(uint appId, ulong itemId, ulong accountId, long generation, object session) =>
        appId != 0 && itemId != 0 && accountId != 0 && generation > 0 && session != null;

    private sealed class Flight
    {
        public readonly T Value;
        public bool TimedOut;
        public Flight(T value) { Value = value; }
    }

    private readonly struct FlightKey : IEquatable<FlightKey>
    {
        private readonly uint appId;
        private readonly ulong itemId;
        private readonly ulong accountId;
        private readonly long generation;
        private readonly object session;

        public FlightKey(uint appId, ulong itemId, ulong accountId, long generation, object session)
        {
            this.appId = appId;
            this.itemId = itemId;
            this.accountId = accountId;
            this.generation = generation;
            this.session = session;
        }

        public bool Matches(uint otherAppId, ulong otherAccountId, long otherGeneration, object otherSession) =>
            appId == otherAppId && accountId == otherAccountId && generation == otherGeneration
            && ReferenceEquals(session, otherSession);

        public bool Equals(FlightKey other) => itemId == other.itemId
            && Matches(other.appId, other.accountId, other.generation, other.session);
        public override bool Equals(object obj) => obj is FlightKey other && Equals(other);
        public override int GetHashCode() => (((((int)appId * 397) ^ itemId.GetHashCode()) * 397
            ^ accountId.GetHashCode()) * 397 ^ generation.GetHashCode()) * 397
            ^ RuntimeHelpers.GetHashCode(session);
    }
}

internal static class SteamCommunityCallbackPolicy
{
    public static bool IsAmbiguousMutationFailure(EResult result, bool ioFailure = false) =>
        ioFailure || result == EResult.k_EResultTimeout
        || result == EResult.k_EResultNoConnection
        || result == EResult.k_EResultServiceUnavailable;

    public static CommunityResult<T> UnknownPreservingValue<T>(CommunityResult<T> result, string message) =>
        CommunityResult<T>.Unknown(result != null ? result.Value : default,
            new CommunityError(CommunityErrorCode.RecoveryRequired, message, true),
            result != null ? result.OperationId : string.Empty);
}
