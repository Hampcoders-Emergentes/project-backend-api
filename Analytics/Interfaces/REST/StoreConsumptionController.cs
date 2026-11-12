using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using System.Net.Mime;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Application.Time;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.Analytics.Interfaces.REST;

public sealed record ConsumptionBucketResource(DateTime StartUtc, double Kwh);
public sealed record EquipmentConsumptionResource(string EquipmentId, string EquipmentName, double Kwh, double PeakKw,
    int SampleCount, IReadOnlyList<ConsumptionBucketResource> Series);
public sealed record StoreConsumptionResource(string StoreId, DateTime FromUtc, DateTime ToUtc, string Granularity,
    double TotalKwh, IReadOnlyList<EquipmentConsumptionResource> Equipment, IReadOnlyList<ConsumptionBucketResource> Series);

[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}/analytics/consumption")]
public sealed class StoreConsumptionController(StoreConsumptionQueryService service) : ControllerBase
{
    /// <summary>Energy (kWh) per equipment and bucket over [from, to). Both bounds are ISO timestamps with explicit offset; max 31 days.</summary>
    [HttpGet, Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult<StoreConsumptionResource>> Get(string storeId, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string granularity = "hourly")
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok(Map(await service.Handle(Query(actor, storeId, from, to, granularity), HttpContext.RequestAborted))); }
        catch (StoreNotAccessibleException) { return NotFound(ErrorBodies.Coded("Resource not found.", ErrorCodes.StoreNotFound)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    /// <summary>Downloads the same data as a CSV file.</summary>
    [HttpGet("report")]
    public async Task<IActionResult> Report(string storeId, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string granularity = "hourly", [FromQuery] string format = "csv")
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        if (!Enum.TryParse<ExportFormat>(format, true, out var exportFormat) || !Enum.IsDefined(exportFormat))
            return BadRequest(new { message = "Unsupported format. Supported: csv." });
        try
        {
            var file = await service.Handle(new ExportStoreConsumptionReportQuery(Query(actor, storeId, from, to, granularity), exportFormat),
                HttpContext.RequestAborted);
            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (StoreNotAccessibleException) { return NotFound(ErrorBodies.Coded("Resource not found.", ErrorCodes.StoreNotFound)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    private static GetStoreConsumptionQuery Query(User actor, string storeId, string? from, string? to, string granularity)
    {
        if (!Enum.TryParse<Granularity>(granularity, true, out var g) || !Enum.IsDefined(g))
            throw new ArgumentException("granularity must be hourly or daily.");
        var interval = ExplicitUtcInterval.Parse(from, to);
        return new(actor.Id, storeId, interval.FromUtc, interval.ToUtc, g);
    }

    private static StoreConsumptionResource Map(StoreEnergyMetric m) => new(m.StoreId, m.FromUtc, m.ToUtc, m.Granularity.ToString().ToLowerInvariant(),
        m.TotalKwh, m.Equipment.Select(e => new EquipmentConsumptionResource(e.EquipmentId, e.EquipmentName, e.Kwh, e.PeakKw, e.SampleCount,
            e.Series.Select(b => new ConsumptionBucketResource(b.StartUtc, b.Kwh)).ToList())).ToList(),
        m.Series.Select(b => new ConsumptionBucketResource(b.StartUtc, b.Kwh)).ToList());
}
