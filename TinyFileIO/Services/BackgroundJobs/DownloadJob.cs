using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MimeKit;
using TinyFileIO.Models.Api.Buckets;
using TinyFileIO.Models.Api.Objects;

namespace TinyFileIO.Services.BackgroundJobs;

public sealed class DownloadJob : IBackgroundJob
{
    public const string Type = "DownloadFromS3";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IS3BucketService _buckets;
    private readonly IS3ObjectService _objects;

    public DownloadJob(IHttpClientFactory httpClientFactory, IS3BucketService buckets, IS3ObjectService objects)
    {
        _httpClientFactory = httpClientFactory;
        _buckets = buckets;
        _objects = objects;
    }

    public string JobType => Type;

    public async Task ExecuteAsync(BackgroundJobContext context, CancellationToken ct)
    {
        var parameters = JsonSerializer.Deserialize<DownloadJobParameters>(context.Run.ParametersJson, JsonOptions)
            ?? throw new InvalidOperationException("Download job parameters are invalid.");

        var source = new ExternalS3Client(_httpClientFactory.CreateClient(), parameters.ServerUrl, parameters.Username, parameters.Key, parameters.SourceBucket);
        var keys = await source.ListAllKeysAsync(ct);
        var existingKeys = await LoadExistingKeysAsync(parameters.TargetBucket, ct);
        var stats = new DownloadJobStats();

        await context.ReportProgressAsync(new BackgroundJobProgress { TotalItems = keys.Count }, ct);

        foreach (var sourceKey in keys)
        {
            ct.ThrowIfCancellationRequested();
            var targetKey = ResolveTargetKey(sourceKey, existingKeys, parameters.DuplicateMode, stats);

            if (targetKey is null)
            {
                stats.Skipped++;
                stats.Processed++;
                await ReportAsync(context, stats, keys.Count, ct);
                continue;
            }

            try
            {
                await using var sourceObject = await source.GetObjectAsync(sourceKey, ct);
                var put = new PutObjectRequest
                {
                    Bucket = parameters.TargetBucket,
                    Key = targetKey,
                    ContentType = MimeTypes.GetMimeType(targetKey)
                };

                await _objects.PutObjectAsync(put, sourceObject.Stream, ct);
                existingKeys.Add(targetKey);
                stats.Succeeded++;
                stats.BytesProcessed += sourceObject.ContentLength ?? 0;
            }
            catch
            {
                stats.Failed++;
            }

            stats.Processed++;
            await ReportAsync(context, stats, keys.Count, ct);
        }
    }

    private async Task<HashSet<string>> LoadExistingKeysAsync(string bucket, CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var request = new ListObjectsV2Request { MaxKeys = 1000 };

        ListObjectsV2Response response;
        do
        {
            response = await _buckets.ListObjectsV2Async(bucket, request, ct);
            foreach (var obj in response.Contents)
                keys.Add(obj.Key);
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated);

        return keys;
    }

    private static string? ResolveTargetKey(string sourceKey, HashSet<string> existingKeys, string duplicateMode, DownloadJobStats stats)
    {
        if (!existingKeys.Contains(sourceKey) || duplicateMode == DownloadDuplicateModes.Overwrite)
            return sourceKey;

        if (duplicateMode == DownloadDuplicateModes.Skip)
            return null;

        stats.Renamed++;
        return FindAvailableName(sourceKey, existingKeys);
    }

    private static string FindAvailableName(string key, HashSet<string> existingKeys)
    {
        var slash = key.LastIndexOf('/');
        var directory = slash >= 0 ? key[..(slash + 1)] : string.Empty;
        var fileName = slash >= 0 ? key[(slash + 1)..] : key;
        var extension = Path.GetExtension(fileName);
        var nameWithoutExtension = string.IsNullOrEmpty(extension) ? fileName : fileName[..^extension.Length];

        for (var i = 1; ; i++)
        {
            var candidate = $"{directory}{nameWithoutExtension}-{i}{extension}";
            if (!existingKeys.Contains(candidate))
                return candidate;
        }
    }

    private static Task ReportAsync(BackgroundJobContext context, DownloadJobStats stats, int total, CancellationToken ct)
        => context.ReportProgressAsync(new BackgroundJobProgress
        {
            TotalItems = total,
            ProcessedItems = stats.Processed,
            SucceededItems = stats.Succeeded,
            SkippedItems = stats.Skipped,
            FailedItems = stats.Failed,
            BytesProcessed = stats.BytesProcessed,
            StatsJson = JsonSerializer.Serialize(stats, JsonOptions)
        }, ct);

    private sealed class ExternalS3Client
    {
        private readonly HttpClient _http;
        private readonly Uri _serverUrl;
        private readonly string _accessKey;
        private readonly string _secretKey;
        private readonly string _bucket;

        public ExternalS3Client(HttpClient http, string serverUrl, string accessKey, string secretKey, string bucket)
        {
            _http = http;
            _serverUrl = new Uri(serverUrl.TrimEnd('/') + "/");
            _accessKey = accessKey;
            _secretKey = secretKey;
            _bucket = bucket;
        }

        public async Task<List<string>> ListAllKeysAsync(CancellationToken ct)
        {
            var keys = new List<string>();
            string? continuationToken = null;

            do
            {
                var pathAndQuery = $"/{Uri.EscapeDataString(_bucket)}?list-type=2&max-keys=1000";
                if (!string.IsNullOrEmpty(continuationToken))
                    pathAndQuery += "&continuation-token=" + Uri.EscapeDataString(continuationToken);

                using var request = CreateRequest(HttpMethod.Get, pathAndQuery);
                using var response = await _http.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
                XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                keys.AddRange(doc.Descendants(ns + "Contents")
                    .Select(e => e.Element(ns + "Key")?.Value)
                    .Where(k => !string.IsNullOrEmpty(k))
                    .Select(k => k!));

                var isTruncated = bool.TryParse(doc.Root?.Element(ns + "IsTruncated")?.Value, out var truncated) && truncated;
                continuationToken = isTruncated ? doc.Root?.Element(ns + "NextContinuationToken")?.Value : null;
            } while (!string.IsNullOrEmpty(continuationToken));

            return keys;
        }

        public async Task<ExternalS3Object> GetObjectAsync(string key, CancellationToken ct)
        {
            using var request = CreateRequest(HttpMethod.Get, $"/{Uri.EscapeDataString(_bucket)}/{EscapeKey(key)}");
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            return new ExternalS3Object(await response.Content.ReadAsStreamAsync(ct), response.Content.Headers.ContentLength);
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string pathAndQuery)
        {
            var uri = new Uri(_serverUrl, pathAndQuery.TrimStart('/'));
            var request = new HttpRequestMessage(method, uri);
            request.Headers.Date = DateTimeOffset.UtcNow;
            request.Headers.Authorization = new AuthenticationHeaderValue("AWS", $"{_accessKey}:{CreateSignature(method.Method, uri, request.Headers.Date.Value)}");
            return request;
        }

        private string CreateSignature(string method, Uri uri, DateTimeOffset date)
        {
            var canonicalResource = uri.AbsolutePath;

            var stringToSign = $"{method}\n\n\n{date:R}\n{canonicalResource}";
            return Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(_secretKey), Encoding.UTF8.GetBytes(stringToSign)));
        }

        private static string EscapeKey(string key)
            => string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
    }

    private sealed record ExternalS3Object(Stream Stream, long? ContentLength) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Stream.DisposeAsync();
    }

    private sealed class DownloadJobStats
    {
        public int Processed { get; set; }
        public int Succeeded { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int Renamed { get; set; }
        public long BytesProcessed { get; set; }
    }
}

public sealed class DownloadJobParameters
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string SourceBucket { get; set; } = string.Empty;
    public string TargetBucket { get; set; } = string.Empty;
    public string DuplicateMode { get; set; } = DownloadDuplicateModes.Overwrite;
}

public static class DownloadDuplicateModes
{
    public const string Overwrite = "Overwrite";
    public const string Skip = "Skip";
    public const string Rename = "Rename";
}
