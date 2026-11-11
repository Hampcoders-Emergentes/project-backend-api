using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;

public sealed record ReportFile(string FileName, string ContentType, byte[] Content);

/// <summary>Outbound port: renders a consumption metric into a downloadable file.</summary>
public interface IConsumptionReportExporter
{
    ReportFile Export(StoreEnergyMetric metric, ExportFormat format);
}
