namespace TinyFileIO.Middleware;

/// <summary>
/// Adds x-amz-request-id and x-amz-id-2 to every response and stores them
/// in HttpContext.Items so controllers can embed them in XML error responses.
/// </summary>
public sealed class S3RequestIdMiddleware
{
    private readonly RequestDelegate _next;

    public S3RequestIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var hostId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        context.Items["x-amz-request-id"] = requestId;
        context.Items["x-amz-id-2"] = hostId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers["x-amz-request-id"] = requestId;
            context.Response.Headers["x-amz-id-2"] = hostId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
