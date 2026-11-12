using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.Persistence.EFC.Configurations.Extensions;

public static class ModelBuilderExtensions
{
    public static void ApplyAnalyticsConfiguration(this ModelBuilder builder)
    {
        builder.ApplyConfiguration(new TechnicianMetricsConfiguration());
    }
}
