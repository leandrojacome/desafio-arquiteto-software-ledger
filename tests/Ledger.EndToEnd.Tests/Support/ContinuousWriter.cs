using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Ledger.EndToEnd.Tests.Support;

internal sealed record WriteOutcome(int Status, string? RetryAfter, string Code, TimeSpan Elapsed);

internal sealed class ContinuousWriter(E2EApi api, string accountId, int parallelism, TimeSpan pause) : IDisposable
{
    public const string Amount = "1.00";

    private readonly ConcurrentDictionary<string, WriteOutcome> _outcomes = new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _stop = new();

    private Task[] _workers = [];

    public IReadOnlyDictionary<string, WriteOutcome> Outcomes => _outcomes;

    public int Accepted => _outcomes.Count(outcome => outcome.Value.Status == 201);

    public int Refused => _outcomes.Count(outcome => outcome.Value.Status == 503);

    public void Start()
    {
        _workers = [.. Enumerable.Range(0, parallelism).Select(_ => Task.Run(LoopAsync))];
    }

    public async Task StopAsync()
    {
        await _stop.CancelAsync();
        await Task.WhenAll(_workers);
    }

    public void Dispose() => _stop.Dispose();

    public async Task<WriteOutcome> AttemptAsync(string key)
    {
        try
        {
            var response = await api.CreditAsync(accountId, Amount, key);

            return new WriteOutcome(response.StatusCode, response.Header("Retry-After"), CodeOf(response), response.Elapsed);
        }
        catch (HttpRequestException exception)
        {
            return new WriteOutcome(0, null, exception.GetType().Name, TimeSpan.Zero);
        }
        catch (TaskCanceledException exception)
        {
            return new WriteOutcome(-1, null, exception.GetType().Name, TimeSpan.Zero);
        }
    }

    public async Task<E2EResponse> RepeatUntilAcceptedAsync(string key, TimeSpan timeout)
    {
        var started = TimeProvider.System.GetTimestamp();

        while (true)
        {
            var response = await api.CreditAsync(accountId, Amount, key);

            if (response.StatusCode == 201)
            {
                return response;
            }

            if (TimeProvider.System.GetElapsedTime(started) > timeout || response.StatusCode is not (429 or 503))
            {
                throw new Xunit.Sdk.XunitException(
                    $"Repeating the key {key} answered {response.StatusCode} instead of being accepted: {response.Body}");
            }

            var seconds = int.TryParse(response.Header("Retry-After"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? Math.Clamp(parsed, 1, 5)
                : 1;

            await Task.Delay(TimeSpan.FromSeconds(seconds), CancellationToken.None);
        }
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var key = E2EApi.NewKey();

            _outcomes[key] = await AttemptAsync(key);

            await Task.Delay(pause, CancellationToken.None);
        }
    }

    private static string CodeOf(E2EResponse response)
    {
        if (response.StatusCode < 400)
        {
            return string.Empty;
        }

        try
        {
            return response.Json().TryGetProperty("code", out var code) ? code.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
