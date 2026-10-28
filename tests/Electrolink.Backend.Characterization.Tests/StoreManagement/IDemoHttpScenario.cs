using System.Text.Json;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

internal interface IDemoHttpScenario : IDisposable
{
    User Owner { get; }
    User Other { get; }
    Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null,
        bool otherUser = false, bool anonymous = false, bool edgeKey = false);
    Task<JsonElement> Create(string path, object body);
}
