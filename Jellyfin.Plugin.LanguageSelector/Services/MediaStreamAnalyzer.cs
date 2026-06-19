using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.LanguageSelector.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.LanguageSelector.Services;

public class MediaStreamAnalyzer
{
    private readonly LanguageDetector _languageDetector;
    
    public MediaStreamAnalyzer(LanguageDetector languageDetector)
    {
        _languageDetector = languageDetector;
    }
    
    public List<MediaStreamInfo> ExtractAudioStreams(BaseItem item)
    {
        var streams = new List<MediaStreamInfo>();
        
        if (item.GetMediaStreams() == null)
        {
            return streams;
        }
        
        var audioStreams = item.GetMediaStreams()
            .Where(s => s.Type == MediaStreamType.Audio)
            .OrderBy(s => s.Index);
        
        foreach (var stream in audioStreams)
        {
            streams.Add(new MediaStreamInfo
            {
                Index = stream.Index,
                Type = "Audio",
                Language = stream.Language,
                Title = stream.Title,
                Codec = stream.Codec,
                IsDefault = stream.IsDefault
            });
        }
        
        return streams;
    }
    
    public List<MediaStreamInfo> ExtractSubtitleStreams(BaseItem item)
    {
        var streams = new List<MediaStreamInfo>();
        
        if (item.GetMediaStreams() == null)
        {
            return streams;
        }
        
        var subtitleStreams = item.GetMediaStreams()
            .Where(s => s.Type == MediaStreamType.Subtitle)
            .OrderBy(s => s.Index);
        
        foreach (var stream in subtitleStreams)
        {
            streams.Add(new MediaStreamInfo
            {
                Index = stream.Index,
                Type = "Subtitle",
                Language = stream.Language,
                Title = stream.Title,
                Codec = stream.Codec,
                IsDefault = stream.IsDefault,
                IsForced = stream.IsForced
            });
        }
        
        return streams;
    }
    
    public List<LanguageOption> GenerateLanguageOptions(BaseItem item)
    {
        var audioStreams = ExtractAudioStreams(item);
        var subtitleStreams = ExtractSubtitleStreams(item);
        return GenerateLanguageOptions(audioStreams, subtitleStreams);
    }

    /// <summary>
    /// Pure option-building logic, independent of Jellyfin entities so it can be
    /// unit-tested. Produces one option per audio stream plus per audio+subtitle
    /// combination, drops anything without a known flag asset, de-duplicates by
    /// id and orders by <paramref name="preferredLanguages"/> when given.
    /// </summary>
    public List<LanguageOption> GenerateLanguageOptions(
        IReadOnlyList<MediaStreamInfo> audioStreams,
        IReadOnlyList<MediaStreamInfo> subtitleStreams,
        IReadOnlyList<string>? preferredLanguages = null)
    {
        var options = new List<LanguageOption>();

        if (audioStreams.Count == 0)
        {
            return options;
        }

        foreach (var audioStream in audioStreams)
        {
            var audioLang = audioStream.Language;

            options.Add(new LanguageOption
            {
                Id = _languageDetector.GetOptionId(audioLang, null),
                DisplayName = _languageDetector.GetDisplayName(audioLang, null),
                FlagIcon = _languageDetector.GetFlagIcon(audioLang, null),
                AudioStreamIndex = audioStream.Index,
                SubtitleStreamIndex = null,
                AudioLanguage = audioLang ?? "unknown",
                SubtitleLanguage = null,
                IsDefault = audioStream.IsDefault && !subtitleStreams.Any(s => s.IsDefault)
            });

            foreach (var subtitleStream in subtitleStreams.Where(s => !s.IsForced))
            {
                var subLang = subtitleStream.Language;

                options.Add(new LanguageOption
                {
                    Id = _languageDetector.GetOptionId(audioLang, subLang),
                    DisplayName = _languageDetector.GetDisplayName(audioLang, subLang),
                    FlagIcon = _languageDetector.GetFlagIcon(audioLang, subLang),
                    AudioStreamIndex = audioStream.Index,
                    SubtitleStreamIndex = subtitleStream.Index,
                    AudioLanguage = audioLang ?? "unknown",
                    SubtitleLanguage = subLang,
                    IsDefault = audioStream.IsDefault && subtitleStream.IsDefault
                });
            }
        }

        var uniqueOptions = options
            .Where(o => _languageDetector.IsSupportedFlag(o.FlagIcon))
            .GroupBy(o => o.Id)
            .Select(g => g.First())
            .ToList();

        return OrderByPreference(uniqueOptions, preferredLanguages);
    }

    private List<LanguageOption> OrderByPreference(
        List<LanguageOption> options,
        IReadOnlyList<string>? preferredLanguages)
    {
        if (preferredLanguages == null || preferredLanguages.Count == 0)
        {
            return options;
        }

        // Normalize the configured codes (e.g. "ger" -> "de") and rank options
        // by their audio language; unknown languages sort to the end. The index
        // tiebreaker keeps the order stable within a language.
        var order = preferredLanguages
            .Select(code => _languageDetector.NormalizeLanguageCode(code))
            .ToList();

        return options
            .Select((option, index) => (option, index))
            .OrderBy(x =>
            {
                var rank = order.IndexOf(_languageDetector.NormalizeLanguageCode(x.option.AudioLanguage));
                return rank < 0 ? int.MaxValue : rank;
            })
            .ThenBy(x => x.index)
            .Select(x => x.option)
            .ToList();
    }

    public LanguageOptionsResponse GetLanguageOptionsForItem(
        BaseItem item,
        IReadOnlyList<string>? preferredLanguages = null)
    {
        var audioStreams = ExtractAudioStreams(item);
        var subtitleStreams = ExtractSubtitleStreams(item);
        var options = GenerateLanguageOptions(audioStreams, subtitleStreams, preferredLanguages);

        return new LanguageOptionsResponse
        {
            Options = options,
            ItemId = item.Id.ToString(),
            ItemName = item.Name ?? "Unknown"
        };
    }
}
