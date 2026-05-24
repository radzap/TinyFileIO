using System.Text;
using System.Xml.Serialization;
using TinyFileIO.Models.Api.Multipart;
using TinyFileIO.Services;

namespace TinyFileIO.Tests;

public sealed class S3XmlSerializerTests
{
    private readonly S3XmlSerializer _sut = new();

    [XmlRoot("TestObject")]
    public sealed class TestObject
    {
        [XmlElement("Value")]
        public string Value { get; set; } = string.Empty;

        [XmlElement("Number")]
        public int Number { get; set; }
    }

    [Fact]
    public void Serialize_ProducesValidUtf8XmlWithExpectedRootElement()
    {
        var xml = _sut.Serialize(new TestObject { Value = "hello", Number = 42 });
        Assert.Contains("<TestObject>", xml);
        Assert.Contains("<Value>hello</Value>", xml);
        Assert.Contains("<Number>42</Number>", xml);
    }

    [Fact]
    public void Serialize_EmitsXmlDeclaration()
    {
        var xml = _sut.Serialize(new TestObject { Value = "x" });
        Assert.StartsWith("<?xml", xml);
    }

    [Fact]
    public void Serialize_RoundTripsThroughDeserialize()
    {
        var original = new TestObject { Value = "round-trip", Number = 7 };
        var xml = _sut.Serialize(original);
        var bytes = Encoding.UTF8.GetBytes(xml);
        var restored = _sut.Deserialize<TestObject>(new MemoryStream(bytes));

        Assert.NotNull(restored);
        Assert.Equal(original.Value, restored!.Value);
        Assert.Equal(original.Number, restored.Number);
    }

    [Fact]
    public void Deserialize_EmptyStream_ReturnsNull()
    {
        var result = _sut.Deserialize<TestObject>(new MemoryStream());
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_CompleteMultipartUpload_IgnoresS3DefaultNamespace()
    {
        const string xml = """
            <CompleteMultipartUpload xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
                <Part>
                    <PartNumber>1</PartNumber>
                    <ETag>&quot;etag-1&quot;</ETag>
                </Part>
                <Part>
                    <PartNumber>2</PartNumber>
                    <ETag>&quot;etag-2&quot;</ETag>
                </Part>
            </CompleteMultipartUpload>
            """;

        var result = _sut.Deserialize<CompleteMultipartUploadRequest>(
            new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.NotNull(result);
        Assert.Equal(2, result!.Parts.Count);
        Assert.Equal(1, result.Parts[0].PartNumber);
        Assert.Equal("\"etag-1\"", result.Parts[0].ETag);
        Assert.Equal(2, result.Parts[1].PartNumber);
        Assert.Equal("\"etag-2\"", result.Parts[1].ETag);
    }
}
