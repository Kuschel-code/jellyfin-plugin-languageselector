using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LanguageSelector.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool EnableDebugLogging { get; set; } = false;
    
    public bool AutoDetectLanguages { get; set; } = true;
    
    public string[] PreferredLanguages { get; set; } = new[] { "ger", "jpn", "eng" };
    
    public bool EnableAutoScan { get; set; } = true;
    
    public bool ScanOnStartup { get; set; } = true;
    
    public int ScanIntervalHours { get; set; } = 24;
    
    public string[] LibrariesToScan { get; set; } = System.Array.Empty<string>();
}
