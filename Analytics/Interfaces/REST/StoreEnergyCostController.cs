using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Application.Time;
using Microsoft.AspNetCore.Mvc;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Concurrency;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.Analytics.Interfaces.REST;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerateEnergyReportResource([Required] string From, [Required] string To, [Required, StringLength(10)] string Format);

/// <param name="DownloadUrl">Relative URL that streams the file; requires the same bearer token.</param>
public sealed record EnergyReportResource(string ReportId, string StoreId, string Format, string FileName, string ContentType,
    long SizeBytes, DateTime FromUtc, DateTime ToUtc, DateTime GeneratedAtUtc, DateTime ExpiresAtUtc, string DownloadUrl);

/// <summary>Energy consumption with costs, end-of-month cost projection and downloadable efficiency reports.</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}")]
public sealed class StoreEnergyCostController(EnergyCostQueryService costs, EnergyReportCommandService reports) : ControllerBase
{
    /// <summary>kWh and estimated cost of one equipment over [from, to) (ISO timestamps with offset, max 31 days).</summary>
    [HttpGet("areas/{areaId}/equipment/{equipmentId}/consumption"), Produces(MediaTypeNames.Application.Json)]
    public Task<ActionResult<EquipmentPowerConsumptionResource>> GetEquipmentConsumption(string storeId, string areaId, string equipmentId,
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string granularity = "daily") =>
        Execute(actor =>
        {
            if (!Enum.TryParse<Granularity>(granularity, true, out var g) || !Enum.IsDefined(g))
                throw new ArgumentException("granularity must be hourly or daily.");
            var interval = ExplicitUtcInterval.Parse(from, to);
            return costs.Handle(new GetEquipmentPowerConsumptionQuery(actor.Id, storeId, areaId, equipmentId, interval.FromUtc,
                interval.ToUtc, g), HttpContext.RequestAborted);
        });

    /// <summary>Month-to-date cost and projected end-of-month electricity expenditure of the store.</summary>
    [HttpGet("analytics/cost-projection"), Produces(MediaTypeNames.Application.Json)]
    public Task<ActionResult<StoreCostForecastResource>> GetCostProjection(string storeId) =>
        Execute(actor => costs.Handle(new GetStoreCostForecastQuery(actor.Id, storeId), HttpContext.RequestAborted));

    /// <summary>Generates an energy efficiency report (csv or pdf) and returns its download metadata.</summary>
    [HttpPost("analytics/reports"), Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(EnergyReportResource), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EnergyReportResource>> Generate(string storeId, GenerateEnergyReportResource body,
        [FromServices] ReportGenerationGuard guard)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        using var lease = guard.TryBegin("energy-report", actor.Id.Value, storeId);
        if (lease == null)
            return Conflict(ErrorBodies.Coded("A report for this store is already being generated. Retry when it finishes.", ErrorCodes.ReportInProgress));
        try
        {
            var interval = ExplicitUtcInterval.Parse(body.From, body.To);
            var meta = await reports.Handle(new GenerateEnergyReportCommand(actor.Id, storeId, interval.FromUtc, interval.ToUtc, body.Format),
                HttpContext.RequestAborted);
            var resource = new EnergyReportResource(meta.ReportId, meta.StoreId, meta.Format, meta.FileName, meta.ContentType, meta.SizeBytes,
                meta.FromUtc, meta.ToUtc, meta.GeneratedAtUtc, meta.ExpiresAtUtc,
                Url.Action(nameof(Download), new { storeId, reportId = meta.ReportId }) ?? $"/api/v1/stores/{storeId}/analytics/reports/{meta.ReportId}/download");
            return CreatedAtAction(nameof(Download), new { storeId, reportId = meta.ReportId }, resource);
        }
        catch (StoreNotAccessibleException) { return NotFound(ErrorBodies.Coded("Resource not found.", ErrorCodes.StoreNotFound)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    /// <summary>Streams a generated report with Content-Disposition: attachment.</summary>
    [HttpGet("analytics/reports/{reportId}/download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf", "text/csv")]
    public async Task<IActionResult> Download(string storeId, string reportId)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            var report = await reports.Handle(new GetEnergyReportFileQuery(actor.Id, storeId, reportId), HttpContext.RequestAborted);
            Response.Headers.CacheControl = "private, no-store";
            return File(report.Content, report.ContentType, report.FileName);
        }
        catch (Exception e) when (e is StoreNotAccessibleException or EnergyReportNotFoundException)
        {
            return NotFound(new { message = "Resource not found." });
        }
    }

    private async Task<ActionResult<T>> Execute<T>(Func<User, Task<T>> action)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok(await action(actor)); }
        catch (StoreNotAccessibleException) { return NotFound(ErrorBodies.Coded("Resource not found.", ErrorCodes.StoreNotFound)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
