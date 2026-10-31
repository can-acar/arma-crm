namespace Arma.Modules.Contacts.Contracts;

/// <summary>Fatura için gereken kişi bilgileri: kimlik numarası (kişide TCKN, kurumda VKN) ve fatura adresi.</summary>
public sealed record ContactBillingInfo(
    Guid Id, ContactType Type, string DisplayName, string? FirstName, string? LastName, string? NationalId, string? Email, string? Phone,
    string? Street, string? District, string? City, string? PostalCode);

/// <summary>Fatura kesen modüllerin kişi bilgisini okuduğu arayüz. Adres önceliği: fatura adresi, birincil adres, ilk dolu adres.</summary>
public interface IContactBillingDirectory
{
    /// <summary>Birleştirilmiş kayıtta asıl kaydın bilgisi döner; kişi silinmişse null.</summary>
    Task<ContactBillingInfo?> FindBillingAsync(Guid id, CancellationToken cancellationToken = default);
}
