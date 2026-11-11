using System.Globalization;
using System.Text;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.Reporting;

/// <summary>CSV adapter (UTF-8 with BOM for spreadsheet tools, invariant culture, formula-injection safe).</summary>
public sealed class CsvConsumptionReportExporter : IConsumptionReportExporter
{
    public ReportFile Export(StoreEnergyMetric metric, ExportFormat format)
    {
        if (format != ExportFormat.Csv) throw new ArgumentException("Unsupported export format.");
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("store_id,from_utc,to_utc,granularity,total_kwh\r\n");
        sb.Append(string.Join(',', Cell(metric.StoreId), metric.FromUtc.ToString("O", inv), metric.ToUtc.ToString("O", inv),
            metric.Granularity.ToString().ToLowerInvariant(), metric.TotalKwh.ToString("F4", inv))).Append("\r\n\r\n");
        sb.Append("equipment_id,equipment_name,bucket_start_utc,kwh\r\n");
        foreach (var equipment in metric.Equipment)
        {
            if (equipment.Series.Count == 0)
                sb.Append(string.Join(',', Cell(equipment.EquipmentId), Cell(equipment.EquipmentName), "", 0.ToString("F4", inv))).Append("\r\n");
            foreach (var bucket in equipment.Series)
                sb.Append(string.Join(',', Cell(equipment.EquipmentId), Cell(equipment.EquipmentName),
                    bucket.StartUtc.ToString("O", inv), bucket.Kwh.ToString("F4", inv))).Append("\r\n");
        }
        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return new($"consumption-{metric.StoreId}-{metric.FromUtc:yyyyMMdd}-{metric.ToUtc:yyyyMMdd}.csv", "text/csv; charset=utf-8", content);
    }

    private static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r') || value.StartsWith('\'')
            ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
