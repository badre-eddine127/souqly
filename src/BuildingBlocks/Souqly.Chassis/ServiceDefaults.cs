using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Souqly.Chassis.Errors;
using Souqly.Chassis.Security;

namespace Souqly.Chassis;

/// <summary>Configuration commune à tous les services : erreurs, sécurité, santé.</summary>
public static class ServiceDefaults
{
    public static IHostApplicationBuilder AddSouqlyDefaults(this IHostApplicationBuilder builder)
    {
        builder.AddSouqlyTelemetry();
        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsExceptionHandler.Customize);
        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        builder.Services.AddSouqlyJwt(builder.Configuration, builder.Environment);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Vendor, p => p.RequireRole(Roles.Vendor))
            .AddPolicy(Policies.Customer, p => p.RequireRole(Roles.Customer))
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.Internal, p => p.RequireRole("SERVICE"));

        return builder;
    }

    public static WebApplication UseSouqlyDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = (ctx, report) => ctx.Response.WriteAsync(report.Status.ToString()),
        });
        return app;
    }
}

public static class Policies
{
    public const string Vendor = "vendor";
    public const string Customer = "customer";
    public const string Admin = "admin";
    public const string Internal = "internal";
}
