using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Accounts;

public sealed class CreateAccountHandler(
    IUnitOfWork unitOfWork,
    IHolderDocumentProtector protector,
    IIdGenerator idGenerator,
    ISecurityTelemetry telemetry,
    ILogger<CreateAccountHandler> logger)
{
    private const string SupportedCurrency = "BRL";
    private const decimal MaxOverdraftLimit = 999_999_999.99m;
    private const string CreateAccountOperation = "create_account";
    private const string CreatedOutcome = "created";
    private const string AlreadyCreatedOutcome = "already_created";
    private const string KeyUnavailableOutcome = "key_unavailable";
    private const string FailedOutcome = "failed";
    private const int MaxReservationAttempts = 2;

    public async Task<Result<CreatedAccount>> HandleAsync(
        CreateAccountCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (command.Currency != SupportedCurrency)
        {
            return AccountErrors.UnsupportedCurrency;
        }

        if (command.OverdraftLimit.Currency != command.Currency)
        {
            return AccountErrors.CurrencyMismatch;
        }

        if (command.OverdraftLimit.Amount is < 0m or > MaxOverdraftLimit)
        {
            return AccountErrors.InvalidOverdraftLimit;
        }

        using var operation = telemetry.BeginCreateAccount();

        try
        {
            return await CreateAsync(command, operation, cancellationToken);
        }
        catch (KeyProviderUnavailableException)
        {
            operation.Outcome(KeyUnavailableOutcome);
            AccountLog.KeyProviderUnavailable(logger, CreateAccountOperation);
            throw;
        }
        catch (Exception)
        {
            operation.Outcome(FailedOutcome);
            throw;
        }
    }

    private async Task<Result<CreatedAccount>> CreateAsync(
        CreateAccountCommand command,
        IAccountCreationOperation operation,
        CancellationToken cancellationToken)
    {
        var accountId = NewAccountId();
        var document = protector.Protect(command.HolderDocument, accountId);
        var account = new NewAccount(accountId, command.Currency, command.OverdraftLimit, document);
        var reservation = ReservationFor(command, account);
        var attempts = 0;

        var result = await unitOfWork.ExecuteAsync(
            async (scope, token) =>
            {
                attempts++;

                return reservation is null
                    ? await WriteAsync(scope, account, command, attempts, token)
                    : await WriteWithKeyAsync(scope, account, command, reservation, token);
            },
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error;
        }

        var write = result.Value;
        var isReplay = write.AccountId != accountId;

        operation.Outcome(write.AlreadyCreated ? AlreadyCreatedOutcome : CreatedOutcome);

        if (!isReplay)
        {
            telemetry.AccountCreated();
            AccountLog.AccountCreated(logger, accountId, command.ClientId);
        }

        return new CreatedAccount(
            write.AccountId,
            command.Currency,
            command.OverdraftLimit,
            command.HolderDocument.Masked(),
            write.CreatedAt,
            isReplay);
    }

    private static AccountCreationKeyReservation? ReservationFor(CreateAccountCommand command, NewAccount account)
    {
        if (command.IdempotencyKey is not { } key)
        {
            return null;
        }

        var requestHash = AccountCreationRequestHash.Compute(
            command.Currency,
            command.OverdraftLimit,
            account.Document.BlindIndex.Span);

        return new AccountCreationKeyReservation(
            command.ClientId,
            key,
            requestHash,
            AccountCreationRequestHash.CurrentVersion,
            account.Id);
    }

    private static async Task<Result<AccountWrite>> WriteAsync(
        IUnitOfWorkScope scope,
        NewAccount account,
        CreateAccountCommand command,
        int attempt,
        CancellationToken cancellationToken)
    {
        var createdAt = await scope.Accounts.CreateAsync(account, cancellationToken);

        if (createdAt is { } created)
        {
            return await RecordCreationAsync(scope, account, command, created, cancellationToken);
        }

        if (attempt == 1)
        {
            throw new InvalidOperationException("The generated account id already exists.");
        }

        var existing = await scope.Accounts.GetCreatedAtAsync(account.Id, cancellationToken);

        return existing is { } previous
            ? new AccountWrite(account.Id, previous, true)
            : throw new InvalidOperationException("The account could not be read back after a duplicate key.");
    }

    private async Task<Result<AccountWrite>> WriteWithKeyAsync(
        IUnitOfWorkScope scope,
        NewAccount account,
        CreateAccountCommand command,
        AccountCreationKeyReservation reservation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxReservationAttempts; attempt++)
        {
            if (await scope.Accounts.TryReserveCreationKeyAsync(reservation, cancellationToken))
            {
                var createdAt = await scope.Accounts.CreateAsync(account, cancellationToken)
                                ?? throw new InvalidOperationException("The generated account id already exists.");

                return await RecordCreationAsync(scope, account, command, createdAt, cancellationToken);
            }

            var record = await scope.Accounts.FindCreationKeyAsync(command.ClientId, reservation.Key, cancellationToken);

            if (record is not null)
            {
                scope.MarkForRollback();

                return Replay(record, command);
            }
        }

        throw new InvalidOperationException(
            "The idempotency key is reserved by another request but its record could not be read back.");
    }

    private Result<AccountWrite> Replay(AccountCreationKeyRecord record, CreateAccountCommand command)
    {
        if (record.HashVersion != AccountCreationRequestHash.CurrentVersion)
        {
            throw new InvalidOperationException("The account creation record uses an unknown request hash version.");
        }

        var matches = protector.BlindIndexCandidates(command.HolderDocument).Any(index =>
            AccountCreationRequestHash.Matches(
                record.RequestHash.Span,
                AccountCreationRequestHash.Compute(command.Currency, command.OverdraftLimit, index.Span)));

        return matches
            ? new AccountWrite(record.AccountId, record.CreatedAt, true)
            : AccountErrors.CreationKeyReused;
    }

    private static async Task<AccountWrite> RecordCreationAsync(
        IUnitOfWorkScope scope,
        NewAccount account,
        CreateAccountCommand command,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var auditEvent = AuditEvents.AccountCreated(command.ClientId, account.Id, command.CorrelationId);
        await scope.Audit.RecordAsync(auditEvent, cancellationToken);

        return new AccountWrite(account.Id, createdAt, false);
    }

    private AccountId NewAccountId()
    {
        var accountId = AccountId.From(idGenerator.NewId());

        return accountId.IsSuccess
            ? accountId.Value
            : throw new InvalidOperationException("The id generator returned an empty identifier.");
    }
}
