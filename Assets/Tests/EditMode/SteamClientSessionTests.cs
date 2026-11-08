using System;
using NUnit.Framework;

[TestFixture]
public sealed class SteamClientSessionTests
{
    [Test]
    public void RuntimeLease_RejectsSecondOwner_AndShutsDownOnce()
    {
        FakeSession fake = new FakeSession();
        int factoryCalls = 0;
        using (SteamClientSessionRegistry.OverrideFactoryForTests(() =>
        {
            factoryCalls++;
            return fake;
        }))
        {
            SteamClientSessionLease lease = SteamClientSessionRegistry.AcquireRuntime();
            Assert.AreSame(fake, SteamClientSessionRegistry.Current);
            Assert.AreSame(fake, lease.Session);

            Assert.Throws<InvalidOperationException>(() => SteamClientSessionRegistry.AcquireRuntime());
            Assert.AreEqual(1, factoryCalls, "A rejected second owner must not create or initialize another session.");
            Assert.AreEqual(0, fake.ShutdownCount);

            lease.Dispose();
            lease.Dispose();

            Assert.IsNull(SteamClientSessionRegistry.Current);
            Assert.AreEqual(1, fake.ShutdownCount);
        }
    }

    [Test]
    public void AccountBinding_InvalidatesWhenSessionIdentityChanges()
    {
        FakeSession fake = new FakeSession
        {
            Status = SteamClientSessionStatus.Initialized,
            AppId = 4966870,
            AccountId = 76561198000000001UL
        };

        Assert.IsTrue(SteamClientSessionIdentity.IsCurrentAccount(
            fake, 4966870, fake.AccountId, fake.InitializationGeneration));

        fake.AccountId++;
        Assert.IsFalse(SteamClientSessionIdentity.IsCurrentAccount(
            fake, 4966870, 76561198000000001UL, fake.InitializationGeneration));
        Assert.IsFalse(SteamClientSessionIdentity.IsCurrentAccount(
            fake, 4966870, fake.AccountId + 1, fake.InitializationGeneration));

        fake.AppId++;
        Assert.IsFalse(SteamClientSessionIdentity.IsCurrentAccount(
            fake, 4966870, fake.AccountId, fake.InitializationGeneration));
        fake.AppId--;
        fake.InitializationGeneration++;
        Assert.IsFalse(SteamClientSessionIdentity.IsCurrentAccount(
            fake, 4966870, fake.AccountId, fake.InitializationGeneration - 1));
    }

    private sealed class FakeSession : ISteamClientSession
    {
        public SteamClientSessionStatus Status { get; set; } = SteamClientSessionStatus.Initialized;
        public uint AppId { get; set; } = 4966870;
        public ulong AccountId { get; set; } = 76561198000000001UL;
        public long InitializationGeneration { get; set; } = 1;
        public int ShutdownCount { get; private set; }

        public bool IsInitialized => Status == SteamClientSessionStatus.Initialized;
        public uint CurrentAppId => AppId;
        public ulong CurrentAccountId => AccountId;
        public string LastError => null;
        public bool TryInitialize() => IsInitialized;
        public void RunCallbacks() { }

        public void Shutdown()
        {
            ShutdownCount++;
            Status = SteamClientSessionStatus.NotInitialized;
        }
    }
}
