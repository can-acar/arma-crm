namespace Arma.SharedKernel.Security;

public static class ArmaClaimTypes
{
    /// <summary>Kullanıcının veya API istemcisinin sahip olduğu her izin için bir talep.</summary>
    public const string Permission = "arma:permission";

    /// <summary>Kullanıcı 2FA'yı açmak zorunda ama henüz açmamışsa "true".</summary>
    public const string TwoFactorSetupRequired = "arma:2fa_setup_required";
}
