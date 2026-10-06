using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class RejectedEntryTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task Debit_AboveTheBalance_IsRefusedWithoutTouchingAnything()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");

        var response = await client.DebitAsync(accountId, "100.01");

        var problem = response.ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
        problem.GetProperty("title").GetString().ShouldBe("Saldo insuficiente");
        problem.GetProperty("detail").GetString().ShouldBe("O saldo resultante ficaria abaixo do limite de cheque especial.");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(1);
        (await _data.CountKeysAsync(accountId)).ShouldBe(1);
        (await _data.BalanceAsync(accountId)).ShouldBe(100.00m);
    }

    [DockerFact]
    public async Task Refusal_NeverRevealsTheBalance()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("123.45");

        var response = await client.DebitAsync(accountId, "9999.99");

        response.ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
        response.Body.ShouldNotContain("123.45");
        response.Body.ShouldNotContain("123,45");
    }

    [DockerFact]
    public async Task Refusal_ForLackOfFunds_DoesNotConsumeTheKey()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("50.00");
        var key = WriteClient.NewKey();

        (await client.DebitAsync(accountId, "80.00", key)).ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
        (await client.CreditAsync(accountId, "100.00")).StatusCode.ShouldBe(201);

        var retried = await client.DebitAsync(accountId, "80.00", key);

        retried.StatusCode.ShouldBe(201, retried.Body);
        retried.HasHeader("Idempotent-Replayed").ShouldBeFalse();
        retried.Text("balanceAfter").ShouldBe("70.00");
        (await _data.BalanceAsync(accountId)).ShouldBe(70.00m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task Entry_InAnotherCurrency_IsRefusedAsAMismatchAndDoesNotConsumeTheKey()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var key = WriteClient.NewKey();

        var response = await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "10.00", "EUR"), key);

        var problem = response.ShouldBeProblem(422, "CURRENCY_MISMATCH");
        problem.GetProperty("title").GetString().ShouldBe("Moeda diferente da moeda da conta");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
        (await _data.CountKeysAsync(accountId)).ShouldBe(1);

        var accepted = await client.PostEntryAsync(accountId, WriteClient.EntryBody("CREDIT", "10.00"), key);

        accepted.StatusCode.ShouldBe(201, accepted.Body);
    }

    [DockerFact]
    public async Task Entry_OnAnAccountThatDoesNotExist_IsNotFoundAndLeavesNothing()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var missing = Guid.NewGuid().ToString("D");

        var response = await client.CreditAsync(missing, "10.00");

        var problem = response.ShouldBeProblem(404, "ACCOUNT_NOT_FOUND");
        problem.GetProperty("title").GetString().ShouldBe("Conta não encontrada");
        (await _data.CountKeysAsync(missing)).ShouldBe(0);
        (await _data.CountEntriesAsync(missing)).ShouldBe(0);
        (await _data.CountOutboxAsync(missing)).ShouldBe(0);
    }

    [DockerTheory]
    [InlineData("not-a-guid")]
    [InlineData("0192B7C281AA7E04B1D56F0C2A9E8D33")]
    [InlineData("{0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33}")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("+0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d3")]
    public async Task Entry_OnAnAccountIdOutsideTheHyphenatedFormat_IsTheSameNotFound(string accountId)
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());

        var response = await client.CreditAsync(Uri.EscapeDataString(accountId), "10.00");

        response.ShouldBeProblem(404, "ACCOUNT_NOT_FOUND");
    }

    [DockerFact]
    public async Task Entry_OnAnAccountIdInUppercase_IsAcceptedAsTheSameAccount()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();

        var response = await client.CreditAsync(accountId.ToUpperInvariant(), "10.00");

        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("accountId").ShouldBe(accountId);
    }
}
