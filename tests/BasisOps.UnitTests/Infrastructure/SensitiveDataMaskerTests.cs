using BasisOps.Infrastructure.Ai;

namespace BasisOps.UnitTests.Infrastructure;

public class SensitiveDataMaskerTests
{
    [Fact]
    public void Mask_KnownUserThatIsAlsoAnEnglishWord_OnlyReplacesWholeWord()
    {
        var masker = new SensitiveDataMasker().AddUser("CAN");

        var masked = masker.Mask("Run 12345678 of Z_BILLING was canceled. User CAN reported it.");

        Assert.Equal("Run 12345678 of Z_BILLING was canceled. User USER_1 reported it.", masked);
    }

    [Fact]
    public void Mask_SameValueTwice_GetsSamePlaceholder()
    {
        var masker = new SensitiveDataMasker().AddUser("MEHMET").AddHost("prdapp01");

        var first = masker.Mask("User MEHMET on prdapp01");
        var second = masker.Mask("MEHMET again, application server prdapp02");

        Assert.Equal("User USER_1 on HOST_1", first);
        Assert.Equal("USER_1 again, application server HOST_2", second);
    }

    [Theory]
    [InlineData("connect to 10.10.1.11 failed", "connect to IP_1 failed")]
    [InlineData("peer fe80::1ff:fe23:4567:890a closed", "peer IP_1 closed")]
    [InlineData("CN=prdapp01.example.internal, O=Example", "CN=HOST_1, O=Example")]
    [InlineData("mail to basis.team@example.com", "mail to EMAIL_1")]
    [InlineData("(owner BATCH_ADM).", "(owner USER_1).")]
    [InlineData("created by: KTANAKA", "created by: USER_1")]
    public void Mask_FreeText_ReplacesSensitiveValues(string text, string expected)
    {
        Assert.Equal(expected, new SensitiveDataMasker().Mask(text));
    }

    [Theory]
    [InlineData("Started at 08:15:00, kernel 7.93, see SAP Note 1234567.")]
    [InlineData("The user who started the job is not known; the server is down.")]
    [InlineData("Field symbol <FS_EMPLOYEE> was not assigned (rdisp/max_wprun_time).")]
    [InlineData("Unable to interpret \"12,5O\" as a number.")]
    public void Mask_TechnicalText_IsUnchanged(string text)
    {
        Assert.Equal(text, new SensitiveDataMasker().Mask(text));
    }

    [Fact]
    public void Mask_TextAlreadyContainingPlaceholder_DoesNotMaskItAgain()
    {
        var masker = new SensitiveDataMasker().AddUser("MEHMET");

        Assert.Equal("User USER_1 and user USER_1", masker.Mask("User MEHMET and user MEHMET"));
    }
}
