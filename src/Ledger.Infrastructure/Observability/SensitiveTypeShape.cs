using System.Reflection;
using Ledger.Application.Security;

namespace Ledger.Infrastructure.Observability;

internal sealed class SensitiveTypeShape
{
    private SensitiveTypeShape(IReadOnlyList<SensitiveMember> members)
    {
        Members = members;
    }

    public IReadOnlyList<SensitiveMember> Members { get; }

    public static SensitiveTypeShape? Create(Type type)
    {
        if (!IsCandidate(type))
        {
            return null;
        }

        var constructorParameters = type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => parameter.Name is not null && parameter.IsDefined(typeof(SensitiveAttribute), true))
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var members = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select(property => new SensitiveMember(
                property,
                property.IsDefined(typeof(SensitiveAttribute), true)
                || constructorParameters.Contains(property.Name)
                || SensitiveMemberNames.IsSensitive(property.Name)))
            .ToList();

        return members.Any(member => member.IsSensitive) ? new SensitiveTypeShape(members) : null;
    }

    private static bool IsCandidate(Type type)
    {
        return !(type.IsPrimitive
                 || type.IsEnum
                 || type.IsPointer
                 || type.IsArray
                 || type == typeof(string)
                 || type == typeof(decimal)
                 || typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
                 || typeof(Delegate).IsAssignableFrom(type)
                 || typeof(Exception).IsAssignableFrom(type));
    }
}
