using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LanguageSelector.Api;

/// <summary>
/// Serves the client-side script with the correct JavaScript MIME type. Serving
/// it through the plugin "configurationpage" endpoint returns it as text/html,
/// which modern browsers refuse to execute as a script, so the flags never
/// appeared. This controller returns it as application/javascript instead.
/// </summary>
[ApiController]
[Route("LanguageSelector")]
[AllowAnonymous]
public class ClientScriptController : ControllerBase
{
    private readonly Assembly _assembly;
    private readonly string _scriptPath;

    public ClientScriptController()
    {
        _assembly = Assembly.GetExecutingAssembly();
        _scriptPath = GetType().Namespace!.Replace(".Api", string.Empty, System.StringComparison.Ordinal)
                      + ".Web.language-selector.js";
    }

    /// <summary>
    /// Gets the embedded client script.
    /// </summary>
    [HttpGet("ClientScript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var stream = _assembly.GetManifestResourceStream(_scriptPath);

        if (stream != null)
        {
            return File(stream, "application/javascript");
        }

        return NotFound();
    }
}
