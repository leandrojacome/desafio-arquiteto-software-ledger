extern alias LedgerWorker;

using System.Reflection;
using Ledger.Application;
using Ledger.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Ledger.Api.IntegrationTests.Observability;

[Trait("Category", "Unit")]
public sealed class LogEventIdsTests
{
    private static readonly string[] ForbiddenParameterNames =
    [
        "document", "holderDocument", "cpf", "cnpj", "token", "authorization", "password", "secret", "key"
    ];

    private static readonly Dictionary<int, string> DocumentedNames = new()
    {
        [1001] = "EntryAccepted",
        [1002] = "IdempotentReplayDelivered",
        [1003] = "IdempotencyKeyReused",
        [1004] = "InsufficientFundsRejected",
        [1005] = "RecordedAtCorrected",
        [2001] = "BalanceQueried",
        [2002] = "StatementQueried",
        [3001] = "OutboxBatchPublished",
        [3002] = "OutboxPublishFailed",
        [3003] = "OutboxCircuitStateChanged",
        [3004] = "OutboxPruned",
        [3005] = "OutboxMessageStuck",
        [3006] = "BrokerConnected",
        [3007] = "BrokerConnectionFailed",
        [3008] = "WorkerLoopFailed",
        [3009] = "OutboxBatchUnroutable",
        [3010] = "RetentionQueueKeptAsFound",
        [3011] = "OutboxRoutingRestored",
        [4001] = "IntegrityRunCompleted",
        [4002] = "IntegrityViolationDetected",
        [4003] = "IntegrityRunFailed",
        [4005] = "IntegrityWindowTruncated",
        [4006] = "AccountIntegrityInspected",
        [4007] = "AccountIntegrityFinding",
        [6001] = "AuthenticationRejected",
        [6002] = "AuthorizationDenied",
        [7001] = "AccountCreated",
        [7002] = "KeyProviderUnavailable",
        [7006] = "RewrapBatchCompleted",
        [7007] = "DocumentDecryptFailed",
        [7008] = "KeyActivationLookupFailed",
        [7009] = "KeyVersionsVanished",
        [7010] = "KeyVersionAheadOfActive",
        [7011] = "KeyVersionNotLive",
        [9201] = "TelemetryExportConfigured"
    };

    private static readonly Assembly[] LedgerAssemblies =
    [
        ApplicationAssembly.Reference,
        InfrastructureAssembly.Reference,
        typeof(Program).Assembly,
        typeof(LedgerWorker::Program).Assembly
    ];

    private static List<DeclaredEvent> Declared()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                                 | BindingFlags.DeclaredOnly;

        return LedgerAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(all))
            .Select(method => new { Method = method, Attribute = method.GetCustomAttribute<LoggerMessageAttribute>() })
            .Where(candidate => candidate.Attribute is not null && candidate.Attribute.EventId != 0)
            .Select(candidate => new DeclaredEvent(
                candidate.Attribute!.EventId,
                candidate.Attribute.EventName,
                $"{candidate.Method.DeclaringType?.Name}.{candidate.Method.Name}",
                [.. candidate.Method.GetParameters().Select(parameter => parameter.Name ?? string.Empty)]))
            .ToList();
    }

    [Fact]
    public void EveryEventId_IsDeclaredByASingleLogMessage()
    {
        var duplicated = Declared()
            .GroupBy(declared => declared.Id)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(declared => declared.Method))}")
            .ToList();

        duplicated.ShouldBeEmpty();
    }

    [Fact]
    public void TheDeclaredEvents_KeepTheNamesOfTheDocumentation()
    {
        var declared = Declared();

        declared.ShouldNotBeEmpty();

        foreach (var (id, name) in DocumentedNames)
        {
            declared.Single(candidate => candidate.Id == id).Name.ShouldBe(name, $"event {id}");
        }
    }

    [Fact]
    public void TheApplicationEvents_ThatTheContractsPromise_AreAllPresent()
    {
        var ids = Declared().Select(declared => declared.Id).ToHashSet();

        foreach (var id in new[] { 1001, 1002, 1003, 1004, 1005, 2001, 2002, 3001, 3002, 3004, 3005, 3009, 3010, 3011, 4001, 4002, 4005, 6001, 6002, 7001, 7002, 7006, 7007, 7009, 7010, 7011, 9201 })
        {
            ids.ShouldContain(id);
        }
    }

    [Fact]
    public void NoLogMessageParameter_IsNamedAfterASensitiveValue()
    {
        var offenders = Declared()
            .SelectMany(declared => declared.Parameters
                .Where(parameter => ForbiddenParameterNames.Contains(parameter, StringComparer.OrdinalIgnoreCase))
                .Select(parameter => $"{declared.Method}({parameter})"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private sealed record DeclaredEvent(int Id, string? Name, string Method, IReadOnlyList<string> Parameters);
}
