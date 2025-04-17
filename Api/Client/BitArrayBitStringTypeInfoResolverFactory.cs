using System.Diagnostics.CodeAnalysis;
using Core.Entities.Images;
using Core.Interfaces.Commons;
using Npgsql.Internal;
using Npgsql.Internal.Postgres;
using NpgsqlTypes;

namespace Api.Client;

[Experimental("NPG9001")]
public class BitArrayBitStringTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    Resolver ResolverInstance { get; } = new();

    public static BitArrayBitStringTypeInfoResolverFactory Instance { get; } = new();

    public override IPgTypeInfoResolver CreateResolver()
    {
        return new Resolver();
    }

    public override IPgTypeInfoResolver CreateArrayResolver()
    {
        return new ArrayResolver();
    }
}

[Experimental("NPG9001")]
public class Resolver : IPgTypeInfoResolver
{
    [field: AllowNull, MaybeNull]
    protected TypeInfoMappingCollection Mappings
    {
        get { return field ??= AddMappings(new TypeInfoMappingCollection()); }
    }

    public PgTypeInfo? GetTypeInfo(
        Type? type,
        DataTypeName? dataTypeName,
        PgSerializerOptions options)
    {
        return Mappings.Find(type, dataTypeName, options);
    }

    private static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
    {
        mappings.AddType<BitArray>(nameof(NpgsqlDbType.Bit).ToLower(),
            static (options, mapping, _) => mapping.CreateInfo(options, new BitArrayBitStringConverter(), DataFormat.Binary), true);
        return mappings;
    }
}

[Experimental("NPG9001")]
sealed class ArrayResolver : Resolver, IPgTypeInfoResolver
{
    [field: AllowNull, MaybeNull]
    private new TypeInfoMappingCollection Mappings =>
        field ??= AddMappings(new TypeInfoMappingCollection(base.Mappings));

    public new PgTypeInfo? GetTypeInfo(
        Type? type,
        DataTypeName? dataTypeName,
        PgSerializerOptions options)
    {
        return Mappings.Find(type, dataTypeName, options);
    }

    private static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
    {
        mappings.AddArrayType<BitArray>(nameof(NpgsqlDbType.Bit).ToLower());
        return mappings;
    }
}