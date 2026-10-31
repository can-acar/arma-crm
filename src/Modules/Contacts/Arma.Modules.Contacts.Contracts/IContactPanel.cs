namespace Arma.Modules.Contacts.Contracts;

/// <summary>
/// Diğer modüllerin kişi kartına sekme eklemesi için (bağışlar, üyelikler, aktiviteler gibi).
/// <see cref="ComponentType"/>, <c>ContactId</c> (Guid) parametresi alan bir Razor bileşenidir.
/// </summary>
public interface IContactPanel
{
    string Key { get; }

    string Title { get; }

    int Order { get; }

    Type ComponentType { get; }

    /// <summary>Sekmeyi görmek için gereken izin; null ise kişiyi görebilen herkes görür.</summary>
    string? Permission => null;
}
