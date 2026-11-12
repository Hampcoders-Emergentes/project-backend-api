using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Application.Time;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;
namespace Hampcoders.Electrolink.API.Analytics.Interfaces.REST;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningDayInputResource([Required, StringLength(10)] string DayOfWeek, [Required, StringLength(5)] string OpenTime,
    [Required, StringLength(5)] string CloseTime);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetOpeningHoursResource([Required, MaxLength(7)] IReadOnlyList<OpeningDayInputResource> Days);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetEnergyBudgetResource([Range(2000, 2100)] int Year, [Range(1, 12)] int Month,
    [Range(0.001, 100_000_000)] double TargetKWh, [Range(1, 100)] double AlertThresholdPercentage = 80);

/// <summary>Energy planning of a store: period comparison, off-hours consumption, monthly budget and predictive maintenance.</summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class StoreEnergyPlanningController(EnergyInsightsQueryService insights, EnergyPlanningCommandService planning) : ControllerBase
{
    /// <summary>
    ///     kWh and cost of period 2 compared with period 1 (baseline). Each period is [from, to) with explicit offsets and
    ///     at most 31 days; percentages are (period2 - period1) / period1.
    /// </summary>
    [HttpGet("analytics/comparison")]
    public Task<ActionResult<ConsumptionComparisonResource>> Compare(string storeId, [FromQuery] string? period1From, [FromQuery] string? period1To,
        [FromQuery] string? period2From, [FromQuery] string? period2To) =>
        Execute(actor =>
        {
            var p1 = ExplicitUtcInterval.Parse(period1From, period1To);
            var p2 = ExplicitUtcInterval.Parse(period2From, period2To);
            return insights.Handle(new CompareConsumptionPeriodsQuery(actor.Id, storeId, p1.FromUtc, p1.ToUtc, p2.FromUtc, p2.ToUtc), HttpContext.RequestAborted);
        });

    /// <summary>
    ///     Consumption above <paramref name="standbyThresholdKw"/> while the store is closed (per its opening hours), grouped
    ///     into episodes. Defaults to the last 7 days; from/to take explicit offsets (max 31 days).
    /// </summary>
    [HttpGet("analytics/off-hours-consumption")]
    public Task<ActionResult<OffHoursConsumptionResource>> OffHours(string storeId, [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] double standbyThresholdKw = 0.1) =>
        Execute(actor =>
        {
            var now = DateTime.UtcNow;
            var interval = from == null && to == null ? new ExplicitUtcInterval(now.AddDays(-7), now) : ExplicitUtcInterval.Parse(from, to);
            return insights.Handle(new GetOffHoursConsumptionQuery(actor.Id, storeId, interval.FromUtc, interval.ToUtc, standbyThresholdKw),
                HttpContext.RequestAborted);
        });

    [HttpGet("analytics/opening-hours")]
    public Task<ActionResult<StoreOpeningHoursResource>> GetOpeningHours(string storeId) =>
        Execute(actor => planning.Handle(new GetStoreOpeningHoursQuery(actor.Id, storeId), HttpContext.RequestAborted));

    /// <summary>Replaces the weekly opening hours (store local time); days not listed are closed.</summary>
    [HttpPut("analytics/opening-hours")]
    public Task<ActionResult<StoreOpeningHoursResource>> PutOpeningHours(string storeId, SetOpeningHoursResource body) =>
        Execute(actor => planning.Handle(new SetStoreOpeningHoursCommand(actor.Id, storeId,
            body.Days.Select(d => new OpeningDayResource(d.DayOfWeek, d.OpenTime, d.CloseTime)).ToList()), HttpContext.RequestAborted));

    /// <summary>Sets the monthly consumption target and the percentage from which the budget is flagged.</summary>
    [HttpPut("analytics/budget")]
    public Task<ActionResult<StoreEnergyBudgetResource>> PutBudget(string storeId, SetEnergyBudgetResource body) =>
        Execute(actor => planning.Handle(new SetStoreEnergyBudgetCommand(actor.Id, storeId, body.Year, body.Month, body.TargetKWh,
            body.AlertThresholdPercentage), HttpContext.RequestAborted));

    /// <summary>Month-to-date consumption against the budget, with a linear end-of-month projection.</summary>
    [HttpGet("analytics/budget-status")]
    public Task<ActionResult<BudgetStatusResource>> BudgetStatus(string storeId, [FromQuery] int? year = null, [FromQuery] int? month = null) =>
        Execute(actor => insights.Handle(new GetBudgetStatusQuery(actor.Id, storeId, year, month), HttpContext.RequestAborted));

    /// <summary>
    ///     Wear patterns per equipment over the last <paramref name="days"/> days (power factor degradation, current
    ///     fluctuation, recurrent overload, supply voltage instability) with an off-peak, closed-store repair window.
    /// </summary>
    [HttpGet("maintenance/predictions")]
    public Task<ActionResult<MaintenancePredictionsResource>> Predictions(string storeId, [FromQuery] int days = 14) =>
        Execute(actor => insights.Handle(new GetMaintenancePredictionsQuery(actor.Id, storeId, days), HttpContext.RequestAborted));

    private async Task<ActionResult<T>> Execute<T>(Func<User, Task<T>> action)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { return Ok(await action(actor)); }
        catch (StoreNotAccessibleException) { return NotFound(ErrorBodies.Coded("Resource not found.", ErrorCodes.StoreNotFound)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
