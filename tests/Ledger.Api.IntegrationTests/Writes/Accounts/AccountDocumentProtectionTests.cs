using System.Globalization;
using System.Text;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;
using Ledger.Api.IntegrationTests.Writes.Support;
using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Writes.Accounts;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class AccountDocumentProtectionTests(PostgresFixture postgres)
{
    private const int CpfBlobLength = 42;
    private const int CnpjBlobLength = 45;
    private const int BlindIndexLength = 32;

    private readonly WriteTestData _data = new(postgres);

    private static string Document(int index)
    {
        return (index % 3) switch
        {
            0 => CpfFactory.Document(index).Normalized,
            1 => "12345678000195",
            _ => "12ABC34501DE35"
        };
    }

    [DockerFact]
    public async Task ManyAccounts_StoreOnlyTheEncryptedBlobAndTheBlindIndexAndNeverTheDocument()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var protector = factory.Services.GetRequiredService<IHolderDocumentProtector>();
        var created = new List<(string AccountId, string Document)>();

        for (var index = 0; index < 30; index++)
        {
            var document = Document(index);
            var response = await client.PostAccountAsync(WriteClient.AccountBody(document));

            response.StatusCode.ShouldBe(201, response.Body);
            created.Add((response.Text("accountId"), document));
        }

        foreach (var (accountId, document) in created)
        {
            var stored = await _data.StoredAccountAsync(accountId);
            var expectedLength = document.Length == 11 ? CpfBlobLength : CnpjBlobLength;

            stored.Encrypted.Length.ShouldBe(expectedLength);
            stored.Encrypted.Take(3).ShouldBe([(byte)0x01, (byte)0x00, (byte)0x01]);
            stored.KeyVersion.ShouldBe(1);
            stored.BlindIndex.Length.ShouldBe(BlindIndexLength);
            Encoding.UTF8.GetString(stored.Encrypted).ShouldNotContain(document);
            Convert.ToHexStringLower(stored.Encrypted).ShouldNotContain(Convert.ToHexStringLower(Encoding.ASCII.GetBytes(document)));

            var decrypted = protector.Unprotect(stored.Encrypted, WriteTestData.Account(accountId));

            decrypted.IsSuccess.ShouldBeTrue();
            decrypted.Value.Normalized.ShouldBe(document);
        }

        var everything = await _data.EverythingStoredAsTextAsync();

        foreach (var (_, document) in created)
        {
            everything.ShouldNotContain(document, Case.Insensitive);
            everything.ShouldNotContain(Masked(document), Case.Insensitive);
        }
    }

    [DockerFact]
    public async Task SameDocumentTwice_GivesEqualBlindIndexesAndDifferentBlobs()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var first = (await client.PostAccountAsync(WriteClient.AccountBody("123.456.789-09"))).Text("accountId");
        var second = (await client.PostAccountAsync(WriteClient.AccountBody("12345678909"))).Text("accountId");
        var other = (await client.PostAccountAsync(WriteClient.AccountBody(CpfFactory.Document(77).Normalized))).Text("accountId");

        var storedFirst = await _data.StoredAccountAsync(first);
        var storedSecond = await _data.StoredAccountAsync(second);
        var storedOther = await _data.StoredAccountAsync(other);

        storedFirst.BlindIndex.ShouldBe(storedSecond.BlindIndex);
        storedFirst.Encrypted.ShouldNotBe(storedSecond.Encrypted);
        storedOther.BlindIndex.ShouldNotBe(storedFirst.BlindIndex);
    }

    [DockerFact]
    public async Task BlobCopiedToAnotherAccountOrTamperedWith_DoesNotDecrypt()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var protector = factory.Services.GetRequiredService<IHolderDocumentProtector>();
        var first = (await client.PostAccountAsync(WriteClient.AccountBody("123.456.789-09"))).Text("accountId");
        var second = (await client.PostAccountAsync(WriteClient.AccountBody("123.456.789-09"))).Text("accountId");
        var blob = (await _data.StoredAccountAsync(first)).Encrypted;

        protector.Unprotect(blob, WriteTestData.Account(first)).IsSuccess.ShouldBeTrue();
        protector.Unprotect(blob, WriteTestData.Account(second)).IsSuccess.ShouldBeFalse();

        for (var position = 0; position < blob.Length; position++)
        {
            var tampered = (byte[])blob.Clone();
            tampered[position] ^= 0x01;

            protector.Unprotect(tampered, WriteTestData.Account(first)).IsSuccess
                .ShouldBeFalse(string.Create(CultureInfo.InvariantCulture, $"byte {position}"));
        }
    }

    [DockerFact]
    public async Task ResponsesOfTheRestOfTheLedger_NeverCarryTheDocumentOrTheIndex()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var created = await client.PostAccountAsync(WriteClient.AccountBody("123.456.789-09"));
        var accountId = created.Text("accountId");
        var stored = await _data.StoredAccountAsync(accountId);

        var entry = await client.CreditAsync(accountId, "10.00");
        var reversal = await client.PostReversalAsync(accountId, entry.Text("entryId"), WriteClient.NewKey());

        foreach (var response in new[] { created, entry, reversal })
        {
            response.Body.ShouldNotContain("12345678909");
            response.Body.ShouldNotContain("123.456.789-09");
            response.Body.ShouldNotContain(Convert.ToHexStringLower(stored.BlindIndex));
            response.Body.ShouldNotContain(Convert.ToBase64String(stored.BlindIndex));
            response.Body.ShouldNotContain(Convert.ToBase64String(stored.Encrypted));
        }

        (await _data.OutboxPayloadsAsync(accountId)).ShouldAllBe(payload => !payload.Contains("12345678909", StringComparison.Ordinal));
        (await _data.AuditRowsAsync(accountId, "account.created")).ShouldAllBe(row => !row.Details.Contains("12345678909", StringComparison.Ordinal));
    }

    private static string Masked(string document)
    {
        var parsed = HolderDocument.From(document);

        return parsed.IsSuccess ? parsed.Value.Masked() : string.Empty;
    }
}
