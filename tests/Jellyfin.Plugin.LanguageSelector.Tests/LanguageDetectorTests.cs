using Jellyfin.Plugin.LanguageSelector.Services;
using Xunit;

namespace Jellyfin.Plugin.LanguageSelector.Tests;

public class LanguageDetectorTests
{
    private readonly LanguageDetector _detector = new();

    [Theory]
    [InlineData("ger", "de")]
    [InlineData("deu", "de")]
    [InlineData("DE", "de")]
    [InlineData("jpn", "jp")]
    [InlineData("ja", "jp")]
    [InlineData("eng", "us")]
    [InlineData("en", "us")]
    public void NormalizeLanguageCode_MapsKnownCodes(string input, string expected)
    {
        Assert.Equal(expected, _detector.NormalizeLanguageCode(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeLanguageCode_ReturnsUnknownForEmpty(string? input)
    {
        Assert.Equal("unknown", _detector.NormalizeLanguageCode(input));
    }

    [Fact]
    public void GetFlagIcon_AudioOnly_ReturnsAudioFlag()
    {
        Assert.Equal("de", _detector.GetFlagIcon("ger", null));
        Assert.Equal("jp", _detector.GetFlagIcon("jpn", null));
    }

    [Theory]
    [InlineData("jpn", "ger", "jp-de")]
    [InlineData("jpn", "eng", "jp-us")]
    public void GetFlagIcon_KnownCombinations(string audio, string sub, string expected)
    {
        Assert.Equal(expected, _detector.GetFlagIcon(audio, sub));
    }

    [Theory]
    [InlineData("de", true)]
    [InlineData("jp", true)]
    [InlineData("us", true)]
    [InlineData("jp-de", true)]
    [InlineData("jp-us", true)]
    [InlineData("de-us", false)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedFlag_OnlyAcceptsShippedFlags(string? flag, bool expected)
    {
        Assert.Equal(expected, _detector.IsSupportedFlag(flag));
    }
}
