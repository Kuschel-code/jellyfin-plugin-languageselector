using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageSelector.Services;

/// <summary>
/// Injects the client-side language-selector script into the Jellyfin web
/// client's index.html on startup. Plugin page resources are not loaded by the
/// web client automatically, so without this the flag buttons would never
/// appear for end users.
/// </summary>
public class ScriptInjectionService : IHostedService
{
    // Served by Jellyfin from the plugin's embedded resources (see Plugin.GetPages).
    private const string ScriptName = "LanguageSelector/language-selector.js";

    // Unique marker so we can detect (and avoid duplicating) a previous injection.
    private const string Marker = "languageselector-injected";

    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<ScriptInjectionService> _logger;

    public ScriptInjectionService(IApplicationPaths appPaths, ILogger<ScriptInjectionService> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            InjectScript();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inject Language Selector script into the web client");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void InjectScript()
    {
        var webPath = _appPaths.WebPath;
        if (string.IsNullOrEmpty(webPath) || !Directory.Exists(webPath))
        {
            _logger.LogWarning("Web client path not found ({WebPath}); skipping script injection", webPath);
            return;
        }

        var indexPath = Path.Combine(webPath, "index.html");
        if (!File.Exists(indexPath))
        {
            _logger.LogWarning("index.html not found at {IndexPath}; skipping script injection", indexPath);
            return;
        }

        var html = File.ReadAllText(indexPath);

        if (html.Contains(Marker, StringComparison.Ordinal))
        {
            _logger.LogDebug("Language Selector script already injected");
            return;
        }

        var closingBody = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (closingBody < 0)
        {
            _logger.LogWarning("No </body> tag found in index.html; skipping script injection");
            return;
        }

        var version = Plugin.Instance?.Version?.ToString() ?? "0";
        var tag = $"<script id=\"{Marker}\" plugin=\"LanguageSelector\" defer " +
                  $"src=\"configurationpage?name={ScriptName}&v={version}\"></script>\n";

        var updated = html.Insert(closingBody, tag);

        try
        {
            File.WriteAllText(indexPath, updated);
            _logger.LogInformation("Injected Language Selector script into web client index.html");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                ex,
                "No write permission for {IndexPath}; the web client must be writable for the flag buttons to load",
                indexPath);
        }
    }
}
