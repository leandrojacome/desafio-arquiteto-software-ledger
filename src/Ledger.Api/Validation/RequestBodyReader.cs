using System.Buffers;

namespace Ledger.Api.Validation;

internal static class RequestBodyReader
{
    private const int ChunkSize = 4096;

    public static async Task<RequestBody> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        const int limit = ApiConstants.MaxRequestBodyBytes;

        if (request.ContentLength is > limit)
        {
            return RequestBody.TooLarge;
        }

        using var collected = new MemoryStream((int)Math.Min(request.ContentLength ?? 0, limit));
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);

        try
        {
            int read;

            while ((read = await request.Body.ReadAsync(chunk.AsMemory(0, ChunkSize), cancellationToken)) > 0)
            {
                if (collected.Length + read > limit)
                {
                    return RequestBody.TooLarge;
                }

                await collected.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return new RequestBody(collected.ToArray(), false);
    }
}
