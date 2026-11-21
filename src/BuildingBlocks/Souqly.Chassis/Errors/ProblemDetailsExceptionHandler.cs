using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Souqly.Chassis.Errors;

/// <summary>
/// Traduit les exceptions en réponses application/problem+json (RFC 9457), toujours avec un traceId.
/// Les détails techniques restent dans les logs, jamais dans la réponse (OWASP A05).
/// </summary>
public sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    private const string TypeBase = "https://docs.souqly.ma/errors/";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException ve => Validation(ve),
            NotFoundException => Problem(StatusCodes.Status404NotFound, "not-found", "Ressource introuvable", exception.Message),
            ForbiddenException => Problem(StatusCodes.Status403Forbidden, "forbidden", "Action interdite", exception.Message),
            ConflictException => Problem(StatusCodes.Status409Conflict, "conflict", "Conflit d'état", exception.Message),
            BusinessRuleException br => Problem(StatusCodes.Status422UnprocessableEntity, br.Code, "Règle métier non respectée", br.Message),
            DependencyUnavailableException du => Unavailable(httpContext, du),
            BadHttpRequestException => Problem(StatusCodes.Status400BadRequest, "bad-request", "Requête invalide", "Le corps ou les paramètres de la requête sont mal formés."),
            _ => null,
        };

        if (exception is DependencyUnavailableException unavailable)
        {
            // La cause (réponse de l'autre service, délai dépassé…) n'est pas renvoyée au client : elle reste dans les logs.
            logger.LogWarning(unavailable.InnerException ?? unavailable, "Dépendance {Dependency} indisponible sur {Method} {Path}",
                unavailable.Dependency, httpContext.Request.Method, httpContext.Request.Path);
        }

        if (problem is null)
        {
            logger.LogError(exception, "Erreur inattendue sur {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            problem = Problem(StatusCodes.Status500InternalServerError, "internal", "Erreur interne", "Une erreur est survenue.");
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string code, string title, string detail) => new()
    {
        Status = status,
        Type = TypeBase + code,
        Title = title,
        Detail = detail,
    };

    private static ProblemDetails Unavailable(HttpContext httpContext, DependencyUnavailableException e)
    {
        httpContext.Response.Headers.RetryAfter = e.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var problem = Problem(StatusCodes.Status503ServiceUnavailable, "dependency-unavailable", "Service momentanément indisponible", e.Message);
        problem.Extensions["retryAfterSeconds"] = e.RetryAfterSeconds;
        return problem;
    }

    private static ValidationProblemDetails Validation(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Type = TypeBase + "validation",
            Title = "Requête invalide",
            Detail = "Un ou plusieurs champs sont invalides.",
        };
    }

    /// <summary>Ajoute traceId et instance à toutes les réponses d'erreur, y compris 401/403/404 du framework.</summary>
    public static void Customize(ProblemDetailsContext context)
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
    }
}
