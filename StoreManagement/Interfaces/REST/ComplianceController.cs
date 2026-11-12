using Hampcoders.Electrolink.API.Shared.Interfaces.REST;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Shared.Application.Time;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Idempotency;
using Hampcoders.Electrolink.API.Shared.Interfaces.REST.Resources;
using Microsoft.AspNetCore.Mvc;
using HumanAuthorize = Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AuthorizeAttribute;

namespace Hampcoders.Electrolink.API.StoreManagement.Interfaces.REST;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChecklistItemInputResource([Required, StringLength(40)] string Code, [Required, StringLength(20)] string Result,
    [StringLength(500)] string? Notes);

/// <param name="ChecklistId">Draft to revise; omit to start a new checklist.</param>
/// <param name="Status">DRAFT (default) saves progress; SUBMITTED finalizes the inspection.</param>
/// <param name="WeekOf">Any date of the inspected week (defaults to today).</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveChecklistResource(
    [StringLength(42)] string? ChecklistId,
    [Required, StringLength(60)] string InspectorStaffId,
    [Required, StringLength(120)] string InspectorName,
    DateOnly? WeekOf,
    [Required, MaxLength(50)] IReadOnlyList<ChecklistItemInputResource> Items,
    [StringLength(2000)] string? Observations,
    [StringLength(20)] string Status = "DRAFT");

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterCertificateResource(
    [Required, StringLength(120)] string TechnicianName,
    [Required, StringLength(60)] string CertificationNumber,
    [StringLength(1000)] string? WorkDescription,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    [StringLength(500)] string? DocumentFileUrl);

/// <param name="ImageBase64">PNG image, raw base64 or a data:image/png;base64 URL (at most 256 KB).</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterSignatureResource(
    [Required, StringLength(120)] string SignerName,
    [Required, StringLength(120)] string SignerTitle,
    [Required, StringLength(360_000)] string ImageBase64);

/// <param name="From">Start of the period (ISO timestamp with offset); defaults to 90 days ago.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerateOhsReportResource(string? From, string? To);

/// <param name="DownloadUrl">Relative URL that streams the PDF; requires the same bearer token.</param>
public sealed record OhsReportResource(string ReportId, string StoreId, string FileName, string ContentType, long SizeBytes, DateTime FromUtc,
    DateTime ToUtc, DateTime GeneratedAtUtc, double ComplianceScore, bool IsSigned, string DownloadUrl);

/// <summary>
///     Regulatory compliance (occupational health and safety) of a store: weekly electrical checklists, maintenance
///     certificates, compliance score, the manager's digital signature and the official OHS dossier.
/// </summary>
[ApiController, HumanAuthorize]
[Route("api/v1/stores/{storeId}")]
[Produces("application/json")]
public sealed class ComplianceController(ComplianceRecordsService records, ComplianceAssessmentService assessment) : ControllerBase
{
    /// <summary>Inspection points of the weekly checklist and the accepted results.</summary>
    [HttpGet("compliance/checklists/template")]
    public ActionResult<ChecklistTemplateResource> Template(string storeId) => Ok(ComplianceRecordsService.Template());

    /// <summary>Saves a draft or submits the weekly inspection (a submitted checklist must answer every item).</summary>
    [HttpPost("compliance/checklists")]
    [IdempotentEndpoint]
    public Task<ActionResult<WeeklySafetyChecklistResource>> SaveChecklist(string storeId, SaveChecklistResource body) =>
        Execute(actor =>
        {
            var status = (body.Status ?? "DRAFT").Trim().ToUpperInvariant();
            if (status is not ("DRAFT" or "SUBMITTED")) throw new ArgumentException("status must be DRAFT or SUBMITTED.");
            return records.Handle(new SaveWeeklyChecklistCommand(actor.Id, storeId, body.ChecklistId, body.InspectorStaffId, body.InspectorName,
                body.WeekOf, body.Items.Select(i => new ChecklistItemInput(i.Code, i.Result, i.Notes)).ToList(), body.Observations,
                status == "SUBMITTED"), HttpContext.RequestAborted);
        });

    /// <summary>Past inspections, newest week first. Filters: status=DRAFT|SUBMITTED, fromWeek/toWeek (yyyy-MM-dd).</summary>
    [HttpGet("compliance/checklists")]
    public Task<ActionResult<ChecklistPageResource>> ListChecklists(string storeId, [FromQuery] string? status = null,
        [FromQuery] DateOnly? fromWeek = null, [FromQuery] DateOnly? toWeek = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Execute(actor => records.Handle(new ListWeeklyChecklistsQuery(actor.Id, storeId, status, fromWeek, toWeek, page, pageSize),
            HttpContext.RequestAborted));

    /// <summary>Registers the signed record of a maintenance performed by a certified technician.</summary>
    [HttpPost("equipment/{equipmentId}/certificates")]
    [IdempotentEndpoint]
    [ProducesResponseType(typeof(MaintenanceCertificateResource), StatusCodes.Status201Created)]
    public Task<ActionResult<MaintenanceCertificateResource>> RegisterCertificate(string storeId, string equipmentId, RegisterCertificateResource body) =>
        Execute(actor => records.Handle(new RegisterCertificateCommand(actor.Id, storeId, equipmentId, body.TechnicianName, body.CertificationNumber,
                body.WorkDescription, body.IssuedAt, body.ExpiresAt, body.DocumentFileUrl), HttpContext.RequestAborted),
            value => Created($"/api/v1/stores/{storeId}/equipment/{equipmentId}/certificates", value));

    [HttpGet("equipment/{equipmentId}/certificates")]
    public Task<ActionResult<PagedResponse<MaintenanceCertificateResource>>> ListCertificates(string storeId, string equipmentId,
        [FromQuery] PageRequest paging) =>
        Execute(async actor => PagedResponse.From(await records.Handle(new ListCertificatesQuery(actor.Id, storeId, equipmentId), HttpContext.RequestAborted),
            paging));

    /// <summary>
    ///     Certificates expiring within <paramref name="withinDays"/> days (or already expired) and equipment with no
    ///     certificate. Store managers are also warned automatically at 30 days, 15 days and on expiry.
    /// </summary>
    [HttpGet("compliance/expirations")]
    public Task<ActionResult<CertificateExpirationsResource>> Expirations(string storeId, [FromQuery] int withinDays = 30) =>
        Execute(actor => assessment.Handle(new GetCertificateExpirationsQuery(actor.Id, storeId, withinDays), HttpContext.RequestAborted));

    /// <summary>Facility health score (0-100) with its weighted components.</summary>
    [HttpGet("compliance/score")]
    public Task<ActionResult<ComplianceScoreResource>> Score(string storeId) =>
        Execute(actor => assessment.Handle(new GetComplianceScoreQuery(actor.Id, storeId), HttpContext.RequestAborted));

    /// <summary>Stores the store manager's handwritten signature (PNG) used in official documents.</summary>
    [HttpPost("compliance/signature")]
    [ProducesResponseType(typeof(ComplianceSignatureResource), StatusCodes.Status201Created)]
    public Task<ActionResult<ComplianceSignatureResource>> RegisterSignature(string storeId, RegisterSignatureResource body) =>
        Execute(actor => records.Handle(new RegisterSignatureCommand(actor.Id, storeId, body.SignerName, body.SignerTitle, body.ImageBase64),
            HttpContext.RequestAborted), value => Created($"/api/v1/stores/{storeId}/compliance/signature", value));

    /// <summary>Metadata of the signature in use.</summary>
    [HttpGet("compliance/signature")]
    public Task<ActionResult<ComplianceSignatureResource>> GetSignature(string storeId) =>
        Execute(actor => records.Handle(new GetSignatureQuery(actor.Id, storeId), HttpContext.RequestAborted));

    [HttpGet("compliance/signature/image")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "image/png")]
    public async Task<IActionResult> GetSignatureImage(string storeId)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            var (png, hash) = await records.SignatureImage(new GetSignatureQuery(actor.Id, storeId), HttpContext.RequestAborted);
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.ETag = $"\"{hash}\"";
            return File(png, "image/png");
        }
        catch (HierarchyNotFoundException notFound) { return NotFound(ErrorBodies.Coded("Resource not found.", notFound.ErrorCode)); }
    }

    /// <summary>Generates the official OHS dossier (PDF) for a period of up to 366 days (default: last 90 days).</summary>
    [HttpPost("compliance/reports/generate")]
    [ProducesResponseType(typeof(OhsReportResource), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OhsReportResource>> Generate(string storeId, GenerateOhsReportResource? body,
        [FromServices] Hampcoders.Electrolink.API.Shared.Infrastructure.Concurrency.ReportGenerationGuard guard)
    {
        if (HttpContext.Items["User"] is not User caller) return Unauthorized();
        using var lease = guard.TryBegin("ohs-report", caller.Id.Value, storeId);
        if (lease == null)
            return Conflict(ErrorBodies.Coded("A report for this store is already being generated. Retry when it finishes.",
                Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions.ErrorCodes.ReportInProgress));
        return await Execute(async actor =>
        {
            var now = DateTime.UtcNow;
            var interval = body?.From == null && body?.To == null
                ? new ExplicitUtcInterval(now.AddDays(-90), now)
                : ExplicitUtcInterval.Parse(body.From, body.To, 366);
            return Resource(storeId, await assessment.Handle(new GenerateOhsReportCommand(actor.Id, storeId, interval.FromUtc, interval.ToUtc),
                HttpContext.RequestAborted));
        }, value => Created(value.DownloadUrl, value));
    }

    /// <summary>Dossiers generated for the store, newest first.</summary>
    [HttpGet("compliance/reports")]
    public Task<ActionResult<PagedResponse<OhsReportResource>>> ListReports(string storeId, [FromQuery] PageRequest paging) =>
        Execute(async actor => PagedResponse.From((await assessment.Handle(new ListOhsReportsQuery(actor.Id, storeId), HttpContext.RequestAborted))
            .Select(r => Resource(storeId, r)), paging));

    /// <summary>Streams a generated dossier with Content-Disposition: attachment.</summary>
    [HttpGet("compliance/reports/{reportId}/download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    public async Task<IActionResult> Download(string storeId, string reportId)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try
        {
            var report = await assessment.Handle(new GetOhsReportFileQuery(actor.Id, storeId, reportId), HttpContext.RequestAborted);
            Response.Headers.CacheControl = "private, no-store";
            return File(report.Content, report.ContentType, report.FileName);
        }
        catch (HierarchyNotFoundException notFound) { return NotFound(ErrorBodies.Coded("Resource not found.", notFound.ErrorCode)); }
    }

    private static OhsReportResource Resource(string storeId, OhsReportMetadata m) => new(m.ReportId, m.StoreId, m.FileName, m.ContentType,
        m.SizeBytes, m.FromUtc, m.ToUtc, m.GeneratedAtUtc, m.ComplianceScore, m.IsSigned,
        $"/api/v1/stores/{storeId}/compliance/reports/{m.ReportId}/download");

    private async Task<ActionResult<T>> Execute<T>(Func<User, Task<T>> action, Func<T, ActionResult<T>>? created = null)
    {
        if (HttpContext.Items["User"] is not User actor) return Unauthorized();
        try { var value = await action(actor); return created != null ? created(value) : Ok(value); }
        catch (HierarchyNotFoundException notFound) { return NotFound(ErrorBodies.Coded("Resource not found.", notFound.ErrorCode)); }
        catch (ComplianceConflictException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
