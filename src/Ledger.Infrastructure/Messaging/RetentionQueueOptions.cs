using System.ComponentModel.DataAnnotations;

namespace Ledger.Infrastructure.Messaging;

internal sealed class RetentionQueueOptions
{
    public const string SectionName = "RabbitMq:Retention";

    public const string DefaultQueue = "retention.ledger.entry-registered";

    public bool Enabled { get; init; } = true;

    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string Queue { get; init; } = DefaultQueue;

    [Range(1, 720)] public int TtlHours { get; init; } = 24;

    [Range(1_000, 100_000_000)] public int MaxLength { get; init; } = 1_000_000;

    [Range(1, 102_400)] public int MaxMegabytes { get; init; } = 1_024;

    [Range(1, 720)] public int DeadLetterTtlHours { get; init; } = 168;

    [Range(1_000, 10_000_000)] public int DeadLetterMaxLength { get; init; } = 100_000;

    public string DeadLetterExchange => Queue + ".dlx";

    public string DeadLetterQueue => Queue + ".dead-letter";
}
