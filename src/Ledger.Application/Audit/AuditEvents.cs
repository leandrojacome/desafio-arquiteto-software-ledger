using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Audit;

public static partial class AuditEvents
{
    public const string WorkerClientId = ServiceIdentity.Worker;
    public const string RequiredWriteScope = "ledger.write";
    public const string RewrapPurpose = "rewrap";

    private const string RoutePattern = @"^(POST|PUT|PATCH|DELETE) /[A-Za-z0-9/{}_-]{1,120}\z";
    private const string GuidPattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false
    };

    public static AuditEvent AccountCreated(string clientId, AccountId accountId, string correlationId)
    {
        return new AuditEvent(
            AuditEventTypes.AccountCreated,
            clientId,
            accountId,
            correlationId,
            AuditOutcome.Success,
            WriteDetails(_ => { }));
    }

    public static AuditEvent AuthorizationDenied(
        string clientId,
        AccountId? accountId,
        string correlationId,
        string route,
        DeniedWriteReason reason)
    {
        if (!Route().IsMatch(route) || EmbeddedGuid().IsMatch(route))
        {
            throw new ArgumentException("The route must be a route template, never a real path.", nameof(route));
        }

        var reasonText = reason switch
        {
            DeniedWriteReason.InsufficientScope => "insufficient_scope",
            DeniedWriteReason.NotProvisioningClient => "not_provisioning_client",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown denial reason.")
        };

        return new AuditEvent(
            AuditEventTypes.AuthorizationDeniedWrite,
            clientId,
            accountId,
            correlationId,
            AuditOutcome.Denied,
            WriteDetails(writer =>
            {
                writer.WriteString("route", route);
                writer.WriteString("requiredScope", RequiredWriteScope);
                writer.WriteString("reason", reasonText);
            }));
    }

    public static AuditEvent IntegrityRunCompleted(string runId, bool withoutViolations, string detailsJson)
    {
        return new AuditEvent(
            AuditEventTypes.IntegrityRunCompleted,
            WorkerClientId,
            null,
            runId,
            withoutViolations ? AuditOutcome.Success : AuditOutcome.Failure,
            detailsJson);
    }

    public static AuditEvent IntegrityViolationDetected(string runId, AccountId accountId, string detailsJson)
    {
        return new AuditEvent(
            AuditEventTypes.IntegrityViolationDetected,
            WorkerClientId,
            accountId,
            runId,
            AuditOutcome.Failure,
            detailsJson);
    }

    public static AuditEvent PiiDecrypted(string passId, int accounts)
    {
        return new AuditEvent(
            AuditEventTypes.PiiDecrypted,
            WorkerClientId,
            null,
            passId,
            AuditOutcome.Success,
            WriteDetails(writer =>
            {
                writer.WriteString("purpose", RewrapPurpose);
                writer.WriteNumber("accounts", accounts);
            }));
    }

    public static AuditEvent PiiRewrapped(string passId, int fromVersion, int toVersion, int accounts, int failed)
    {
        return new AuditEvent(
            AuditEventTypes.PiiRewrapped,
            WorkerClientId,
            null,
            passId,
            AuditOutcome.Success,
            WriteDetails(writer =>
            {
                writer.WriteNumber("fromVersion", fromVersion);
                writer.WriteNumber("toVersion", toVersion);
                writer.WriteNumber("accounts", accounts);
                writer.WriteNumber("failed", failed);
            }));
    }

    public static AuditEvent KeysVersionActivated(string passId, int version)
    {
        return new AuditEvent(
            AuditEventTypes.KeysVersionActivated,
            WorkerClientId,
            null,
            passId,
            AuditOutcome.Success,
            WriteDetails(writer => writer.WriteNumber("version", version)));
    }

    [GeneratedRegex(RoutePattern, RegexOptions.CultureInvariant)]
    private static partial Regex Route();

    [GeneratedRegex(GuidPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedGuid();

    private static string WriteDetails(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
