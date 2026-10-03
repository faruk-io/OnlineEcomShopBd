using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TechBazar.Api.Extensions;
using TechBazar.Application.PcBuilder;

namespace TechBazar.Api.Controllers;

[AllowAnonymous]
public sealed class PcBuilderController(IPcBuilderService builder) : ApiControllerBase
{
    [HttpGet("slots")]
    public ActionResult<IReadOnlyList<BuilderSlotDto>> Slots() => Ok(builder.Slots());

    /// <summary>Prices the parts from the database and runs the compatibility rules (socket, RAM type, PSU headroom, fit…).</summary>
    [HttpPost("evaluate")]
    public async Task<ActionResult<BuildReportDto>> Evaluate(BuildRequest request, CancellationToken ct) => Ok(await builder.EvaluateAsync(request, ct));

    /// <summary>Saves a build and returns a short share code (the link is /builder?b=CODE).</summary>
    [HttpPost("builds")]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    [ProducesResponseType(typeof(SavedBuildDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Save(SaveBuildRequest request, CancellationToken ct)
    {
        var saved = await builder.SaveAsync(User.UserIdOrNull(), request, ct);
        return Created($"/api/v1/pc-builder/builds/{saved.Code}", saved);
    }

    [HttpGet("builds/{code}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SavedBuildDto>> Get(string code, CancellationToken ct) => Ok(await builder.GetAsync(code, ct));
}
