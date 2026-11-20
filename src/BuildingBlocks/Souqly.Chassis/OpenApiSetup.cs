using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Souqly.Chassis.Security;

namespace Souqly.Chassis;

/// <summary>
/// Document OpenAPI commun : titre, URL relative (fonctionne derrière la passerelle) et schémas de sécurité
/// (connexion Keycloak en Authorization Code + PKCE, ou jeton collé à la main).
/// </summary>
public static class OpenApiSetup
{
    public const string OAuth2Scheme = "oauth2";
    public const string BearerScheme = "bearer";

    public static IHostApplicationBuilder AddSouqlyOpenApi(this IHostApplicationBuilder builder, string title)
    {
        var auth = builder.Configuration.GetSection("Authentication").Get<SouqlyAuthOptions>() ?? new SouqlyAuthOptions();
        builder.Services.AddSouqlyOpenApi(title, auth);
        return builder;
    }

    public static IServiceCollection AddSouqlyOpenApi(this IServiceCollection services, string title, SouqlyAuthOptions auth)
    {
        services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
        {
            doc.Info.Title = title;
            doc.Info.Version = "1.0.0";
            doc.Servers = [];   // URL relative : la doc fonctionne en direct comme derrière la passerelle

            var realm = auth.Authority.TrimEnd('/');
            doc.Components ??= new OpenApiComponents();
            doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            doc.Components.SecuritySchemes[OAuth2Scheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "Connexion Keycloak (realm souqly), client public souqly-web avec PKCE.",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{realm}/protocol/openid-connect/auth"),
                        TokenUrl = new Uri($"{realm}/protocol/openid-connect/token"),
                        Scopes = new Dictionary<string, string> { ["openid"] = "Identité OpenID Connect" },
                    },
                },
            };
            doc.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Jeton d'accès Keycloak collé à la main (Authorization: Bearer …).",
            };
            doc.Security =
            [
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(OAuth2Scheme, doc)] = ["openid"] },
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, doc)] = [] },
            ];
            return Task.CompletedTask;
        }));
        return services;
    }
}
