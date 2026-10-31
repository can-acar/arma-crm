namespace Arma.SharedKernel.Portal;

/// <summary>
/// Bağışçı portalına e-posta bağlantısıyla giriş yapmış kişi. Portal sayfaları oturum gerektirmeyen (statik)
/// sayfalardır; kişiye ait veriyi göstermeden önce <see cref="ContactIdAsync"/> ile sahiplik denetlenir.
/// </summary>
public interface IPortalSession
{
    /// <summary>Portal çerezi geçerliyse kişinin kimliği (birleştirilmişse asıl kaydı), değilse null.</summary>
    Task<Guid?> ContactIdAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Modüllerin portal hesap sayfasına bölüm eklemesi için (bağışlarım, üyeliğim, iletişim tercihlerim gibi).
/// <see cref="ComponentType"/>, <c>ContactId</c> (Guid) parametresi alan statik bir Razor bileşenidir.
/// </summary>
public interface IPortalSection
{
    string Key { get; }

    string Title { get; }

    int Order { get; }

    Type ComponentType { get; }
}
