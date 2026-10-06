using System.Net.Sockets;
using System.Security.Authentication;
using RabbitMQ.Client.Exceptions;

namespace Ledger.Infrastructure.Messaging;

internal static class BrokerFailures
{
    public static bool IsAttemptFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception is OperationCanceledException
            ? !cancellationToken.IsCancellationRequested
            : IsUnavailable(exception);
    }

    public static bool IsUnavailable(Exception exception)
    {
        return exception is RabbitMQClientException
            or IOException
            or SocketException
            or TimeoutException
            or AuthenticationException;
    }
}
