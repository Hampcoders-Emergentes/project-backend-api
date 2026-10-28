using Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates;
namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Queries;

public sealed record TechnicianDeviceStockPage(IReadOnlyList<IoTDevice> Items, int TotalCount, int AvailableCount);
