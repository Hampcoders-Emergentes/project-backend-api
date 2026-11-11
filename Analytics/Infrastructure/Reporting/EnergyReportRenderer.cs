using System.Globalization;
using System.Text;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.Reporting;

/// <summary>Renders energy efficiency reports as CSV (sectioned, spreadsheet friendly) or PDF (printable summary).</summary>
public sealed class EnergyReportRenderer : IEnergyReportRenderer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public ReportFile Render(EnergyReportData data, string format)
    {
        var name = $"energy-report-{data.StoreId}-{data.FromUtc:yyyyMMdd}-{data.ToUtc:yyyyMMdd}";
        return format switch
        {
            "csv" => new(name + ".csv", "text/csv; charset=utf-8", Csv(data)),
            "pdf" => new(name + ".pdf", "application/pdf", Pdf(data)),
            _ => throw new ArgumentException("Unsupported report format.")
        };
    }

    private static byte[] Csv(EnergyReportData d)
    {
        var sb = new StringBuilder();
        void Row(params string[] cells) => sb.Append(string.Join(',', cells.Select(Cell))).Append("\r\n");
        Row("section", "store_id", "from_utc", "to_utc", "time_zone", "currency", "base_rate_per_kwh", "total_kwh", "peak_kwh",
            "off_peak_kwh", "total_cost", "peak_cost", "off_peak_cost", "max_demand_kw");
        Row("summary", d.StoreId, d.FromUtc.ToString("O", Inv), d.ToUtc.ToString("O", Inv), d.TimeZone, d.Currency, N(d.BaseRatePerKwh),
            N(d.TotalKwh), N(d.PeakKwh), N(d.OffPeakKwh), N(d.TotalCost), N(d.PeakCost), N(d.OffPeakCost), N(d.MaxDemandKw));
        sb.Append("\r\n");
        Row("section", "equipment_id", "equipment_name", "kwh", "peak_kw", "cost", "share_percent");
        foreach (var e in d.Equipment) Row("equipment", e.EquipmentId, e.EquipmentName, N(e.Kwh), N(e.PeakKw), N(e.Cost), N(e.SharePercent));
        sb.Append("\r\n");
        Row("section", "date", "equipment_id", "equipment_name", "kwh", "peak_kw", "cost");
        foreach (var r in d.Daily) Row("daily", r.Day.ToString("yyyy-MM-dd", Inv), r.EquipmentId, r.EquipmentName, N(r.Kwh), N(r.PeakKw), N(r.Cost));
        sb.Append("\r\n");
        Row("section", "rank", "local_hour_start", "kwh", "cost");
        var rank = 1;
        foreach (var p in d.PeakDemandHours) Row("peak_demand_hour", (rank++).ToString(Inv), p.LocalHourStart.ToString("yyyy-MM-dd HH:mm", Inv), N(p.Kwh), N(p.Cost));
        sb.Append("\r\n");
        Row("section", "local_hour", "average_kwh", "peak_tariff");
        foreach (var h in d.HourOfDayProfile) Row("hour_profile", h.LocalHour.ToString("00", Inv), N(h.AverageKwh), h.IsPeakTariff ? "true" : "false");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static byte[] Pdf(EnergyReportData d)
    {
        var pdf = new SimplePdfDocument($"Electrolink energy efficiency report - {d.StoreId} - generated {d.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC");
        pdf.Title("Energy Efficiency Report");
        pdf.Line($"Store: {d.StoreId}");
        pdf.Line($"Period (UTC): {d.FromUtc:yyyy-MM-dd HH:mm} to {d.ToUtc:yyyy-MM-dd HH:mm}   Time zone: {d.TimeZone}");
        pdf.Line($"Tariff: {N(d.BaseRatePerKwh)} {d.Currency}/kWh base");

        pdf.Heading("Summary");
        pdf.Line($"Total energy:      {N(d.TotalKwh),14} kWh    Total cost:      {N(d.TotalCost),12} {d.Currency}");
        pdf.Line($"Peak-hour energy:  {N(d.PeakKwh),14} kWh    Peak-hour cost:  {N(d.PeakCost),12} {d.Currency}");
        pdf.Line($"Off-peak energy:   {N(d.OffPeakKwh),14} kWh    Off-peak cost:   {N(d.OffPeakCost),12} {d.Currency}");
        pdf.Line($"Max demand (hourly average): {N(d.MaxDemandKw)} kW");

        pdf.Heading("Consumption by equipment");
        pdf.Line($"{"Equipment",-40} {"kWh",12} {"Peak kW",9} {"Cost",12} {"Share %",8}");
        foreach (var e in d.Equipment) pdf.Line($"{Fit(e.EquipmentName, 40),-40} {N(e.Kwh),12} {N(e.PeakKw),9} {N(e.Cost),12} {N(e.SharePercent),8}");
        if (d.Equipment.Count == 0) pdf.Line("No equipment registered.");

        pdf.Heading("Peak demand hours");
        pdf.Line($"{"#",3} {"Local hour",-18} {"kWh (avg kW)",14} {"Cost",12}");
        var rank = 1;
        foreach (var p in d.PeakDemandHours) pdf.Line($"{rank++,3} {p.LocalHourStart.ToString("yyyy-MM-dd HH:mm", Inv),-18} {N(p.Kwh),14} {N(p.Cost),12}");
        if (d.PeakDemandHours.Count == 0) pdf.Line("No consumption recorded in this period.");

        pdf.Heading("Average consumption by hour of day");
        foreach (var h in d.HourOfDayProfile)
        {
            var bar = d.HourOfDayProfile.Max(x => x.AverageKwh) is var max && max > 0 ? new string('#', (int)Math.Round(h.AverageKwh / max * 50)) : "";
            pdf.Line($"{h.LocalHour:00}:00 {(h.IsPeakTariff ? "P" : " ")} {N(h.AverageKwh),10} kWh  {bar}");
        }
        pdf.Line("P = peak tariff hour");

        pdf.Heading("Daily breakdown by equipment");
        pdf.Line($"{"Date",-10} {"Equipment",-40} {"kWh",12} {"Peak kW",9} {"Cost",12}");
        foreach (var r in d.Daily) pdf.Line($"{r.Day.ToString("yyyy-MM-dd", Inv),-10} {Fit(r.EquipmentName, 40),-40} {N(r.Kwh),12} {N(r.PeakKw),9} {N(r.Cost),12}");
        if (d.Daily.Count == 0) pdf.Line("No consumption recorded in this period.");
        return pdf.ToBytes();
    }

    private static string N(double value) => value.ToString("0.000", Inv);
    private static string N(decimal value) => value.ToString("0.00", Inv);
    private static string Fit(string value, int width) => value.Length <= width ? value : value[..(width - 1)] + "~";

    // Formula-injection safe CSV cell.
    private static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !double.TryParse(value, NumberStyles.Float, Inv, out _))
            value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
