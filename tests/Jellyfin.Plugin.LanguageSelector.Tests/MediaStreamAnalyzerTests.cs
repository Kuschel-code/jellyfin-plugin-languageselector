using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LanguageSelector.Models;
using Jellyfin.Plugin.LanguageSelector.Services;
using Xunit;

namespace Jellyfin.Plugin.LanguageSelector.Tests;

public class MediaStreamAnalyzerTests
{
    private readonly MediaStreamAnalyzer _analyzer = new(new LanguageDetector());

    private static MediaStreamInfo Audio(int index, string? language) =>
        new() { Index = index, Type = "Audio", Language = language };

    private static MediaStreamInfo Subtitle(int index, string? language, bool forced = false) =>
        new() { Index = index, Type = "Subtitle", Language = language, IsForced = forced };

    [Fact]
    public void GenerateLanguageOptions_GermanAudioOnly_ReturnsSingleFlag()
    {
        var options = _analyzer.GenerateLanguageOptions(
            new[] { Audio(1, "ger") },
            new List<MediaStreamInfo>());

        var option = Assert.Single(options);
        Assert.Equal("de", option.FlagIcon);
        Assert.Equal(1, option.AudioStreamIndex);
        Assert.Null(option.SubtitleStreamIndex);
    }

    [Fact]
    public void GenerateLanguageOptions_JapaneseAudioWithSubs_ReturnsCombinations()
    {
        var options = _analyzer.GenerateLanguageOptions(
            new[] { Audio(1, "jpn") },
            new[] { Subtitle(2, "ger"), Subtitle(3, "eng") });

        var flags = options.Select(o => o.FlagIcon).ToList();
        Assert.Contains("jp", flags);
        Assert.Contains("jp-de", flags);
        Assert.Contains("jp-us", flags);
    }

    [Fact]
    public void GenerateLanguageOptions_DropsUnknownLanguages()
    {
        var options = _analyzer.GenerateLanguageOptions(
            new[] { Audio(1, "fre") },
            new List<MediaStreamInfo>());

        Assert.Empty(options);
    }

    [Fact]
    public void GenerateLanguageOptions_SkipsForcedSubtitles()
    {
        var options = _analyzer.GenerateLanguageOptions(
            new[] { Audio(1, "jpn") },
            new[] { Subtitle(2, "ger", forced: true) });

        Assert.DoesNotContain(options, o => o.FlagIcon == "jp-de");
        Assert.Contains(options, o => o.FlagIcon == "jp");
    }

    [Fact]
    public void GenerateLanguageOptions_NoAudio_ReturnsEmpty()
    {
        var options = _analyzer.GenerateLanguageOptions(
            new List<MediaStreamInfo>(),
            new[] { Subtitle(1, "ger") });

        Assert.Empty(options);
    }

    [Fact]
    public void GenerateLanguageOptions_OrdersByPreferredLanguages()
    {
        var options = _analyzer.GenerateLanguageOptions(
            new[] { Audio(1, "jpn"), Audio(2, "ger") },
            new List<MediaStreamInfo>(),
            new[] { "eng", "ger", "jpn" });

        // "ger" (de) is preferred before "jpn" (jp).
        Assert.Equal("de", options.First().FlagIcon);
        Assert.Equal("jp", options.Last().FlagIcon);
    }

    [Fact]
    public void GenerateLanguageOptions_DeduplicatesByOptionId()
    {
        var options = _analyzer.GenerateLanguageOptions(
            new[] { Audio(1, "ger"), Audio(2, "deu") },
            new List<MediaStreamInfo>());

        Assert.Single(options);
    }
}
