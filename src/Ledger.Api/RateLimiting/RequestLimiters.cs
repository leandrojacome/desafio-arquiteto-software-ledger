using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;
using Ledger.Api.Security;
using Microsoft.Extensions.Options;

namespace Ledger.Api.RateLimiting;

internal sealed class RequestLimiters : IDisposable
{
    private const string AccountIdRouteValue = "accountId";

    private readonly RateLimitingOptions _options;
    private readonly Func<RequestClass, RateLimiter> _concurrencyFactory;
    private readonly Func<ClientQuotaKey, RateLimiter> _clientFactory;
    private readonly Func<Guid, RateLimiter> _accountFactory;
    private readonly TokenBucketRateLimiterOptions _writeClientBucket;
    private readonly TokenBucketRateLimiterOptions _readClientBucket;
    private readonly TokenBucketRateLimiterOptions _accountBucket;
    private readonly ConcurrencyLimiterOptions _writeConcurrency;
    private readonly ConcurrencyLimiterOptions _balanceConcurrency;
    private readonly ConcurrencyLimiterOptions _statementConcurrency;

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The partitioned limiters are owned by the chain, which disposes them.")]
    public RequestLimiters(IOptions<RateLimitingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _writeClientBucket = BucketFor(_options.WritePerClient, _options.ReplenishmentSeconds);
        _readClientBucket = BucketFor(_options.ReadPerClient, _options.ReplenishmentSeconds);
        _accountBucket = BucketFor(_options.WritePerAccount, _options.ReplenishmentSeconds);
        _writeConcurrency = ConcurrencyFor(_options.WriteConcurrency);
        _balanceConcurrency = ConcurrencyFor(_options.BalanceConcurrency);
        _statementConcurrency = ConcurrencyFor(_options.StatementConcurrency);

        _concurrencyFactory = requestClass => new ConcurrencyLimiter(ConcurrencyOptionsOf(requestClass));
        _clientFactory = key => new TokenBucketRateLimiter(key.IsRead ? _readClientBucket : _writeClientBucket);
        _accountFactory = _ => new TokenBucketRateLimiter(_accountBucket);

        Chain = new SingleAttemptRateLimiter(PartitionedRateLimiter.CreateChained(
            new NamedRateLimiter(
                PartitionedRateLimiter.Create<HttpContext, RequestClass>(ConcurrencyPartition),
                ConcurrencyPolicyOf),
            new NamedRateLimiter(
                PartitionedRateLimiter.Create<HttpContext, ClientQuotaKey>(ClientPartition),
                ClientPolicyOf),
            new NamedRateLimiter(
                PartitionedRateLimiter.Create<HttpContext, Guid>(AccountPartition),
                AccountPolicyOf)));
    }

    public PartitionedRateLimiter<HttpContext> Chain { get; }

    public void Dispose() => Chain.Dispose();

    internal static RequestClass? ClassOf(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<RequestClassMetadata>()?.Class;

    internal RateLimitPartition<RequestClass> ConcurrencyPartition(HttpContext context)
    {
        return _options.Enabled && ClassOf(context) is { } requestClass
            ? RateLimitPartition.Get(requestClass, _concurrencyFactory)
            : RateLimitPartition.GetNoLimiter(default(RequestClass));
    }

    internal RateLimitPartition<ClientQuotaKey> ClientPartition(HttpContext context)
    {
        if (!_options.Enabled || ClassOf(context) is not { } requestClass)
        {
            return RateLimitPartition.GetNoLimiter(default(ClientQuotaKey));
        }

        var isRead = requestClass != RequestClass.Write;
        var clientId = CallerIdentity.ClientIdOf(context);
        var key = clientId is null
            ? new ClientQuotaKey(isRead, null, context.Connection.RemoteIpAddress)
            : new ClientQuotaKey(isRead, clientId, null);

        return RateLimitPartition.Get(key, _clientFactory);
    }

    internal RateLimitPartition<Guid> AccountPartition(HttpContext context)
    {
        if (_options.Enabled
            && ClassOf(context) == RequestClass.Write
            && CallerIdentity.CanWrite(context)
            && context.Request.RouteValues.TryGetValue(AccountIdRouteValue, out var raw)
            && CanonicalGuid.TryParse(raw, out var accountId))
        {
            return RateLimitPartition.Get(accountId, _accountFactory);
        }

        return RateLimitPartition.GetNoLimiter(Guid.Empty);
    }

    private static TokenBucketRateLimiterOptions BucketFor(TokenBucketSettings bucket, int replenishmentSeconds)
    {
        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = bucket.Capacity,
            TokensPerPeriod = (int)Math.Min(int.MaxValue, (long)bucket.RefillPerSecond * replenishmentSeconds),
            ReplenishmentPeriod = TimeSpan.FromSeconds(replenishmentSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        };
    }

    private static ConcurrencyLimiterOptions ConcurrencyFor(int limit)
    {
        return new ConcurrencyLimiterOptions { PermitLimit = limit, QueueLimit = 0 };
    }

    private static string ConcurrencyPolicyOf(HttpContext context)
    {
        return ClassOf(context) switch
        {
            RequestClass.Balance => RateLimitPolicyNames.BalanceConcurrency,
            RequestClass.Statement => RateLimitPolicyNames.StatementConcurrency,
            _ => RateLimitPolicyNames.WriteConcurrency
        };
    }

    private static string ClientPolicyOf(HttpContext context)
    {
        return ClassOf(context) == RequestClass.Write
            ? RateLimitPolicyNames.WritePerClient
            : RateLimitPolicyNames.ReadPerClient;
    }

    private static string AccountPolicyOf(HttpContext context) => RateLimitPolicyNames.WritePerAccount;

    private ConcurrencyLimiterOptions ConcurrencyOptionsOf(RequestClass requestClass)
    {
        return requestClass switch
        {
            RequestClass.Balance => _balanceConcurrency,
            RequestClass.Statement => _statementConcurrency,
            _ => _writeConcurrency
        };
    }
}
