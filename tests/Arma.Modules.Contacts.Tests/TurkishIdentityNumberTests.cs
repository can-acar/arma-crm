using Arma.Modules.Contacts.Domain;

namespace Arma.Modules.Contacts.Tests;

public class TurkishIdentityNumberTests
{
    [Theory]
    [InlineData("10000000146")]
    public void Valid_tckn_passes(string value) => TurkishIdentityNumber.IsValidTckn(value).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1000000014")]   // 10 hane
    [InlineData("01000000146")]  // 0 ile başlıyor
    [InlineData("10000000147")]  // 11. hane yanlış
    [InlineData("10000000156")]  // 10. hane yanlış
    [InlineData("1000000014a")]  // rakam olmayan karakter
    public void Invalid_tckn_fails(string? value) => TurkishIdentityNumber.IsValidTckn(value).ShouldBeFalse();

    [Theory]
    [InlineData("1234567890", true)]
    [InlineData("123456789", false)]
    [InlineData("12345678ab", false)]
    public void Vkn_format_is_checked(string value, bool expected) =>
        TurkishIdentityNumber.IsValidVknFormat(value).ShouldBe(expected);

    [Theory]
    [InlineData("10000000146", "100******46")]
    [InlineData("1234567890", "123*****90")]
    [InlineData("12345", "*****")]
    public void Mask_hides_middle_digits(string value, string expected) =>
        TurkishIdentityNumber.Mask(value).ShouldBe(expected);
}
