using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace TinyFileIO.Services;

/// <summary>Serializes and deserializes S3 XML request/response bodies.</summary>
public interface IS3XmlSerializer
{
    string Serialize<T>(T obj);
    T? Deserialize<T>(Stream stream);
    Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default);
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
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return DeserializeBuffered<T>(buffer);
    }

    public async Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return DeserializeBuffered<T>(buffer);
    }

    private static T? DeserializeBuffered<T>(MemoryStream buffer)
    {
        try
        {
            buffer.Position = 0;
            var serializer = new XmlSerializer(typeof(T));
            return (T?)serializer.Deserialize(buffer);
        }
        catch (InvalidOperationException)
        {
            return DeserializeWithoutNamespaces<T>(buffer);
        }
        catch (XmlException)
        {
            return default;
        }
    }

    private static T? DeserializeWithoutNamespaces<T>(MemoryStream buffer)
    {
        try
        {
            buffer.Position = 0;
            var document = XDocument.Load(buffer);
            var withoutNamespaces = RemoveNamespaces(document.Root!);
            using var reader = withoutNamespaces.CreateReader();

            var serializer = new XmlSerializer(typeof(T), new XmlRootAttribute
            {
                ElementName = GetRootElementName(typeof(T)),
                Namespace = string.Empty
            });
            return (T?)serializer.Deserialize(reader);
        }
        catch (InvalidOperationException)
        {
            return default;
        }
        catch (XmlException)
        {
            return default;
        }
    }

    private static XElement RemoveNamespaces(XElement element)
    {
        return new XElement(element.Name.LocalName,
            element.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration)
                .Select(attribute => new XAttribute(attribute.Name.LocalName, attribute.Value)),
            element.Nodes().Select(node => node is XElement child ? RemoveNamespaces(child) : node));
    }

    private static string GetRootElementName(Type type)
    {
        var root = type.GetCustomAttributes(typeof(XmlRootAttribute), inherit: false)
            .OfType<XmlRootAttribute>()
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(root?.ElementName) ? type.Name : root.ElementName;
    }
}
