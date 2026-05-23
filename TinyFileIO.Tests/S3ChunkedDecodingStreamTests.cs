using System.Text;
using Microsoft.AspNetCore.Http;
using TinyFileIO.Services;

namespace TinyFileIO.Tests;

public sealed class S3ChunkedDecodingStreamTests
{
    // ── Static helpers ────────────────────────────────────────────────────────

    [Fact]
    public void IsAwsChunked_ContentEncodingHeader_ReturnsTrue()
    {
        var headers = new HeaderDictionary
        {
            ["Content-Encoding"] = "aws-chunked"
        };
        Assert.True(S3ChunkedDecodingStream.IsAwsChunked(headers));
    }

    [Fact]
    public void IsAwsChunked_StreamingPayloadHash_ReturnsTrue()
    {
        var headers = new HeaderDictionary
        {
            ["x-amz-content-sha256"] = "STREAMING-AWS4-HMAC-SHA256-PAYLOAD"
        };
        Assert.True(S3ChunkedDecodingStream.IsAwsChunked(headers));
    }

    [Fact]
    public void IsAwsChunked_NoRelevantHeaders_ReturnsFalse()
    {
        var headers = new HeaderDictionary
        {
            ["Content-Type"] = "application/octet-stream"
        };
        Assert.False(S3ChunkedDecodingStream.IsAwsChunked(headers));
    }

    [Fact]
    public void GetDecodedContentLength_ValidHeader_ReturnsValue()
    {
        var headers = new HeaderDictionary
        {
            ["x-amz-decoded-content-length"] = "42"
        };
        Assert.Equal(42L, S3ChunkedDecodingStream.GetDecodedContentLength(headers));
    }

    [Fact]
    public void GetDecodedContentLength_AbsentHeader_ReturnsNull()
    {
        var headers = new HeaderDictionary();
        Assert.Null(S3ChunkedDecodingStream.GetDecodedContentLength(headers));
    }

    // ── Stream reading ────────────────────────────────────────────────────────

    /// <summary>Encodes <paramref name="payloads"/> as aws-chunked framing.</summary>
    private static MemoryStream MakeChunked(params byte[][] payloads)
    {
        var sb = new StringBuilder();
        foreach (var payload in payloads)
        {
            sb.Append(payload.Length.ToString("x"));
            sb.Append("\r\n");
            sb.Append(Encoding.ASCII.GetString(payload));
            sb.Append("\r\n");
        }
        // terminal chunk
        sb.Append("0\r\n\r\n");
        return new MemoryStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task Read_SingleChunk_ReturnsExactPayload()
    {
        var payload = "hello world"u8.ToArray();
        var decoded = new S3ChunkedDecodingStream(MakeChunked(payload));
        Assert.Equal(payload, await ReadAllAsync(decoded));
    }

    [Fact]
    public async Task Read_MultipleChunks_ConcatenatesPayload()
    {
        var p1 = "foo"u8.ToArray();
        var p2 = "bar"u8.ToArray();
        var decoded = new S3ChunkedDecodingStream(MakeChunked(p1, p2));
        var expected = p1.Concat(p2).ToArray();
        Assert.Equal(expected, await ReadAllAsync(decoded));
    }

    [Fact]
    public async Task Read_TerminalZeroChunk_ReturnsZeroOnNextRead()
    {
        var decoded = new S3ChunkedDecodingStream(MakeChunked("x"u8.ToArray()));
        await ReadAllAsync(decoded); // consume everything
        var buf = new byte[4];
        var n = await decoded.ReadAsync(buf, TestContext.Current.CancellationToken);
        Assert.Equal(0, n);
    }

    [Fact]
    public async Task Read_SmallBuffer_WorksAcrossMultipleCalls()
    {
        var payload = Encoding.ASCII.GetBytes("abcdefghij");
        var stream = new S3ChunkedDecodingStream(MakeChunked(payload));

        var result = new List<byte>();
        var buf = new byte[3];
        var ct = TestContext.Current.CancellationToken;
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
            result.AddRange(buf[..read]);

        Assert.Equal(payload, result.ToArray());
    }

    [Fact]
    public async Task Read_EmptyStream_ReturnsZeroBytesWithoutThrowing()
    {
        // Zero-length body: just the terminal chunk
        var decoded = new S3ChunkedDecodingStream(MakeChunked());
        var data = await ReadAllAsync(decoded);
        Assert.Empty(data);
    }
}
