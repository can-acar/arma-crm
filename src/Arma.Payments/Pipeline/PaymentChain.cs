using Arma.SharedKernel.Payments;

namespace Arma.Payments.Pipeline;

/// <summary>İşleyicileri sırayla birbirine bağlar; son halka <paramref name="terminal"/>, yani sağlayıcının kendisidir.</summary>
internal static class PaymentChain
{
    public static Task<TResult> RunAsync<TContext, TResult>(IEnumerable<IPaymentHandler<TContext, TResult>> handlers, TContext context,
        Func<TContext, CancellationToken, Task<TResult>> terminal, CancellationToken cancellationToken)
        where TContext : PaymentContext
    {
        var ordered = handlers.OrderBy(h => h.Order).ToArray();
        Func<Task<TResult>> next = () => terminal(context, cancellationToken);
        for (var i = ordered.Length - 1; i >= 0; i--)
        {
            var handler = ordered[i];
            var following = next;
            next = () => handler.HandleAsync(context, following, cancellationToken);
        }

        return next();
    }
}
