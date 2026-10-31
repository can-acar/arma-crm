using Arma.Infrastructure.Messaging;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;

namespace Arma.Infrastructure.Messaging;

public static class OutboxExtensions
{
    /// <summary>
    /// Değişiklikleri kaydeder ve yayımlanan iletileri gönderir. Blazor Server'da servisler ve outbox devre (sekme)
    /// boyunca yaşar; Wolverine varsayılan olarak ilk gönderimden sonrakileri sessizce düşürdüğü için her çağrıda
    /// yeniden göndermeye izin verilir.
    /// </summary>
    public static Task SaveChangesAndPublishAsync(this IDbContextOutbox outbox, CancellationToken cancellationToken) =>
        outbox.SaveChangesAndFlushMessagesAsync(MultiFlushMode.AllowMultiples, cancellationToken);
}
