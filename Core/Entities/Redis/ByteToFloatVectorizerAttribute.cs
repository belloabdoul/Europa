using System.Text.Json.Serialization;
using Redis.OM.Contracts;
using Redis.OM.Modeling;

namespace Core.Entities.Redis;

/// <inheritdoc />
public class ByteToFloatVectorizerAttribute : VectorizerAttribute<byte[]>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ByteToFloatVectorizerAttribute" /> class.
    /// </summary>
    /// <param name="dim">The dimensions of the vector.</param>
    public ByteToFloatVectorizerAttribute(int dim)
    {
        Dim = dim;
        Vectorizer = new ByteToFloatVectorizer(dim);
    }

    public override VectorType VectorType { get; } = VectorType.FLOAT32;
    public override int Dim { get; }
    public override IVectorizer<byte[]> Vectorizer { get; }

    public override byte[] Vectorize(object obj)
    {
        if (obj is not byte[] bytes) throw new InvalidOperationException("Must pass in an array of binary bytes");

        return Vectorizer.Vectorize(bytes);
    }

    /// <summary>
    ///     Creates the json converter fulfilled by this attribute.
    /// </summary>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <returns>The Json Converter.</returns>
    public override JsonConverter? CreateConverter(Type typeToConvert)
    {
        return new ByteVectorJsonConverter(this);
    }
}