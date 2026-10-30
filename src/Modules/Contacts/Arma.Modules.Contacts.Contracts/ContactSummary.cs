namespace Arma.Modules.Contacts.Contracts;

/// <summary>Diğer modüllerin bir kişi hakkında görebileceği en küçük bilgi.</summary>
public sealed record ContactSummary(Guid Id, ContactType Type, string DisplayName, string? Email, string? Phone);
