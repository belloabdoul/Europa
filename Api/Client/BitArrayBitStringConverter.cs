using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Core.Entities.Images;
using Core.Interfaces.Commons;
using Npgsql.Internal;

namespace Api.Client;

using static BitStringHelpers;

file static class BitStringHelpers
{
    public static int GetByteCountFromBitCount(int n)
    {
        const int bitShiftPerByte = 3;
        Debug.Assert(n >= 0);
        // Due to sign extension, we don't need to special case for n == 0, since ((n - 1) >> 3) + 1 = 0
        // This doesn't hold true for ((n - 1) / 8) + 1, which equals 1.
        return (n - 1 + (1 << bitShiftPerByte)) >>> bitShiftPerByte;
    }
}

[Experimental("NPG9001")]
internal sealed class BitArrayBitStringConverter : PgStreamingConverter<BitArray>
{
    public override BitArray Read(PgReader reader)
    {
        if (reader.ShouldBuffer(sizeof(int)))
            reader.Buffer(sizeof(int));

        var bits = reader.ReadInt32();
        var bytes = GC.AllocateUninitializedArray<byte>(GetByteCountFromBitCount(bits));
        reader.ReadBytes(bytes);
        return new BitArray(bits, bytes);
    }

    public override async ValueTask<BitArray> ReadAsync(PgReader reader, CancellationToken cancellationToken = default)
    {
        if (reader.ShouldBuffer(sizeof(int)))
            await reader.BufferAsync(sizeof(int), cancellationToken).ConfigureAwait(false);

        var bits = reader.ReadInt32();
        var bytes = GC.AllocateUninitializedArray<byte>(GetByteCountFromBitCount(bits));
        await reader.ReadBytesAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new BitArray(bits, bytes);
    }

    public override Size GetSize(SizeContext context, BitArray value, ref object? writeState)
        => sizeof(int) + GetByteCountFromBitCount(value.Length);

    public override void Write(PgWriter writer, BitArray value)
        => Write(async: false, writer, value, CancellationToken.None).GetAwaiter().GetResult();

    public override ValueTask WriteAsync(PgWriter writer, BitArray value, CancellationToken cancellationToken = default)
        => Write(async: true, writer, value, cancellationToken);

    async ValueTask Write(bool async, PgWriter writer, BitArray value, CancellationToken cancellationToken = default)
    {
        if (writer.ShouldFlush(sizeof(int)))
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        writer.WriteInt32(value.Length);
        if (async)
            await writer.WriteBytesAsync(value.Values, cancellationToken).ConfigureAwait(false);
        else
            writer.WriteBytes(value.Values.AsSpan());
    }
}