namespace TinyFileIO.Middleware;

/// <summary>
/// Rewrites virtual-hosted style S3 requests to path style before routing.
///
/// Virtual-hosted style:   GET  http://{bucket}.{baseHost}/{key}
/// Rewritten to path style: GET  http://{baseHost}/{bucket}/{key}
///
/// The base host is taken from the <c>S3:BaseHost</c> configuration value
/// (e.g. <c>tinyfileio.local</c>). If the setting is absent the middleware is
/// a no-op.
/// </summary>
public sealed class VirtualHostedMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _baseHost;

    public VirtualHostedMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _baseHost = config["S3:BaseHost"]?.ToLowerInvariant();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!string.IsNullOrEmpty(_baseHost))
        {
            var host = context.Request.Host.Host.ToLowerInvariant();

            // Match {bucket}.{baseHost} — but NOT the bare base host itself
            if (host != _baseHost && host.EndsWith("." + _baseHost, StringComparison.Ordinal))
            {
                var bucket = host[..^(_baseHost.Length + 1)]; // strip .{baseHost}

                // Prefix the existing path with /{bucket}
                var originalPath = context.Request.Path.Value ?? "/";
                var newPath = "/" + bucket + (originalPath == "/" ? string.Empty : originalPath);

                context.Request.Path = new PathString(newPath);

                // Store the rewrite flag so controllers/middleware can detect it
                context.Items["VirtualHostedBucket"] = bucket;
            }
        }

        await _next(context);
    }
}
