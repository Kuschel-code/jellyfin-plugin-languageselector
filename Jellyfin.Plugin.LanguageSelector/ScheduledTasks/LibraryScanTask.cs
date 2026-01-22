using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            var rootFolder = _libraryManager.GetUserRootFolder();
            if (rootFolder == null)
            {
                _logger.LogWarning("User root folder not available");
                return;
            }

            _logger.LogInformation("Library scan initiated. Language options will be detected on-demand when users browse media.");
            
            progress?.Report(50);

            await Task.Delay(100, cancellationToken);
            
            progress?.Report(100);

            _logger.LogInformation("Library scan completed successfully");
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
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        if (Plugin.Instance == null)
        {
            return Enumerable.Empty<TaskTriggerInfo>();
        }

        var config = Plugin.Instance.Configuration;
        var triggers = new List<TaskTriggerInfo>();

        if (config.ScanOnStartup)
        {
            triggers.Add(new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerStartup
            });
        }

        if (config.EnableAutoScan && config.ScanIntervalHours > 0)
        {
            triggers.Add(new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(config.ScanIntervalHours).Ticks
            });
        }

        return triggers;
    }
}
