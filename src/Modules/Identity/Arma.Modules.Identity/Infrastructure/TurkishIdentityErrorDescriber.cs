using Microsoft.AspNetCore.Identity;

namespace Arma.Modules.Identity.Infrastructure;

internal sealed class TurkishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "Beklenmeyen bir hata oluştu.");

    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), $"{email} adresiyle kayıtlı bir kullanıcı zaten var.");

    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), $"{userName} kullanıcı adı zaten kullanılıyor.");

    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), $"'{email}' geçerli bir e-posta adresi değil.");

    public override IdentityError DuplicateRoleName(string role) => Error(nameof(DuplicateRoleName), $"'{role}' adında bir rol zaten var.");

    public override IdentityError InvalidRoleName(string? role) => Error(nameof(InvalidRoleName), $"'{role}' geçerli bir rol adı değil.");

    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "Parola yanlış.");

    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"Parola en az {length} karakter olmalı.");

    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Parola en az bir rakam içermeli.");

    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Parola en az bir küçük harf içermeli.");

    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Parola en az bir büyük harf içermeli.");

    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "Parola en az bir sembol içermeli.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), $"Parola en az {uniqueChars} farklı karakter içermeli.");

    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), $"Kullanıcı zaten '{role}' rolünde.");

    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), $"Kullanıcı '{role}' rolünde değil.");

    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "Doğrulama kodu geçersiz.");

    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "Kayıt başka biri tarafından değiştirildi; sayfayı yenileyip tekrar deneyin.");

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
