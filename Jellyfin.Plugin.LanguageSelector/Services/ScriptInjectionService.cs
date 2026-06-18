using System;
using System.IO;
using System.Text.RegularExpressions;
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

    // Unique id on the injected tag so we can find, refresh or de-duplicate it.
    private const string Marker = "languageselector-injected";

    // Matches a previously injected tag (any version) so it can be replaced.
    private static readonly Regex ExistingTag = new(
        "\\s*<script id=\"" + Marker + "\"[^>]*></script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        var closingBody = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (closingBody < 0)
        {
            _logger.LogWarning("No </body> tag found in index.html; skipping script injection");
            return;
        }

        // Strip any previous injection first so a version change refreshes the
        // tag (cache-busting) and accidental duplicates are collapsed.
        var stripped = ExistingTag.Replace(html, string.Empty);

        var version = Plugin.Instance?.Version?.ToString() ?? "0";
        var tag = $"<script id=\"{Marker}\" plugin=\"LanguageSelector\" defer " +
                  $"src=\"configurationpage?name={ScriptName}&v={version}\"></script>\n";

        var insertAt = stripped.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        var updated = stripped.Insert(insertAt, tag);

        if (string.Equals(updated, html, StringComparison.Ordinal))
        {
            _logger.LogDebug("Language Selector script already up to date (v{Version})", version);
            return;
        }

        try
        {
            File.WriteAllText(indexPath, updated);
            _logger.LogInformation("Injected Language Selector script (v{Version}) into web client index.html", version);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(
                ex,
                "Could not write {IndexPath}; the web client directory must be writable for the flag buttons to load",
                indexPath);
        }
    }
}
