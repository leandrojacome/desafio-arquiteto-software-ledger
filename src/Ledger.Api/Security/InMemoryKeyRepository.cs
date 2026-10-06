using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Ledger.Api.Security;

internal sealed class InMemoryKeyRepository : IXmlRepository
{
    private readonly ConcurrentQueue<XElement> _elements = new();

    public IReadOnlyCollection<XElement> GetAllElements() => [.. _elements];

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        _elements.Enqueue(element);
    }
}
