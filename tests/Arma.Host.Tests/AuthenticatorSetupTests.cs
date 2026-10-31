using Arma.Modules.Identity.Application;

namespace Arma.Host.Tests;

public class AuthenticatorSetupTests
{
    [Fact]
    public void Uri_contains_issuer_account_and_secret() =>
        AuthenticatorSetup.BuildUri("ayşe@ornek.com.tr", "ABCDEFGH")
            .ShouldBe("otpauth://totp/Arma%20CRM:ay%C5%9Fe%40ornek.com.tr?secret=ABCDEFGH&issuer=Arma%20CRM&digits=6");

    [Fact]
    public void Key_is_grouped_by_four() =>
        AuthenticatorSetup.FormatKey("ABCDEFGHIJ").ShouldBe("abcd efgh ij");

    [Fact]
    public void Qr_code_is_svg() =>
        AuthenticatorSetup.RenderQrSvg("otpauth://totp/x?secret=ABC").ShouldStartWith("<svg");
}
