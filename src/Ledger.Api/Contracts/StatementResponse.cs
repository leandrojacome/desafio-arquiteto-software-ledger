using System.ComponentModel;
using Ledger.Application.Entries;

namespace Ledger.Api.Contracts;

[Description("Uma página do extrato de uma conta, com o lançamento mais recente primeiro.")]
internal sealed record StatementResponse(
    [property: Description("Lançamentos da página.")]
    IReadOnlyList<EntryResponse> Items,
    [property: Description("Cursor assinado da próxima página, ou null na última. É vinculado à conta.")]
    string? NextCursor,
    [property: Description("Tamanho de página aplicado à requisição.")]
    int Limit)
{
    public static StatementResponse From(StatementPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new StatementResponse([.. page.Items.Select(EntryResponse.From)], page.NextCursor, page.Limit);
    }
}
