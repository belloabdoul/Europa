using Redis.OM.Contracts;
using Redis.OM.Modeling;

namespace Core.Entities.Redis;

/// <summary>
/// A vectorizer for a byte array composed of 0 and 1.
/// </summary>
public class ByteToFloatVectorizer : IVectorizer<byte[]>
{
    private static readonly Dictionary<byte, byte[]> BinaryToFloatBytes = new()
    {
        {
            0, BitConverter.GetBytes(0f)
        },
        {
            1, BitConverter.GetBytes(1f)
        }
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteToFloatVectorizer"/> class.
    /// </summary>
    /// <param name="dim">The dimensions.</param>
    public ByteToFloatVectorizer(int dim)
    {
        Dim = dim;
    }

    /// <inheritdoc />
    public VectorType VectorType => VectorType.FLOAT32;

    /// <inheritdoc />
    public int Dim { get; }

    /// <inheritdoc />
    public byte[] Vectorize(byte[] obj) => obj.SelectMany(val => BinaryToFloatBytes[val]).ToArray();
}