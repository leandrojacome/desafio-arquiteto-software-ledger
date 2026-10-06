using Dapper;
using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresAuditTrail(
    IPostgresConnectionFactory connections,
    PostgresSource source,
    ISecurityTelemetry telemetry) : IAuditTrail
{
    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(source, cancellationToken);

        await connection.ExecuteAsync(AuditCommands.Insert(auditEvent, null, cancellationToken));

        telemetry.AuditRecorded(auditEvent.EventType, auditEvent.Outcome);
    }

    public async Task<bool> ContainsAsync(
        string eventType,
        string detailName,
        string detailValue,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(source, cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            AuditCommands.Contains(eventType, detailName, detailValue, null, cancellationToken));
    }
}

internal sealed class PostgresScopedAuditTrail(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    ISecurityTelemetry telemetry) : IAuditTrail
{
    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(AuditCommands.Insert(auditEvent, transaction, cancellationToken));

        telemetry.AuditRecorded(auditEvent.EventType, auditEvent.Outcome);
    }

    public async Task<bool> ContainsAsync(
        string eventType,
        string detailName,
        string detailValue,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<bool>(
            AuditCommands.Contains(eventType, detailName, detailValue, transaction, cancellationToken));
    }
}
