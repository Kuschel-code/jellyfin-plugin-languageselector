using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.LanguageSelector.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageSelector;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger,
        IServerConfigurationManager configurationManager)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        if (Configuration.InjectClientScript)
        {
            TryInjectClientScript(applicationPaths, configurationManager, logger);
        }
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Language Selector";

    public override Guid Id => Guid.Parse("d4c4a3e2-9b7a-4f5c-8e1d-2a3b4c5d6e7f");

    public override string Description => "One-click language selection for anime and media playback";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;

        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = ns + ".Configuration.config.html"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/de.svg",
                EmbeddedResourcePath = ns + ".Web.flags.de.svg"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/us.svg",
                EmbeddedResourcePath = ns + ".Web.flags.us.svg"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/jp.svg",
                EmbeddedResourcePath = ns + ".Web.flags.jp.svg"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/jp-de.svg",
                EmbeddedResourcePath = ns + ".Web.flags.jp-de.svg"
            },
            new PluginPageInfo
            {
                Name = "LanguageSelector/flags/jp-en.svg",
                EmbeddedResourcePath = ns + ".Web.flags.jp-en.svg"
            }
        };
    }

    // Inject a <script> tag pointing at the ClientScript controller into the web
    // client's index.html. Mirrors the approach used by established plugins
    // (e.g. JellyScrub) and is idempotent / self-updating.
    private void TryInjectClientScript(
        IApplicationPaths applicationPaths,
        IServerConfigurationManager configurationManager,
        ILogger logger)
    {
        try
        {
            var webPath = applicationPaths.WebPath;
            if (string.IsNullOrWhiteSpace(webPath))
            {
                return;
            }

            var indexFile = Path.Combine(webPath, "index.html");
            if (!File.Exists(indexFile))
            {
                logger.LogWarning("Language Selector: index.html not found at {IndexFile}", indexFile);
                return;
            }

            var original = File.ReadAllText(indexFile);
            var basePath = GetBasePath(configurationManager, logger);

            var version = Version?.ToString() ?? "1.0.0.0";
            var scriptElement = string.Format(
                "<script plugin=\"LanguageSelector\" version=\"{0}\" src=\"{1}/LanguageSelector/ClientScript\" defer></script>",
                version,
                basePath);

            // Remove any previous Language Selector script (old version or old
            // injection style, attributes in any order) before inserting the
            // current one. Earlier plugin versions injected a tag with
            // id="languageselector-injected" and the attributes ordered
            // differently, which a plugin-first pattern would miss.
            var cleaned = Regex.Replace(original, "<script[^>]*plugin=\"LanguageSelector\"[^>]*></script>\\n?", string.Empty);
            cleaned = Regex.Replace(cleaned, "<script[^>]*id=\"languageselector-injected\"[^>]*></script>\\n?", string.Empty);

            var bodyClosing = cleaned.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyClosing == -1)
            {
                logger.LogWarning("Language Selector: no closing body tag in {IndexFile}", indexFile);
                return;
            }

            var updated = cleaned.Insert(bodyClosing, scriptElement);

            if (string.Equals(updated, original, StringComparison.Ordinal))
            {
                return;
            }

            File.WriteAllText(indexFile, updated);
            logger.LogInformation("Language Selector: injected client script into {IndexFile}", indexFile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Language Selector: failed to inject client script (web directory may be read-only)");
        }
    }

    private static string GetBasePath(IServerConfigurationManager configurationManager, ILogger logger)
    {
        try
        {
            var networkConfig = configurationManager.GetConfiguration("network");
            var baseUrl = networkConfig.GetType().GetProperty("BaseUrl")?.GetValue(networkConfig)?.ToString()?.Trim('/');
            if (!string.IsNullOrEmpty(baseUrl))
            {
                return "/" + baseUrl;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Language Selector: unable to read base path, using '/'");
        }

        return string.Empty;
    }
}
