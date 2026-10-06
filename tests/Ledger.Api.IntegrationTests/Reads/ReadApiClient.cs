using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

internal sealed class ReadApiClient : IDisposable
{
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly HttpClient _client;

    private ReadApiClient(HttpClient client)
    {
        _client = client;
    }

    public static ReadApiClient For(LedgerApiFactory factory, string? token = null) => new(factory.Authenticated(token));

    public static ReadApiClient Anonymous(LedgerApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return new ReadApiClient(factory.CreateClient());
    }

    public static string BalancePath(AccountId accountId) => $"/v1/accounts/{accountId}/balance";

    public static string StatementPath(AccountId accountId) => $"/v1/accounts/{accountId}/entries";

    public async Task<ReadResponse> SendAsync(HttpMethod method, string pathAndQuery, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(method, pathAndQuery);

        if (correlationId is not null)
        {
            request.Headers.Add(CorrelationHeader, correlationId);
        }

        using var response = await _client.SendAsync(request, CancellationToken.None);

        return await ReadResponse.FromAsync(response, CancellationToken.None);
    }

    public Task<ReadResponse> GetAsync(string pathAndQuery, string? correlationId = null) =>
        SendAsync(HttpMethod.Get, pathAndQuery, correlationId);

    public Task<ReadResponse> BalanceAsync(AccountId accountId, string? correlationId = null) =>
        GetAsync(BalancePath(accountId), correlationId);

    public Task<ReadResponse> BalanceAsOfAsync(AccountId accountId, string asOf, string? correlationId = null) =>
        GetAsync($"{BalancePath(accountId)}?asOf={Uri.EscapeDataString(asOf)}", correlationId);

    public Task<ReadResponse> BalanceAtAsync(AccountId accountId, DateTimeOffset asOf) =>
        BalanceAsOfAsync(accountId, StatementItem.FormatInstant(asOf));

    public Task<ReadResponse> StatementAsync(AccountId accountId, string? query = null, string? correlationId = null) =>
        GetAsync(query is null ? StatementPath(accountId) : $"{StatementPath(accountId)}?{query}", correlationId);

    public async Task<StatementWalk> WalkAsync(
        AccountId accountId,
        int limit,
        string? from = null,
        string? to = null,
        string? startCursor = null)
    {
        var items = new List<StatementItem>();
        var pageSizes = new List<int>();
        var cursors = new List<string?>();
        var cursor = startCursor;

        do
        {
            var query = $"limit={limit}";
            query += from is null ? string.Empty : $"&from={Uri.EscapeDataString(from)}";
            query += to is null ? string.Empty : $"&to={Uri.EscapeDataString(to)}";
            query += cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}";

            using var response = await StatementAsync(accountId, query);

            response.Status.ShouldBe(System.Net.HttpStatusCode.OK, response.Body);

            var page = StatementItem.ItemsOf(response);

            items.AddRange(page);
            pageSizes.Add(page.Count);

            cursor = response.IsNull("nextCursor") ? null : response.Text("nextCursor");
            cursors.Add(cursor);
        }
        while (cursor is not null);

        return new StatementWalk(items, pageSizes, cursors);
    }

    public void Dispose() => _client.Dispose();
}

internal sealed record StatementWalk(
    IReadOnlyList<StatementItem> Items,
    IReadOnlyList<int> PageSizes,
    IReadOnlyList<string?> Cursors);
