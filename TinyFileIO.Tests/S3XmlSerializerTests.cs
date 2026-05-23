using System.Text;
using System.Xml.Serialization;
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
}
