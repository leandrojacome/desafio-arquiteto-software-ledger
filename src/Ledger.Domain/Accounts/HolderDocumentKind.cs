using System.Diagnostics.CodeAnalysis;

namespace Ledger.Domain.Accounts;

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset document kind pass for a valid one; the default must stay outside the defined values.")]
public enum HolderDocumentKind
{
    Cpf = 1,
    Cnpj = 2
}
