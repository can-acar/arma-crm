namespace Arma.Modules.Comms.Contracts;

/// <summary>
/// Bilgilendirme amaçlı (ticari olmayan) e-posta: makbuz, teşekkür, şifre bildirimi gibi. İleti izni
/// gerekmez; kişinin kayıtlı e-posta adresine gönderilir ve zaman çizelgesine düşer.
/// </summary>
/// <param name="Body">Düz metin; satır sonları korunur.</param>
public sealed record SendTransactionalEmail(Guid ContactId, string Subject, string Body);

/// <summary>
/// Kişinin ticari ileti izni başka bir yerde (web formu gibi) alındı veya reddedildi.
/// </summary>
/// <param name="Channel">"email", "sms" veya "call".</param>
/// <param name="Source">İznin kaynağı; İYS'ye bildirilen kaynağı belirler ("form", "import", "manual").</param>
public sealed record RecordChannelConsent(Guid ContactId, string Channel, bool Approved, string Source);
