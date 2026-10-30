namespace Arma.Modules.Comms.Contracts;

/// <summary>
/// Bilgilendirme amaçlı (ticari olmayan) e-posta: makbuz, teşekkür, şifre bildirimi gibi. İleti izni
/// gerekmez; kişinin kayıtlı e-posta adresine gönderilir ve zaman çizelgesine düşer.
/// </summary>
/// <param name="Body">Düz metin; satır sonları korunur.</param>
public sealed record SendTransactionalEmail(Guid ContactId, string Subject, string Body);
