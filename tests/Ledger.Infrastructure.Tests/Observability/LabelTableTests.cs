using System.Reflection;
using System.Text.RegularExpressions;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed partial class LabelTableTests
{
    public static TheoryData<Type, string[]> ClosedSets() =>
        new()
        {
            { typeof(EntryTypeLabel), ["credit", "debit", "reversal"] },
            { typeof(EntryResultLabel), ["recorded", "replayed", "rejected", "failed"] },
            {
                typeof(RejectionReason),
                [
                    "insufficient_funds", "currency_mismatch", "account_not_found", "idempotency_conflict",
                    "entry_already_reversed", "entry_not_reversible", "entry_not_found", "validation"
                ]
            },
            { typeof(BalanceMode), ["current", "as_of"] },
            {
                typeof(DbOperation),
                [
                    "insert_entry", "update_balance", "insert_outbox", "insert_idempotency_key", "select_balance",
                    "select_balance_as_of", "select_entries"
                ]
            },
            { typeof(DbRetryReason), ["deadlock", "serialization_failure", "transient_connection"] },
            {
                typeof(AuthFailureReason),
                ["missing_token", "invalid_token", "expired", "insufficient_scope", "missing_client_id", "invalid_client_id"]
            },
            {
                typeof(RateLimitPolicy),
                [
                    "write-per-client", "read-per-client", "write-per-account", "write-concurrency",
                    "balance-concurrency", "statement-concurrency"
                ]
            },
            { typeof(PiiPurpose), ["rewrap", "holder_lookup", "investigation"] },
            { typeof(ReloadResult), ["ok", "failed"] },
            {
                typeof(AuditEventLabel),
                [
                    "account.created", "authorization.denied_write", "pii.decrypted", "pii.rewrapped",
                    "keys.version_activated"
                ]
            },
            { typeof(AuditOutcomeLabel), ["SUCCESS", "DENIED"] },
            { typeof(AuditSkipReason), ["rate_capped", "write_failed"] },
            { typeof(RewrapResult), ["rewrapped", "failed"] },
            { typeof(CreateAccountOutcome), ["created", "already_created", "key_unavailable", "failed"] },
            { typeof(IntegrityResult), ["ok", "violation", "error"] },
            { typeof(IntegrityViolationKind), ["balance_mismatch", "chain_broken"] },
            { typeof(PublishOutcome), ["confirmed", "failed"] }
        };

    [Theory]
    [MemberData(nameof(ClosedSets))]
    public void Labels_AreExactlyTheSetOfTheContract(Type enumType, string[] expected)
    {
        TextsOf(enumType).Order().ShouldBe(expected.Order());
    }

    [Theory]
    [MemberData(nameof(ClosedSets))]
    public void Labels_AreShortIdentifiersWithoutSpacesOrIds(Type enumType, string[] expected)
    {
        expected.ShouldNotBeEmpty();

        foreach (var text in TextsOf(enumType))
        {
            SafeLabel().IsMatch(text).ShouldBeTrue(text);
            text.Length.ShouldBeLessThanOrEqualTo(32, text);
        }
    }

    [Fact]
    public void Parse_AcceptsOnlyTheExactTextOfAMember()
    {
        LabelTable<EntryTypeLabel>.TryParse("credit", out var credit).ShouldBeTrue();
        credit.ShouldBe(EntryTypeLabel.Credit);

        LabelTable<EntryTypeLabel>.TryParse("CREDIT", out _).ShouldBeFalse();
        LabelTable<EntryTypeLabel>.TryParse(" credit", out _).ShouldBeFalse();
        LabelTable<EntryTypeLabel>.TryParse("credit\n", out _).ShouldBeFalse();
        LabelTable<EntryTypeLabel>.TryParse("", out _).ShouldBeFalse();
        LabelTable<EntryTypeLabel>.TryParse(null, out _).ShouldBeFalse();
        LabelTable<AuditOutcomeLabel>.TryParse("success", out _).ShouldBeFalse();
        LabelTable<AuditOutcomeLabel>.TryParse("SUCCESS", out var success).ShouldBeTrue();
        success.ShouldBe(AuditOutcomeLabel.Success);
    }

    [Fact]
    public void Labels_RoundTripForEveryMemberOfEveryEnum()
    {
        RoundTrip<EntryTypeLabel>();
        RoundTrip<EntryResultLabel>();
        RoundTrip<RejectionReason>();
        RoundTrip<BalanceMode>();
        RoundTrip<DbOperation>();
        RoundTrip<DbRetryReason>();
        RoundTrip<AuthFailureReason>();
        RoundTrip<RateLimitPolicy>();
        RoundTrip<PiiPurpose>();
        RoundTrip<ReloadResult>();
        RoundTrip<AuditEventLabel>();
        RoundTrip<AuditOutcomeLabel>();
        RoundTrip<AuditSkipReason>();
        RoundTrip<RewrapResult>();
        RoundTrip<CreateAccountOutcome>();
        RoundTrip<IntegrityResult>();
        RoundTrip<IntegrityViolationKind>();
        RoundTrip<PublishOutcome>();
    }

    [Fact]
    public void EveryLabelEnumOfTheAssembly_IsCoveredByTheClosedSetTable()
    {
        var declared = typeof(LabelTable<>).Assembly
            .GetTypes()
            .Where(type => type is { IsEnum: true, Namespace: "Ledger.Infrastructure.Observability.Labels" })
            .ToHashSet();
        var covered = ClosedSets().Select(row => (Type)row[0]!).ToHashSet();

        covered.ShouldBe(declared, ignoreOrder: true);
    }

    [Fact]
    public void WorkerLoops_HaveTheDocumentedLabels()
    {
        Ledger.Application.Outbox.WorkerLoop.Outbox.Label().ShouldBe("outbox");
        Ledger.Application.Outbox.WorkerLoop.IntegrityRecent.Label().ShouldBe("integrity-recent");
        Ledger.Application.Outbox.WorkerLoop.IntegrityFull.Label().ShouldBe("integrity-full");
        Ledger.Application.Outbox.WorkerLoop.Measure.Label().ShouldBe("measure");
        Ledger.Application.Outbox.WorkerLoop.OutboxPrune.Label().ShouldBe("prune");
        Ledger.Application.Outbox.WorkerLoop.IdempotencyPrune.Label().ShouldBe("idempotency-prune");
        Ledger.Application.Outbox.WorkerLoop.KeyRewrap.Label().ShouldBe("rewrap");
    }

    private static void RoundTrip<TEnum>()
        where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            LabelTable<TEnum>.TryParse(value.Label(), out var parsed).ShouldBeTrue();
            parsed.ShouldBe(value);
        }
    }

    private static IReadOnlyCollection<string> TextsOf(Type enumType)
    {
        var table = typeof(LabelTable<>).MakeGenericType(enumType);
        var texts = table.GetProperty("Texts", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

        return texts as IReadOnlyCollection<string> ?? throw new InvalidOperationException("Texts is unavailable.");
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeLabel();
}
