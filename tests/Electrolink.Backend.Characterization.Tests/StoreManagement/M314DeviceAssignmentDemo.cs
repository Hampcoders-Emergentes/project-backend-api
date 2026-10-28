using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hampcoders.Electrolink.API.Assets.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Planning.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M314DeviceAssignmentDemo
{
    // Reuse real identity/request fixtures and the subsequent protected installation assertions.
    public static Task Run(string manifestPath) => M313DeviceInstallationDemo.Run(manifestPath, assignOverHttp: true);
    internal static async Task Check(RealBackendScenario h, string deviceId, string spareId, string propertyId, string wrongPropertyId, RequestId request)
    {
        var path = "/api/v1/io-t-devices/" + deviceId + "/assign/" + propertyId;
        object Body(string id) => new { installationRequestId = id };
        async Task Denied(string url, object body, HttpStatusCode code, bool other = true, bool anonymous = false, bool edge = false)
        {
            var before = await M313DeviceInstallationDemo.Snapshot(h); var events = h.AssetMutationEvents;
            using var response = await h.Send(HttpMethod.Post, url, body, otherUser: other, anonymous: anonymous, edgeKey: edge);
            Assert.Equal(code, response.StatusCode);
            if (code == HttpStatusCode.NotFound) Assert.Equal("{\"message\":\"Resource not found.\"}", await response.Content.ReadAsStringAsync());
            Assert.True(before == await M313DeviceInstallationDemo.Snapshot(h)); Assert.Equal(events, h.AssetMutationEvents);
        }
        await Denied(path, Body(request.Value), HttpStatusCode.NotFound, other: false);
        await Denied(path, Body(request.Value), HttpStatusCode.Unauthorized, anonymous: true);
        await Denied(path, Body(request.Value), HttpStatusCode.Unauthorized, anonymous: true, edge: true);
        await Denied(path, Body("req-missing-synthetic"), HttpStatusCode.NotFound);
        await Denied(path.Replace(propertyId, wrongPropertyId, StringComparison.Ordinal), Body(request.Value), HttpStatusCode.NotFound);
        await Denied(path.Replace(deviceId, "dev-missing-synthetic", StringComparison.Ordinal), Body(request.Value), HttpStatusCode.NotFound);
        using (var scope = h.Scope())
        {
            var before = await M313DeviceInstallationDemo.Snapshot(h); var events = h.AssetMutationEvents;
            var bridge = new IoTInstallationServiceScheduledEventHandler(scope.ServiceProvider.GetRequiredService<IIoTDeviceCommandService>(),
                NullLogger<IoTInstallationServiceScheduledEventHandler>.Instance);
            await Assert.ThrowsAsync<ArgumentException>(() => bridge.Handle(new(propertyId, request.Value, "", DateTime.UtcNow), default));
            Assert.True(before == await M313DeviceInstallationDemo.Snapshot(h)); Assert.Equal(events, h.AssetMutationEvents);
        }
        var unrelated = await M313DeviceInstallationDemo.Snapshot(h, includeDevice: false, includeProperties: false);
        var count = h.AssetMutationEvents;
        using (var response = await h.Send(HttpMethod.Post, path, Body(request.Value), otherUser: true))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(new[] { "assignedPropertyId", "connectionStatus", "firmwareVersion", "id", "installedAt", "lastReadingAt", "serialNumber", "status" },
                body.EnumerateObject().Select(x => x.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(deviceId, body.GetProperty("id").GetString()); Assert.Equal((int)EDeviceStatus.Assigned, body.GetProperty("status").GetInt32());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("installedAt").ValueKind);
        }
        using (var fresh = h.Scope())
        {
            var devices = fresh.ServiceProvider.GetRequiredService<IIoTDeviceRepository>();
            var device = await devices.FindByIdAsync(IoTDeviceId.From(deviceId)); var spare = await devices.FindByIdAsync(IoTDeviceId.From(spareId));
            Assert.NotNull(device); Assert.NotNull(spare); Assert.Equal(EDeviceStatus.Assigned, device.Status);
            Assert.Equal(propertyId, device.AssignedPropertyId!.Value); Assert.Equal(request.Value, device.InstallationRequestId!.Value);
            Assert.Null(device.InstalledAt); Assert.Null(device.InstalledByTechnicianId);
            Assert.Equal(EDeviceStatus.InStock, spare.Status); Assert.Null(spare.AssignedPropertyId); Assert.Null(spare.InstallationRequestId);
            var property = await fresh.ServiceProvider.GetRequiredService<IPropertyRepository>().FindByIdAsync(PropertyId.From(propertyId));
            Assert.NotNull(property); Assert.Contains(deviceId, property.InstalledDeviceIds); Assert.DoesNotContain(spareId, property.InstalledDeviceIds);
        }
        Assert.Equal(count + 1, h.AssetMutationEvents); Assert.True(unrelated == await M313DeviceInstallationDemo.Snapshot(h, includeDevice: false, includeProperties: false));
        await Denied(path, Body(request.Value), HttpStatusCode.Conflict);
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.14 real assignment HTTP: assigned technician only, explicit chosen stock persisted, spare untouched, existing DTO, one event; unrelated/anonymous/Edge/incompatible requests and repeats unchanged");
        Console.WriteLine("PASS M3.14 empty scheduled selection fails visibly with free stock present and without mutation; continuing existing protected installation checks");
    }
}
