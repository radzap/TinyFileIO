using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace TinyFileIO.Services;

/// <summary>Serializes and deserializes S3 XML request/response bodies.</summary>
public interface IS3XmlSerializer
{
    string Serialize<T>(T obj);
    T? Deserialize<T>(Stream stream);
}

public sealed class S3XmlSerializer : IS3XmlSerializer
{
    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = Encoding.UTF8,
        Indent = false,
        OmitXmlDeclaration = false
    };

    // Suppress the xsi/xsd namespace declarations that XmlSerializer emits by default.
    private static readonly XmlSerializerNamespaces EmptyNamespaces = new([XmlQualifiedName.Empty]);

    public string Serialize<T>(T obj)
    {
        var serializer = new XmlSerializer(typeof(T));
        using var ms = new MemoryStream();
        using var writer = XmlWriter.Create(ms, WriterSettings);
        serializer.Serialize(writer, obj, EmptyNamespaces);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public T? Deserialize<T>(Stream stream)
    {
        var serializer = new XmlSerializer(typeof(T));
        try
        {
            return (T?)serializer.Deserialize(stream);
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }
}
