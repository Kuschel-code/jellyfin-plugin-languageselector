using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LanguageSelector.Api;

/// <summary>
/// Serves the client-side script and flag images with correct MIME types and
/// without authentication. Serving these through the plugin
/// "configurationpage" endpoint returns text/html (and may require elevated
/// auth), which browsers refuse to execute as a script or render as an image,
/// so the flags never appeared.
/// </summary>
[ApiController]
[Route("LanguageSelector")]
[AllowAnonymous]
public class ClientScriptController : ControllerBase
{
    private static readonly HashSet<string> AllowedFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "de.svg",
        "us.svg",
        "jp.svg",
        "jp-de.svg",
        "jp-en.svg"
    };

    private readonly Assembly _assembly;
    private readonly string _baseNamespace;

    public ClientScriptController()
    {
        _assembly = Assembly.GetExecutingAssembly();
        _baseNamespace = GetType().Namespace!.Replace(".Api", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the embedded client script.
    /// </summary>
    [HttpGet("ClientScript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetClientScript()
    {
        var stream = _assembly.GetManifestResourceStream(_baseNamespace + ".Web.language-selector.js");

        if (stream != null)
        {
            return File(stream, "application/javascript");
        }

        return NotFound();
    }

    /// <summary>
    /// Gets an embedded flag image.
    /// </summary>
    [HttpGet("flags/{name}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetFlag([FromRoute] string name)
    {
        if (!AllowedFlags.Contains(name))
        {
            return NotFound();
        }

        var stream = _assembly.GetManifestResourceStream(_baseNamespace + ".Web.flags." + name);

        if (stream != null)
        {
            return File(stream, "image/svg+xml");
        }

        return NotFound();
    }
}
