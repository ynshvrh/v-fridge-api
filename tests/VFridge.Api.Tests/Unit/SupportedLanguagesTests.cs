using VFridge.Api.Contracts;
using VFridge.Api.Services;

namespace VFridge.Api.Tests.Unit;

public class SupportedLanguagesTests
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("EN", true)]
    [InlineData("uk", true)]
    [InlineData("ru", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_Recognises_Allowed_Codes(string? input, bool expected)
    {
        SupportedLanguages.IsSupported(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("uk-UA,uk;q=0.9,en;q=0.8", "uk")]
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("fr,de;q=0.5", null)]
    [InlineData("uk", "uk")]
    [InlineData("uk-UA", "uk")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void MatchAcceptLanguage_Picks_First_Supported_Base_Code(string? header, string? expected)
    {
        SupportedLanguages.MatchAcceptLanguage(header).Should().Be(expected);
    }

    [Fact]
    public void Normalize_FallsBack_When_Unsupported()
    {
        SupportedLanguages.Normalize("xx").Should().Be("en");
        SupportedLanguages.Normalize(null).Should().Be("en");
        SupportedLanguages.Normalize("UK").Should().Be("uk");
    }

    [Fact]
    public void CultureContext_Is_Empty_For_Any_And_Populated_For_Concrete_Cuisines()
    {
        AiPrompts.CultureContextFor("any").Should().BeNull();
        AiPrompts.CultureContextFor("unknown").Should().BeNull();

        AiPrompts.CultureContextFor("ukrainian")
            .Should().NotBeNullOrWhiteSpace()
            .And.Subject.ToString().Should().Contain("Ukrainian").And.Contain("borscht");
        AiPrompts.CultureContextFor("georgian")
            .Should().NotBeNullOrWhiteSpace()
            .And.Subject.ToString().Should().Contain("Georgian").And.Contain("khachapuri");
        AiPrompts.CultureContextFor("japanese")
            .Should().NotBeNullOrWhiteSpace()
            .And.Subject.ToString().Should().Contain("Japanese").And.Contain("ramen");
    }

    [Fact]
    public void LanguageInstruction_Is_Null_For_English_And_Populated_For_Ukrainian()
    {
        // English is the model's default — no instruction wastes prompt budget.
        AiPrompts.LanguageInstructionFor("en").Should().BeNull();
        AiPrompts.LanguageInstructionFor("unknown").Should().BeNull();

        AiPrompts.LanguageInstructionFor("uk")
            .Should().NotBeNullOrWhiteSpace()
            .And.Subject.ToString().Should().Contain("Ukrainian");
    }
}
