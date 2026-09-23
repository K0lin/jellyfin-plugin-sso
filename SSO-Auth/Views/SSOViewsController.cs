using System.Linq;
using MediaBrowser.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SSO_Auth.Views;

/// <summary>
/// The sso views controller.
/// </summary>
[ApiController]
[Route("[controller]")]
public class SSOViewsController : ControllerBase
{
    private readonly ILogger<SSOViewsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SSOViewsController"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{SSOViewsController}"/> interface.</param>
    public SSOViewsController(ILogger<SSOViewsController> logger)
    {
        _logger = logger;
        _logger.LogInformation("SSO Views Controller initialized");
    }

    private ActionResult ServeView(string viewName)
    {
        var plugin = SSOPlugin.Instance;
        if (plugin is null)
        {
            return BadRequest("No plugin instance found");
        }

        var view = plugin.GetViews().FirstOrDefault(pageInfo => pageInfo.Name == viewName);

        if (view is null)
        {
            return NotFound("No matching view found");
        }

        var stream = plugin.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);

        if (stream is null)
        {
            _logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
            return NotFound();
        }

        return File(stream, MimeTypes.GetMimeType(view.EmbeddedResourcePath));
    }

    /// <summary>
    /// Gets a html view.
    /// </summary>
    /// <param name="viewName">The name of the view / asset to fetch.</param>
    /// <returns>The html view with the specified name.</returns>
    [HttpGet("{**viewName}")]
    public ActionResult GetView([FromRoute] string viewName)
    {
        return ServeView(viewName);
    }
}
