namespace Souqly.Chassis;

/// <summary>
/// Sonde de santé pour Docker : l'image aspnet n'a ni curl ni wget, donc le service s'interroge lui-même.
/// `dotnet Service.dll --healthcheck` appelle /health/ready et renvoie 0 (sain) ou 1.
/// </summary>
public static class HealthProbe
{
    public static async Task<bool> TryRunAsync(string[] args)
    {
        if (!args.Contains("--healthcheck", StringComparer.Ordinal))
        {
            return false;
        }

        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health/ready"));
            Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            Environment.ExitCode = 1;
        }
        catch (TaskCanceledException)
        {
            Environment.ExitCode = 1;
        }

        return true;
    }
}
