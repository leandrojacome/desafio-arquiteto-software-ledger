using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Ledger.EndToEnd.Tests.Support;

internal sealed class E2EApi(HttpClient http, string? token = null)
{
    private const string Json = "application/json";

    private const int MaxPacedAttempts = 20;

    public string Token { get; } = token ?? E2ETokenFactory.Create();

    public static string NewKey() => Guid.NewGuid().ToString("N");

    public async Task<E2EResponse> SendAsync(
        HttpMethod method,
        string path,
        string? body = null,
        string? idempotencyKey = null,
        string? token = null,
        bool anonymous = false,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (!anonymous)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? Token);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, Json);
        }

        var started = Stopwatch.GetTimestamp();
        using var response = await http.SendAsync(request, CancellationToken.None);

        return await E2EResponse.FromAsync(response, Stopwatch.GetElapsedTime(started));
    }

    public async Task<int> ProbeStatusAsync(string path)
    {
        try
        {
            return (await SendAsync(HttpMethod.Get, path, anonymous: true)).StatusCode;
        }
        catch (HttpRequestException)
        {
            return 0;
        }
        catch (TaskCanceledException)
        {
            return 0;
        }
    }

    public async Task<string> CreateAccountAsync(string? overdraftLimit = null)
    {
        var response = await OpenAccountAsync(NewCpf(), overdraftLimit);

        response.StatusCode.ShouldBe(201, response.Body);

        return response.Text("accountId");
    }

    public Task<E2EResponse> OpenAccountAsync(string document, string? overdraftLimit = null, string currency = "BRL", string? token = null)
    {
        var limit = overdraftLimit is null ? string.Empty : $",\"overdraftLimit\":\"{overdraftLimit}\"";
        var body = $"{{\"holderDocument\":\"{document}\",\"currency\":\"{currency}\"{limit}}}";

        return SendAsync(HttpMethod.Post, "/v1/accounts", body, token: token);
    }

    public Task<E2EResponse> CreditAsync(string accountId, string amount, string? key = null, string? token = null) =>
        PostEntryAsync(accountId, "CREDIT", amount, key, token);

    public async Task<E2EResponse> CreditPacedAsync(string accountId, string amount)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await CreditAsync(accountId, amount);

            if (response.StatusCode != 429 || attempt >= MaxPacedAttempts)
            {
                return response;
            }

            var seconds = int.TryParse(response.Header("Retry-After"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? Math.Max(1, parsed)
                : 1;

            await Task.Delay(TimeSpan.FromSeconds(seconds), CancellationToken.None);
        }
    }

    public Task<E2EResponse> DebitAsync(string accountId, string amount, string? key = null, string? token = null) =>
        PostEntryAsync(accountId, "DEBIT", amount, key, token);

    public Task<E2EResponse> PostEntryAsync(
        string accountId,
        string type,
        string amount,
        string? key = null,
        string? token = null,
        bool anonymous = false)
    {
        var body = string.Create(CultureInfo.InvariantCulture, $"{{\"type\":\"{type}\",\"amount\":\"{amount}\",\"currency\":\"BRL\"}}");

        return SendAsync(HttpMethod.Post, $"/v1/accounts/{accountId}/entries", body, key ?? NewKey(), token, anonymous);
    }

    public Task<E2EResponse> ReverseAsync(string accountId, string entryId, string? key = null) =>
        SendAsync(HttpMethod.Post, $"/v1/accounts/{accountId}/entries/{entryId}/reversals", null, key ?? NewKey());

    public Task<E2EResponse> BalanceAsync(string accountId, string? asOf = null, string? token = null, bool anonymous = false)
    {
        var query = asOf is null ? string.Empty : $"?asOf={Uri.EscapeDataString(asOf)}";

        return SendAsync(HttpMethod.Get, $"/v1/accounts/{accountId}/balance{query}", token: token, anonymous: anonymous);
    }

    public Task<E2EResponse> StatementAsync(string accountId, string? query = null, string? token = null, bool anonymous = false)
    {
        var suffix = string.IsNullOrEmpty(query) ? string.Empty : "?" + query;

        return SendAsync(HttpMethod.Get, $"/v1/accounts/{accountId}/entries{suffix}", token: token, anonymous: anonymous);
    }

    public async Task<string> FundedAccountAsync(string amount)
    {
        var accountId = await CreateAccountAsync();
        var funding = await CreditAsync(accountId, amount);

        funding.StatusCode.ShouldBe(201, funding.Body);

        return accountId;
    }

    public static string NewCpf()
    {
        var digits = new int[11];

        for (var index = 0; index < 9; index++)
        {
            digits[index] = RandomNumberGenerator.GetInt32(0, 10);
        }

        if (digits.Take(9).Distinct().Count() == 1)
        {
            digits[0] = (digits[0] + 1) % 10;
        }

        digits[9] = CheckDigit(digits, 9);
        digits[10] = CheckDigit(digits, 10);

        return string.Concat(digits.Select(digit => digit.ToString(CultureInfo.InvariantCulture)));
    }

    private static int CheckDigit(int[] digits, int length)
    {
        var sum = 0;

        for (var index = 0; index < length; index++)
        {
            sum += digits[index] * (length + 1 - index);
        }

        var remainder = sum * 10 % 11;

        return remainder == 10 ? 0 : remainder;
    }
}
