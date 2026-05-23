using System.Text;
using Microsoft.AspNetCore.Http;

namespace TinyFileIO.Services;

/// <summary>
/// Decodes S3 SigV4 streaming payload framing (Content-Encoding: aws-chunked).
/// The request authorization seed signature is validated by S3AuthMiddleware; this
/// stream removes the per-chunk framing before object bytes are persisted.
/// </summary>
public sealed class S3ChunkedDecodingStream : Stream
{
    private readonly Stream _inner;
    private long _remainingInChunk;
    private bool _completed;
    private bool _consumeChunkTerminator;

    public S3ChunkedDecodingStream(Stream inner)
    {
        _inner = inner;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public static bool IsAwsChunked(IHeaderDictionary headers)
    {
        var contentEncoding = headers.ContentEncoding.ToString();
        var payloadHash = headers["x-amz-content-sha256"].ToString();

        return contentEncoding.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                   .Any(encoding => string.Equals(encoding, "aws-chunked", StringComparison.OrdinalIgnoreCase))
               || payloadHash.StartsWith("STREAMING-AWS4-HMAC-SHA256", StringComparison.OrdinalIgnoreCase);
    }

    public static long? GetDecodedContentLength(IHeaderDictionary headers)
    {
        return long.TryParse(headers["x-amz-decoded-content-length"].FirstOrDefault(), out var decodedLength)
            ? decodedLength
            : null;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override int Read(Span<byte> buffer)
    {
        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = ReadAsync(rented.AsMemory(0, buffer.Length)).AsTask().GetAwaiter().GetResult();
            rented.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
            return 0;

        if (_completed)
            return 0;

        if (_remainingInChunk == 0)
        {
            if (_consumeChunkTerminator)
            {
                await ConsumeChunkTerminatorAsync(cancellationToken);
                _consumeChunkTerminator = false;
            }

            await ReadNextChunkHeaderAsync(cancellationToken);
            if (_completed)
                return 0;
        }

        var bytesToRead = (int)Math.Min(buffer.Length, _remainingInChunk);
        var read = await _inner.ReadAsync(buffer[..bytesToRead], cancellationToken);
        if (read == 0)
            throw new EndOfStreamException("Unexpected end of S3 chunked payload.");

        _remainingInChunk -= read;
        if (_remainingInChunk == 0)
            _consumeChunkTerminator = true;

        return read;
    }

    private async Task ReadNextChunkHeaderAsync(CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(cancellationToken);
        var semicolon = line.IndexOf(';');
        var sizeText = semicolon >= 0 ? line[..semicolon] : line;

        if (!long.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var chunkSize)
            || chunkSize < 0)
        {
            throw new InvalidDataException("Invalid S3 chunked payload chunk size.");
        }

        _remainingInChunk = chunkSize;
        if (chunkSize == 0)
        {
            await ConsumeTrailersAsync(cancellationToken);
            _completed = true;
        }
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        var one = new byte[1];

        while (true)
        {
            var read = await _inner.ReadAsync(one, cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of S3 chunked payload.");

            if (one[0] == (byte)'\n')
                break;

            line.WriteByte(one[0]);
        }

        var bytes = line.ToArray();
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            bytes = bytes[..^1];

        return Encoding.ASCII.GetString(bytes);
    }

    private async Task ConsumeChunkTerminatorAsync(CancellationToken cancellationToken)
    {
        var one = new byte[1];
        var read = await _inner.ReadAsync(one, cancellationToken);
        if (read == 0)
            throw new EndOfStreamException("Unexpected end of S3 chunked payload.");

        if (one[0] == (byte)'\n')
            return;

        if (one[0] != (byte)'\r')
            throw new InvalidDataException("Invalid S3 chunked payload chunk terminator.");

        read = await _inner.ReadAsync(one, cancellationToken);
        if (read == 0 || one[0] != (byte)'\n')
            throw new InvalidDataException("Invalid S3 chunked payload chunk terminator.");
    }

    private async Task ConsumeTrailersAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken);
            if (line.Length == 0)
                return;
        }
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();
}
