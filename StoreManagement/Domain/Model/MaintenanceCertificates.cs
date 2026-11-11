namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

public static class CertificateUrgency
{
    public const string Valid = "VALID";
    public const string ExpiresWithin30Days = "EXPIRES_WITHIN_30_DAYS";
    public const string ExpiresWithin15Days = "EXPIRES_WITHIN_15_DAYS";
    public const string Expired = "EXPIRED";
}

/// <summary>
///     Signed record of maintenance performed on an equipment by a certified technician, valid until it expires.
///     Expiry notices are sent once at 30 days, once at 15 days and once on expiry.
/// </summary>
public sealed class EquipmentMaintenanceCertificate
{
    /// <summary>Days before expiry at which the store manager is warned, most distant first.</summary>
    public static readonly IReadOnlyList<int> NoticeThresholdsDays = [30, 15, 0];

    public string Id { get; private set; } = null!;
    public string StoreId { get; private set; } = null!;
    public string EquipmentId { get; private set; } = null!;
    public string TechnicianName { get; private set; } = null!;
    public string CertificationNumber { get; private set; } = null!;
    public string? WorkDescription { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public string? DocumentFileUrl { get; private set; }
    public string RegisteredByUserId { get; private set; } = null!;
    public DateTime RegisteredAtUtc { get; private set; }
    /// <summary>Smallest notice threshold (days) already sent; null when no notice was sent.</summary>
    public int? LastExpiryNoticeDays { get; private set; }
    private EquipmentMaintenanceCertificate() { }

    public static EquipmentMaintenanceCertificate Register(string storeId, string equipmentId, string technicianName, string certificationNumber,
        string? workDescription, DateTime issuedAtUtc, DateTime expiresAtUtc, string? documentFileUrl, string actor, DateTime nowUtc)
    {
        if (issuedAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Dates must be UTC.");
        if (issuedAtUtc > nowUtc.AddDays(1)) throw new ArgumentException("issuedAt cannot be in the future.");
        if (expiresAtUtc <= issuedAtUtc || expiresAtUtc > issuedAtUtc.AddYears(10)) throw new ArgumentException("expiresAt must be after issuedAt and within 10 years.");
        var number = Text(certificationNumber, 60, "certificationNumber");
        if (number.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '/' or '.' or '_' or ' ')))
            throw new ArgumentException("certificationNumber may contain letters, digits, spaces and - / . _ only.");
        string? url = null;
        if (!string.IsNullOrWhiteSpace(documentFileUrl))
        {
            url = documentFileUrl.Trim();
            if (url.Length > 500 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("documentFileUrl must be an absolute https URL (at most 500 characters).");
        }
        if (workDescription != null && workDescription.Trim().Length > 1000) throw new ArgumentException("workDescription must contain at most 1000 characters.");
        return new()
        {
            Id = "cert-" + Guid.NewGuid().ToString("N"), StoreId = storeId, EquipmentId = equipmentId,
            TechnicianName = Text(technicianName, 120, "technicianName"), CertificationNumber = number,
            WorkDescription = string.IsNullOrWhiteSpace(workDescription) ? null : workDescription.Trim(),
            IssuedAtUtc = issuedAtUtc, ExpiresAtUtc = expiresAtUtc, DocumentFileUrl = url, RegisteredByUserId = actor, RegisteredAtUtc = nowUtc
        };
    }

    public bool IsValidAt(DateTime nowUtc) => IssuedAtUtc <= nowUtc && nowUtc < ExpiresAtUtc;

    public string UrgencyAt(DateTime nowUtc) => (ExpiresAtUtc - nowUtc).TotalDays switch
    {
        <= 0 => CertificateUrgency.Expired,
        <= 15 => CertificateUrgency.ExpiresWithin15Days,
        <= 30 => CertificateUrgency.ExpiresWithin30Days,
        _ => CertificateUrgency.Valid
    };

    /// <summary>Notice threshold that applies now and has not been sent yet, or null.</summary>
    public int? DueNotice(DateTime nowUtc)
    {
        var daysLeft = (ExpiresAtUtc - nowUtc).TotalDays;
        int? due = null;
        foreach (var threshold in NoticeThresholdsDays)
            if (daysLeft <= threshold) due = threshold;
        return due != null && (LastExpiryNoticeDays == null || due < LastExpiryNoticeDays) ? due : null;
    }

    public void RecordNotice(int thresholdDays) => LastExpiryNoticeDays = thresholdDays;

    private static string Text(string value, int max, string field)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0 || text.Length > max) throw new ArgumentException($"{field} is required and must contain at most {max} characters.");
        return text;
    }
}
