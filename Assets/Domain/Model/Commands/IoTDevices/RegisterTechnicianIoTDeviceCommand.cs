using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
namespace Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;

// Trusted application command; the HTTP body cannot choose the stock technician.
public sealed record RegisterTechnicianIoTDeviceCommand(TechnicianId Technician, string SerialNumber, string PlainTextApiKey, string FirmwareVersion);
