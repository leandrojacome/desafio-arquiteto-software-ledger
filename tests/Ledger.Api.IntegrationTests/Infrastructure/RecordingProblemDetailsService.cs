using Microsoft.AspNetCore.Http;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class RecordingProblemDetailsService : IProblemDetailsService
{
    private readonly List<ProblemDetailsContext> _written = [];

    public IReadOnlyList<ProblemDetailsContext> Written => _written;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        _written.Add(context);

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
    {
        _written.Add(context);

        return ValueTask.FromResult(true);
    }
}
