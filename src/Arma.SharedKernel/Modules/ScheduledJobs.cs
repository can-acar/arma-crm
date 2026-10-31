namespace Arma.SharedKernel.Modules;

/// <summary>
/// Belirli aralıklarla arka planda çalışan iş (yenileme hatırlatması, zamanlanmış otomasyon gibi). Modüller
/// scoped servis olarak kaydeder; her çalıştırmada yeni bir scope açılır. İş tekrar çalıştırılmaya dayanıklı
/// yazılmalıdır: uygulama yeniden başlayınca ilk çalıştırma hemen yapılır.
/// </summary>
public interface IScheduledJob
{
    string Name { get; }

    TimeSpan Interval { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
