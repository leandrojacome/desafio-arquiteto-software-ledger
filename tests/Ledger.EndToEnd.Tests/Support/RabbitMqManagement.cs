using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ledger.EndToEnd.Tests.Support;

internal sealed record ReceivedEvent(string MessageId, string RoutingKey, string CorrelationId, JsonElement Payload)
{
    public string AccountId => Payload.GetProperty("accountId").GetString() ?? string.Empty;

    public string EntryId => Payload.GetProperty("entryId").GetString() ?? string.Empty;
}

internal sealed class RabbitMqManagement : IDisposable
{
    public const string Exchange = "ledger.events";

    public const string EntryRegisteredKey = "EntryRegistered";

    public const string RetentionQueue = "retention.ledger.entry-registered";

    public const string RetentionDeadLetterQueue = RetentionQueue + ".dead-letter";

    private const string VirtualHost = "%2F";

    private readonly HttpClient _http;

    public RabbitMqManagement(Uri managementUrl, string user, string password)
    {
        _http = new HttpClient { BaseAddress = managementUrl, Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
    }

    public static string NewQueueName(string purpose) => $"e2e.{purpose}.{Guid.NewGuid():N}";

    public async Task DeclareBoundQueueAsync(string queue)
    {
        using var declare = await PutAsync($"/api/queues/{VirtualHost}/{queue}", """{"durable":true,"auto_delete":false,"arguments":{}}""");

        declare.EnsureSuccessStatusCode();

        using var bind = await PostAsync(
            $"/api/bindings/{VirtualHost}/e/{Exchange}/q/{queue}",
            $$"""{"routing_key":"{{EntryRegisteredKey}}"}""");

        bind.EnsureSuccessStatusCode();
    }

    public async Task<JsonElement?> QueueAsync(string queue)
    {
        using var response = await _http.GetAsync($"/api/queues/{VirtualHost}/{queue}", CancellationToken.None);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        return document.RootElement.Clone();
    }

    public async Task<IReadOnlyList<(string Source, string RoutingKey)>> BindingsAsync(string queue)
    {
        using var response = await _http.GetAsync($"/api/queues/{VirtualHost}/{queue}/bindings", CancellationToken.None);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        return
        [
            .. document.RootElement.EnumerateArray().Select(binding => (
                binding.GetProperty("source").GetString() ?? string.Empty,
                binding.GetProperty("routing_key").GetString() ?? string.Empty))
        ];
    }

    public async Task DeleteQueueAsync(string queue)
    {
        using var response = await _http.DeleteAsync($"/api/queues/{VirtualHost}/{queue}", CancellationToken.None);
    }

    public async Task<long> ReadyCountAsync(string queue)
    {
        using var response = await _http.GetAsync($"/api/queues/{VirtualHost}/{queue}", CancellationToken.None);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));

        return document.RootElement.TryGetProperty("messages_ready", out var ready) ? ready.GetInt64() : 0;
    }

    public async Task<IReadOnlyList<ReceivedEvent>> ConsumeAsync(string queue, int count)
    {
        var body = $$"""{"count":{{count}},"ackmode":"ack_requeue_false","encoding":"auto"}""";
        using var response = await PostAsync($"/api/queues/{VirtualHost}/{queue}/get", body);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
        var events = new List<ReceivedEvent>();

        foreach (var message in document.RootElement.EnumerateArray())
        {
            var properties = message.GetProperty("properties");
            var messageId = properties.TryGetProperty("message_id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
            var payload = JsonDocument.Parse(message.GetProperty("payload").GetString() ?? "{}").RootElement.Clone();

            var correlationId = properties.TryGetProperty("correlation_id", out var correlation) ? correlation.GetString() ?? string.Empty : string.Empty;

            events.Add(new ReceivedEvent(messageId, message.GetProperty("routing_key").GetString() ?? string.Empty, correlationId, payload));
        }

        return events;
    }

    public async Task<IReadOnlyList<ReceivedEvent>> ConsumeAllAsync(string queue)
    {
        var all = new List<ReceivedEvent>();

        while (true)
        {
            var batch = await ConsumeAsync(queue, 200);

            if (batch.Count == 0)
            {
                return all;
            }

            all.AddRange(batch);
        }
    }

    public async Task<IReadOnlyList<ReceivedEvent>> ConsumeUntilAsync(
        string queue,
        Func<IReadOnlyList<ReceivedEvent>, bool> done,
        TimeSpan timeout,
        string failure)
    {
        var collected = new List<ReceivedEvent>();

        await E2EWait.UntilAsync(
            async () =>
            {
                collected.AddRange(await ConsumeAllAsync(queue));

                return done(collected);
            },
            timeout,
            failure,
            TimeSpan.FromMilliseconds(400));

        return collected;
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> PutAsync(string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        return await _http.PutAsync(path, content, CancellationToken.None);
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        return await _http.PostAsync(path, content, CancellationToken.None);
    }
}
