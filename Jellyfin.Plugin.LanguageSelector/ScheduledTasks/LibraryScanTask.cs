using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LanguageSelector.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageSelector.ScheduledTasks;

public class LibraryScanTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryScanTask> _logger;

    public LibraryScanTask(ILibraryManager libraryManager, ILogger<LibraryScanTask> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => "Scan Libraries for Language Options";

    public string Key => "LanguageSelectorLibraryScan";

    public string Description => "Scans all libraries to detect available language options for media files";

    public string Category => "Language Selector";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance == null)
        {
            _logger.LogWarning("Plugin instance not available");
            return;
        }

        var config = Plugin.Instance.Configuration;
        
        if (!config.EnableAutoScan)
        {
            _logger.LogInformation("Auto scan is disabled");
            return;
        }

        _logger.LogInformation("Starting library scan for language options");

        try
        {
            var analyzer = new MediaStreamAnalyzer(new LanguageDetector());

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode, BaseItemKind.Movie },
                Recursive = true,
                IsVirtualItem = false
            };

            // Restrict to the libraries chosen in settings, if any were given.
            if (config.LibrariesToScan is { Length: > 0 } selectedLibraries)
            {
                var ancestorIds = _libraryManager.GetVirtualFolders()
                    .Where(vf => selectedLibraries.Contains(vf.Name, StringComparer.OrdinalIgnoreCase))
                    .Select(vf => Guid.TryParse(vf.ItemId, out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .ToArray();

                if (ancestorIds.Length > 0)
                {
                    query.AncestorIds = ancestorIds;
                    _logger.LogInformation("Restricting scan to {Count} selected librarie(s)", ancestorIds.Length);
                }
                else
                {
                    _logger.LogWarning("No configured library matched by name; scanning all libraries");
                }
            }

            var items = _libraryManager.GetItemList(query);
            var total = items.Count;

            if (total == 0)
            {
                _logger.LogInformation("No media items found to scan");
                progress?.Report(100);
                return;
            }

            _logger.LogInformation("Scanning {Total} media items for language options", total);

            var itemsWithOptions = 0;
            var processed = 0;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var optionCount = analyzer.GenerateLanguageOptions(item).Count;
                    if (optionCount > 0)
                    {
                        itemsWithOptions++;

                        if (config.EnableDebugLogging)
                        {
                            _logger.LogDebug(
                                "{ItemName}: {OptionCount} language option(s)",
                                item.Name,
                                optionCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to analyze item {ItemName}", item.Name);
                }

                processed++;
                progress?.Report((double)processed / total * 100);
            }

            _logger.LogInformation(
                "Library scan completed: {WithOptions} of {Total} items have selectable language options",
                itemsWithOptions,
                total);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scan cancelled by user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library scan");
            throw;
        }

        await Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Always provide a schedule so the task appears under Scheduled Tasks
        // with a recurring trigger. Enable/disable is enforced in ExecuteAsync,
        // and the interval can be tuned in the Scheduled Tasks UI.
        var intervalHours = Plugin.Instance?.Configuration.ScanIntervalHours ?? 24;
        if (intervalHours <= 0)
        {
            intervalHours = 24;
        }

        var triggers = new List<TaskTriggerInfo>
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(intervalHours).Ticks
            }
        };

        if (Plugin.Instance?.Configuration.ScanOnStartup ?? true)
        {
            triggers.Add(new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerStartup
            });
        }

        return triggers;
    }
}
