using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Ledger.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Limits;

[Trait("Category", "Unit")]
public sealed class RequestLimitersPartitionTests : IDisposable
{
    private const string Account = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";

    private readonly RequestLimiters _limiters = Create(new RateLimitingOptions
    {
        Enabled = true,
        ReplenishmentSeconds = 3600,
        WritePerClient = new TokenBucketSettings { Capacity = 30, RefillPerSecond = 10 },
        ReadPerClient = new TokenBucketSettings { Capacity = 60, RefillPerSecond = 20 },
        WritePerAccount = new TokenBucketSettings { Capacity = 9, RefillPerSecond = 3 }
    });

    public void Dispose() => _limiters.Dispose();

    [Fact]
    public void AnAuthenticatedWrite_IsPartitionedByTheClientAndTheWriteKind()
    {
        var context = ContextFor(RequestClass.Write, "pix-gateway");

        var partition = _limiters.ClientPartition(context);

        partition.PartitionKey.ShouldBe(new ClientQuotaKey(false, "pix-gateway", null));
    }

    [Theory]
    [InlineData("Balance")]
    [InlineData("Statement")]
    public void AnAuthenticatedRead_IsPartitionedByTheClientAndTheReadKind(string requestClass)
    {
        var context = ContextFor(Enum.Parse<RequestClass>(requestClass), "pix-gateway");

        _limiters.ClientPartition(context).PartitionKey.ShouldBe(new ClientQuotaKey(true, "pix-gateway", null));
    }

    [Fact]
    public void TwoClients_DoNotShareAPartition()
    {
        var first = _limiters.ClientPartition(ContextFor(RequestClass.Write, "pix-gateway")).PartitionKey;
        var second = _limiters.ClientPartition(ContextFor(RequestClass.Write, "cards-core")).PartitionKey;

        first.ShouldNotBe(second);
    }

    [Fact]
    public void AReadAndAWriteOfTheSameClient_DoNotShareAPartition()
    {
        var read = _limiters.ClientPartition(ContextFor(RequestClass.Balance, "pix-gateway")).PartitionKey;
        var write = _limiters.ClientPartition(ContextFor(RequestClass.Write, "pix-gateway")).PartitionKey;

        read.ShouldNotBe(write);
    }

    [Fact]
    public void ARequestWithoutAToken_IsPartitionedByTheSocketOrigin()
    {
        var origin = IPAddress.Parse("10.1.2.3");
        var context = ContextFor(RequestClass.Write, clientId: null, origin);

        var key = _limiters.ClientPartition(context).PartitionKey;

        key.ClientId.ShouldBeNull();
        key.Origin.ShouldBe(origin);
    }

    [Fact]
    public void TwoOrigins_WithoutAToken_DoNotShareAPartition()
    {
        var first = _limiters.ClientPartition(ContextFor(RequestClass.Write, null, IPAddress.Parse("10.1.2.3"))).PartitionKey;
        var second = _limiters.ClientPartition(ContextFor(RequestClass.Write, null, IPAddress.Parse("10.1.2.4"))).PartitionKey;

        first.ShouldNotBe(second);
    }

    [Fact]
    public void TheClientBucket_FollowsTheConfiguredCapacityOfItsKind()
    {
        var write = _limiters.ClientPartition(ContextFor(RequestClass.Write, "pix-gateway"));
        var read = _limiters.ClientPartition(ContextFor(RequestClass.Balance, "pix-gateway"));

        using var writeLimiter = write.Factory(write.PartitionKey);
        using var readLimiter = read.Factory(read.PartitionKey);

        writeLimiter.GetStatistics()!.CurrentAvailablePermits.ShouldBe(30);
        readLimiter.GetStatistics()!.CurrentAvailablePermits.ShouldBe(60);
    }

    [Fact]
    public void ARouteWithoutAClass_IsNotLimitedByAnyLink()
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(null, EndpointMetadataCollection.Empty, "health"));

        _limiters.ConcurrencyPartition(context).PartitionKey.ShouldBe(default);
        _limiters.ClientPartition(context).PartitionKey.ShouldBe(default);
        _limiters.AccountPartition(context).PartitionKey.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void ADisabledLimiter_LimitsNothing()
    {
        using var disabled = Create(new RateLimitingOptions { Enabled = false });
        var context = ContextFor(RequestClass.Write, "pix-gateway");

        disabled.ConcurrencyPartition(context).PartitionKey.ShouldBe(default);
        disabled.ClientPartition(context).PartitionKey.ShouldBe(default);
        disabled.AccountPartition(context).PartitionKey.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void AWriteWithAValidAccountId_IsPartitionedByTheAccount()
    {
        var context = ContextFor(RequestClass.Write, "pix-gateway");
        context.Request.RouteValues["accountId"] = Account;

        _limiters.AccountPartition(context).PartitionKey.ShouldBe(Guid.Parse(Account));
    }

    [Fact]
    public void AWriteFromAnAnonymousCaller_NeverCreatesAnAccountLimiter()
    {
        var context = ContextFor(RequestClass.Write, clientId: null, IPAddress.Parse("10.1.2.3"));
        context.Request.RouteValues["accountId"] = Account;

        _limiters.AccountPartition(context).PartitionKey.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void AWriteFromATokenWithoutTheWriteScope_NeverCreatesAnAccountLimiter()
    {
        var context = ContextFor(RequestClass.Write, "pix-gateway", scope: "ledger.read");
        context.Request.RouteValues["accountId"] = Account;

        _limiters.AccountPartition(context).PartitionKey.ShouldBe(Guid.Empty);
    }

    [Theory]
    [InlineData("ledger.writer")]
    [InlineData("LEDGER.WRITE")]
    [InlineData("")]
    public void AWriteFromATokenWithAScopeThatOnlyLooksLikeTheWriteScope_NeverCreatesAnAccountLimiter(string scope)
    {
        var context = ContextFor(RequestClass.Write, "pix-gateway", scope: scope);
        context.Request.RouteValues["accountId"] = Account;

        _limiters.AccountPartition(context).PartitionKey.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void AWriteFromAPrincipalThatIsNotAuthenticated_NeverCreatesAnAccountLimiter()
    {
        var context = ContextFor(RequestClass.Write, "pix-gateway", authenticated: false);
        context.Request.RouteValues["accountId"] = Account;

        _limiters.AccountPartition(context).PartitionKey.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void AWriteFromATokenWithAClientIdThatDoesNotFit_NeverCreatesAnAccountLimiter()
    {
        var context = ContextFor(RequestClass.Write, new string('c', 129));
        context.Request.RouteValues["accountId"] = Account;

        _limiters.AccountPartition(context).PartitionKey.ShouldBe(Guid.Empty);
        _limiters.ClientPartition(context).PartitionKey.ClientId.ShouldBeNull();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("0192b7c281aa7e04b1d56f0c2a9e8d33")]
    [InlineData("{0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33}")]
    [InlineData(" 0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")]
    [InlineData("+192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public void AnAccountIdThatIsNotACanonicalGuid_CreatesNoAccountLimiter(string accountId)
    {
        var context = ContextFor(RequestClass.Write, "pix-gateway");
        context.Request.RouteValues["accountId"] = accountId;

        _limiters.AccountPartition(context).PartitionKey.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void ARead_WithAnAccountId_IsNeverLimitedPerAccount()
    {
        var context = ContextFor(RequestClass.Balance, "pix-gateway");
        context.Request.RouteValues["accountId"] = Account;

        _limiters.AccountPartition(context).PartitionKey.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void TheConcurrencyPartition_IsTheClassOfTheRoute()
    {
        _limiters.ConcurrencyPartition(ContextFor(RequestClass.Write, "a")).PartitionKey.ShouldBe(RequestClass.Write);
        _limiters.ConcurrencyPartition(ContextFor(RequestClass.Balance, "a")).PartitionKey.ShouldBe(RequestClass.Balance);
        _limiters.ConcurrencyPartition(ContextFor(RequestClass.Statement, "a")).PartitionKey.ShouldBe(RequestClass.Statement);
    }

    [Fact]
    public void ThePartitionFunctions_AllocateNothingPerRequest()
    {
        var context = ContextFor(RequestClass.Write, "pix-gateway");
        context.Request.RouteValues["accountId"] = Account;
        var anonymous = ContextFor(RequestClass.Balance, null, IPAddress.Parse("10.1.2.3"));

        for (var warmUp = 0; warmUp < 1_000; warmUp++)
        {
            Touch(context);
            Touch(anonymous);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var call = 0; call < 20_000; call++)
        {
            Touch(context);
            Touch(anonymous);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBeLessThan(2_048);
    }

    [Fact]
    public async Task TheChain_RefusesTheRequestThatExceedsTheClientCapacityAndNamesThePolicy()
    {
        using var small = Create(new RateLimitingOptions
        {
            Enabled = true,
            ReplenishmentSeconds = 3600,
            WritePerClient = new TokenBucketSettings { Capacity = 2, RefillPerSecond = 1 }
        });
        var context = ContextFor(RequestClass.Write, "pix-gateway");

        using var first = await small.Chain.AcquireAsync(context, 1, CancellationToken.None);
        using var second = await small.Chain.AcquireAsync(context, 1, CancellationToken.None);
        using var third = await small.Chain.AcquireAsync(context, 1, CancellationToken.None);

        first.IsAcquired.ShouldBeTrue();
        second.IsAcquired.ShouldBeTrue();
        third.IsAcquired.ShouldBeFalse();
        third.TryGetMetadata(NamedLease.PolicyMetadata, out var policy).ShouldBeTrue();
        policy.ShouldBe(RateLimitPolicyNames.WritePerClient);
        third.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ARefusalAfterAnAttempt_IsNotRetriedByTheWaitingPathSoTheCallerPaysOnce()
    {
        using var small = Create(new RateLimitingOptions
        {
            Enabled = true,
            ReplenishmentSeconds = 3600,
            WritePerClient = new TokenBucketSettings { Capacity = 3, RefillPerSecond = 1 },
            WritePerAccount = new TokenBucketSettings { Capacity = 1, RefillPerSecond = 1 }
        });
        const string otherAccount = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d34";
        const string thirdAccount = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d35";

        using var first = small.Chain.AttemptAcquire(WriteContext(Account), 1);
        var second = WriteContext(Account);
        using var secondAttempt = small.Chain.AttemptAcquire(second, 1);
        using var secondWaiting = await small.Chain.AcquireAsync(second, 1, CancellationToken.None);
        using var third = small.Chain.AttemptAcquire(WriteContext(otherAccount), 1);
        using var fourth = small.Chain.AttemptAcquire(WriteContext(thirdAccount), 1);

        first.IsAcquired.ShouldBeTrue();
        secondAttempt.IsAcquired.ShouldBeFalse();
        secondWaiting.IsAcquired.ShouldBeFalse();
        secondWaiting.TryGetMetadata(NamedLease.PolicyMetadata, out var policy).ShouldBeTrue();
        policy.ShouldBe(RateLimitPolicyNames.WritePerAccount);
        third.IsAcquired.ShouldBeTrue();
        fourth.IsAcquired.ShouldBeFalse();
        fourth.TryGetMetadata(NamedLease.PolicyMetadata, out var lastPolicy).ShouldBeTrue();
        lastPolicy.ShouldBe(RateLimitPolicyNames.WritePerClient);
    }

    private static DefaultHttpContext WriteContext(string accountId)
    {
        var context = ContextFor(RequestClass.Write, "pix-gateway");
        context.Request.RouteValues["accountId"] = accountId;

        return context;
    }

    private void Touch(HttpContext context)
    {
        _ = _limiters.ConcurrencyPartition(context);
        _ = _limiters.ClientPartition(context);
        _ = _limiters.AccountPartition(context);
    }

    private static DefaultHttpContext ContextFor(
        RequestClass requestClass,
        string? clientId,
        IPAddress? origin = null,
        string scope = "ledger.read ledger.write",
        bool authenticated = true)
    {
        var context = new DefaultHttpContext();
        var metadata = new EndpointMetadataCollection(new RequestClassMetadata(requestClass));

        context.SetEndpoint(new Endpoint(null, metadata, "business"));

        if (clientId is not null)
        {
            var claims = new List<Claim> { new("client_id", clientId), new("scope", scope) };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Bearer" : null));
        }

        context.Connection.RemoteIpAddress = origin;

        return context;
    }

    private static RequestLimiters Create(RateLimitingOptions options) => new(Options.Create(options));
}
