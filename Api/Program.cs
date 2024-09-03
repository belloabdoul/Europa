using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Api.DatabaseRepository.Implementations;
using Api.DatabaseRepository.Interfaces;
using Api.Implementations.Common;
using Api.Implementations.DuplicatesByHash;
using Api.Implementations.SimilarAudios;
using Api.Implementations.SimilarImages.ImageHashGenerators;
using Api.Implementations.SimilarImages.ImageProcessors;
using Core.Entities;
using Core.Entities.Redis;
using Core.Interfaces;
using Core.Interfaces.Common;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using FluentValidation;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using NetVips;
using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Giflib;
using PhotoSauce.NativeCodecs.Libheif;
using PhotoSauce.NativeCodecs.Libjpeg;
using PhotoSauce.NativeCodecs.Libjxl;
using PhotoSauce.NativeCodecs.Libpng;
using PhotoSauce.NativeCodecs.Libwebp;
using StackExchange.Redis;

namespace Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        var services = builder.Services;

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<FileSearchType>());
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<FileType>());
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        const string apiCorsPolicy = "ApiCorsPolicy";

        // Add services to the container.
        services.AddCors(options => options.AddPolicy(apiCorsPolicy, corsPolicyBuilder =>
        {
            corsPolicyBuilder
                .WithOrigins("http://localhost:4200")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }));

        services.AddScoped<IValidator<SearchParameters>, SearchParametersValidator>();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // Add signalR
        services.AddSignalR(hubConnection => { hubConnection.EnableDetailedErrors = true; });
        builder.Services.Configure<JsonHubProtocolOptions>(o =>
        {
            o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });

        // Create index on database if not done
        var redis = ConnectionMultiplexer.Connect("localhost");
        services.AddSingleton(redis.GetDatabase());
        services.AddHostedService<RedisService>();

        // Initialize FFmpeg
        var current = AppDomain.CurrentDomain.BaseDirectory;
        var ffmpegPath = Path.Combine(current, "FFmpeg", "bin", "x64");
        DynamicallyLoadedBindings.LibrariesPath = ffmpegPath;
        DynamicallyLoadedBindings.Initialize();

        // Register images identifiers
        services.AddSingleton<IFileTypeIdentifier, MagicScalerImageProcessor>();
        services.AddSingleton<IFileTypeIdentifier, LibRawImageProcessor>();
        services.AddSingleton<IFileTypeIdentifier, LibVipsImageProcessor>();

        // Register main thumbnail generators : these are to be used for libRaw only
        services.AddSingleton<IMainThumbnailGenerator, MagicScalerImageProcessor>();
        services.AddSingleton<IMainThumbnailGenerator, LibVipsImageProcessor>();

        // Register thumbnail generators
        services.AddSingleton<IThumbnailGenerator, MagicScalerImageProcessor>();
        services.AddSingleton<IThumbnailGenerator, LibRawImageProcessor>();
        services.AddSingleton<IThumbnailGenerator, LibVipsImageProcessor>();
        
        // Register dependency resolver for fileType identifier depending on file types
        services.AddSingleton<FileTypeIdentifierResolver>(serviceProvider => searchType =>
        {
            return serviceProvider.GetServices<IFileTypeIdentifier>().Where(service =>
                service.GetAssociatedSearchType() == searchType).ToList();
        });

        // Register directory reader
        services.AddSingleton<IDirectoryReader, DirectoryReader>();

        // Dependencies for finding duplicates by cryptographic hash.
        services.AddSingleton<IHashGenerator, HashGenerator>();

        // Dependencies for finding similar audio files.
        services.AddSingleton<IAudioHashGenerator, AudioHashGenerator>();

        // Dependencies for finding similar image files.
        services.AddSingleton<IImageHash, PerceptualHash>();
        services.AddSingleton<IDbHelpers, DbHelpers>();

        services.AddSingleton<ISearchTypeImplementationFactory, SearchTypeImplementationFactory>();
        var app = builder.Build();

        // Configure the HTTP request pipeline.
        // if (app.Environment.IsDevelopment())
        // {
        //     app.UseSwagger();
        //     app.UseSwaggerUI(swaggerUiOptions => swaggerUiOptions.ConfigObject.AdditionalItems["syntaxHighlight"] =
        //         new Dictionary<string, object>
        //         {
        //             ["activated"] = false
        //         });
        // }

        NetVips.NetVips.Concurrency = 1;
        Cache.Max = 0;
        Cache.MaxMem = 0;
        Cache.MaxFiles = 0;

        Environment.SetEnvironmentVariable("VIPS_DISC_THRESHOLD", "0");

        CodecManager.Configure(codecs =>
        {
            codecs.UseGiflib();
            codecs.UseLibheif();
            codecs.UseLibjpeg();
            codecs.UseLibjxl();
            codecs.UseLibpng();
            codecs.UseLibwebp();
        });

        app.UseCors(apiCorsPolicy);

        app.UseAuthorization();

        var todosApi = app.MapGroup("/duplicates");

        todosApi.MapPost("/findDuplicates", async (SearchParameters searchParameters,
            IValidator<SearchParameters> searchParametersValidator, IDirectoryReader directoryReader,
            ISearchTypeImplementationFactory searchTypeImplementationFactory,
            CancellationToken cancellationToken = default) =>
        {
            var validationResult = await searchParametersValidator.ValidateAsync(searchParameters, cancellationToken);

            if (!validationResult.IsValid)
            {
                return Results.BadRequest(validationResult.ToDictionary());
            }

            var hypotheticalDuplicates =
                await directoryReader.GetAllFilesFromFolderAsync(searchParameters, cancellationToken);

            var searchImplementation =
                searchTypeImplementationFactory.GetSearchImplementation(searchParameters.FileSearchType!.Value,
                    searchParameters.DegreeOfSimilarity ?? 0);

            GC.Collect(2, GCCollectionMode.Default, true, true);

            var duplicatesGroups =
                await searchImplementation.FindSimilarFilesAsync(hypotheticalDuplicates, cancellationToken);

            return Results.Ok(duplicatesGroups.ToResponseDto());
        });

        app.MapHub<NotificationHub>("/notifications", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.AllowStatefulReconnects = true;
        });

        app.Run();
    }
}

[JsonSerializable(typeof(SearchParameters))]
[JsonSerializable(typeof(BadRequest))]
[JsonSerializable(typeof(IDictionary<string, string[]>))]
[JsonSerializable(typeof(Ok))]
[JsonSerializable(typeof(DuplicatesResponse))]
[JsonSerializable(typeof(Notification))]
[JsonSerializable(typeof(ImagesGroup))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(Similarity))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}