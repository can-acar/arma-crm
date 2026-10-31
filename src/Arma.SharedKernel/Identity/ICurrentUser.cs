namespace Arma.SharedKernel.Identity;

/// <summary>
/// İşlemi yapan kullanıcı. Hem API isteklerinde hem Blazor devrelerinde (circuit) doğru kullanıcıyı verir;
/// kayıtların <c>created_by</c> / <c>updated_by</c> alanları buradan doldurulur.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Oturum açmış kullanıcının kimliği; oturum yoksa (ör. komut satırı) null.</summary>
    Guid? UserId { get; }
}
