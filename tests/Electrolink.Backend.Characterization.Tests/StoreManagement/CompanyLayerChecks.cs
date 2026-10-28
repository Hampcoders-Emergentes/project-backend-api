using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hampcoders.Electrolink.API.Profiles.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Resources;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

internal static class CompanyLayerChecks
{
    internal static CompleteProfileAsCompanyResource Resource(string taxId) => new("Layer Corp", taxId,
        "Billing street", "12", "Billing district", "Billing city", "Billing country", "B-0001", "Layer industry", ECompanySize.Small,
        "https://company.example.invalid", "Layer", "Owner", "+99900000031", "00000031", "1991-02-03",
        "Personal street", "34", "Personal district", "Personal city", "Personal country", "P-0002");

    internal static async Task Rejections(RealBackendScenario h, CompanyCompletionTrace trace, CompleteProfileAsCompanyResource valid)
    {
        var invalidJson = JsonSerializer.SerializeToNode(valid, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        invalidJson["companySize"] = "UnsupportedSize";
        using (var response = await h.Send(HttpMethod.Post, "/api/v1/profiles/me/complete/company", invalidJson))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Console.WriteLine("CONTROLLED binding error: " + error.GetProperty("errors").GetRawText());
            Assert.Equal(400, error.GetProperty("status").GetInt32());
            Assert.True(error.GetProperty("errors").TryGetProperty("$.companySize", out _));
            var step = Assert.Single(trace.Steps); Assert.False(step.ModelValid); Assert.False(step.ControllerReached); Assert.False(step.ApplicationReached);
            Console.WriteLine("CONTROLLED 400 binding: Content-Type=application/json; errors field=$.companySize; controller=false; application=false");
        }
        await UnchangedIncomplete(h, trace);
        using (var response = await h.Send(HttpMethod.Post, "/api/v1/profiles/me/complete/company", valid with { PhoneNumber = "not-a-phone" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(error.TryGetProperty("message", out var message));
            Console.WriteLine("CONTROLLED domain error: " + message.GetString());
            var step = trace.Steps.Last(); Assert.True(step.ModelValid); Assert.True(step.ControllerReached); Assert.True(step.ApplicationReached);
            Assert.Equal("InvalidPhoneNumberException", step.ExceptionType);
            Console.WriteLine("CONTROLLED 400 domain: Content-Type=application/json; exception=InvalidPhoneNumberException; DomainExceptionFilter; controller=true; application=true");
        }
        await UnchangedIncomplete(h, trace);
        Assert.Equal(2, trace.Steps.Count);
    }
    private static async Task UnchangedIncomplete(RealBackendScenario h, CompanyCompletionTrace trace)
    {
        using var scope = h.Scope(); var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await ctx.Profiles.AsNoTracking().SingleAsync();
        Assert.Equal(EProfileStatus.Incomplete, profile.Status); Assert.Null(profile.PersonalData); Assert.Null(profile.Company);
        Assert.Equal(0, await ctx.Set<Hampcoders.Electrolink.API.Profiles.Domain.Model.Entities.Company>().CountAsync());
        Assert.Equal(0, await ctx.Set<Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates.PropertyPortfolio>().CountAsync());
        Assert.Equal(0, await ctx.OutboxMessages.CountAsync()); Assert.Empty(trace.Events);
    }
    internal static async Task Duplicate(RealBackendScenario h, CompanyCompletionTrace trace, CompleteProfileAsCompanyResource existing)
    {
        var events = trace.Events.Count;
        using var response = await h.Send(HttpMethod.Post, "/api/v1/profiles/me/complete/company", existing, otherUser: true);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(error.TryGetProperty("message", out _));
        var step = trace.Steps.Last(); Assert.True(step.ModelValid); Assert.True(step.ControllerReached); Assert.True(step.ApplicationReached);
        Assert.Equal("TaxIdAlreadyInUseException", step.ExceptionType); Assert.Equal(events, trace.Events.Count);
        using var scope = h.Scope(); var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var other = await ctx.Profiles.AsNoTracking().SingleAsync(p => p.UserId != h.Owner.Id);
        Assert.Equal(EProfileStatus.Incomplete, other.Status); Assert.Null(other.Company); Assert.Null(other.PersonalData);
        Assert.Equal(1, await ctx.Set<Hampcoders.Electrolink.API.Profiles.Domain.Model.Entities.Company>().CountAsync());
        Assert.Equal(1, await ctx.Set<Hampcoders.Electrolink.API.Assets.Domain.Model.Aggregates.PropertyPortfolio>().CountAsync());
        Console.WriteLine("CONTROLLED 409 uniqueness: real PostgreSQL checker; TaxIdAlreadyInUseException mapped by existing filter; second profile still Incomplete; no extra Company/portfolio/event");
    }
    internal static async Task Persisted(RealBackendScenario h, CompanyCompletionTrace trace, CompleteProfileAsCompanyResource expected)
    {
        Assert.Equal(5, trace.Steps.Count); // Two initial 400s, first completion, duplicate TaxId 409, second completion.
        var step = trace.Steps[2]; Assert.True(step.ModelValid); Assert.True(step.ControllerReached); Assert.True(step.ApplicationReached);
        Assert.Null(step.ExceptionType); Assert.Equal(expected, step.Resource);
        using var scope = h.Scope(); var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await ctx.Profiles.AsNoTracking().Include(p => p.Company).SingleAsync(p => p.UserId == h.Owner.Id);
        Assert.NotNull(profile.Company); Assert.NotNull(profile.PersonalData);
        var company = profile.Company.CompanyData; var personal = profile.PersonalData;
        Assert.Equal(new string?[] { expected.CompanyName, expected.TaxId, expected.BillingStreet, expected.BillingNumber, expected.BillingDistrict,
            expected.BillingCity, expected.BillingCountry, expected.BillingPostalCode, expected.Industry, expected.CompanySize.ToString(), expected.Website,
            expected.FirstName, expected.LastName, expected.PhoneNumber, expected.Dni, expected.DateOfBirth, expected.Street, expected.Number,
            expected.District, expected.City, expected.Country, expected.PostalCode },
            new string?[] { company.CompanyName, company.TaxId.Value, company.BillingAddress.Street, company.BillingAddress.Number,
                company.BillingAddress.District, company.BillingAddress.City, company.BillingAddress.Country, company.BillingAddress.PostalCode,
                company.Industry, company.Size.ToString(), company.Website, personal.FirstName, personal.LastName, personal.PhoneNumber.Value,
                personal.Dni.Value, personal.DateOfBirth.Value.ToString("yyyy-MM-dd"), personal.Address.Street, personal.Address.Number,
                personal.Address.District, personal.Address.City, personal.Address.Country, personal.Address.PostalCode });
        Assert.Equal(2, trace.Events.Count);
        var completed = Assert.Single(trace.Events, e => e.UserId == h.Owner.Id.Value);
        Assert.Equal(profile.ProfileId.Value, completed.ProfileId); Assert.Equal(profile.Company.CompanyId.Value, completed.SubjectId.ToString());
        Assert.Equal(EBusinessRole.Company, completed.BusinessRole);
        using var response = await h.Send(HttpMethod.Get, "/api/v1/profiles/me"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(); var value = dto.GetProperty("company");
        Assert.Equal(profile.ProfileId.Value, dto.GetProperty("profileId").GetString());
        Assert.Equal(h.Owner.Id.Value, dto.GetProperty("userId").GetString()); Assert.Equal("Active", dto.GetProperty("status").GetString());
        Assert.Equal("Company", dto.GetProperty("businessRole").GetString());
        Assert.Equal(new[] { profile.Company.CompanyId.Value, expected.CompanyName, expected.TaxId, expected.Industry,
            expected.CompanySize.ToString(), expected.Website, expected.BillingStreet, expected.BillingNumber, expected.BillingDistrict,
            expected.BillingCity, expected.BillingCountry, expected.BillingPostalCode },
            new[] { "companyId", "companyName", "taxId", "industry", "companySize", "website", "billingStreet", "billingNumber", "billingDistrict",
                "billingCity", "billingCountry", "billingPostalCode" }.Select(key => value.GetProperty(key).GetString()).ToArray());
        Console.WriteLine("PASS Company layers: 22 input fields reach resource/assembler/application/domain/repository/UoW unchanged; ProfileCompleted identities match persisted profile/subject; profile GET DTO and real Assets portfolios verified");
    }
}
