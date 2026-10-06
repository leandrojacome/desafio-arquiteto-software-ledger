using System.ComponentModel.DataAnnotations;
using Ledger.Application.Security;

namespace Ledger.Infrastructure.Messaging;

internal sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required] public string Host { get; init; } = string.Empty;

    [Range(1, 65_535)] public int Port { get; init; } = 5672;

    [Required] public string VirtualHost { get; init; } = "/";

    [Required] public string Username { get; init; } = string.Empty;

    [Required]
    [Sensitive]
    public string Password { get; init; } = string.Empty;

    public bool UseTls { get; init; }

    [Range(1, 60)] public int ConnectTimeoutSeconds { get; init; } = 2;

    [Required] public string Exchange { get; init; } = "ledger.events";

    [Range(1, 300)] public int ReconnectMinSeconds { get; init; } = 1;

    [Range(1, 300)] public int ReconnectMaxSeconds { get; init; } = 30;

    [Range(0, 50)] public int ReconnectJitterPercent { get; init; } = 20;

    public RetentionQueueOptions Retention { get; init; } = new();
}
