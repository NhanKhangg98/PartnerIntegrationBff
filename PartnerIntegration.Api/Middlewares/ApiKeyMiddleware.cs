namespace PartnerIntegration.Api.Middlewares;

/// <summary>
/// Middleware validates the X-Api-Key header on every incoming request.
/// Valid keys are configured via Security:ApiKeys (comma-separated list).
/// </summary>
public class ApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly HashSet<string> _validApiKeys;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        var raw = configuration["Security:ApiKeys"] ?? string.Empty;
        _validApiKeys = new HashSet<string>(
            raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal
        );
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Allow Swagger and internal mock routes to pass without API key
        if (IsExcludedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey)
            || !_validApiKeys.Contains(extractedApiKey.ToString()))
        {
            _logger.LogWarning(
                "Unauthorized request. Missing or invalid {Header}. Path: {Path}",
                ApiKeyHeaderName,
                context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                status = 401,
                error = "Unauthorized",
                message = $"A valid '{ApiKeyHeaderName}' header is required."
            });
            return;
        }

        await _next(context);
    }

    private static bool IsExcludedPath(PathString path)
    {
        return path.StartsWithSegments("/swagger")
            || path.StartsWithSegments("/api/internal/mock");
    }
}
