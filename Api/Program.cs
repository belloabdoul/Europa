using System.Text.Json;
using Api.DatabaseRepository.Implementations;
using Api.DatabaseRepository.Interfaces;
using Api.Implementations.Common;
using Api.Implementations.DuplicatesByHash;
using Api.Implementations.SimilarAudios;
using Api.Implementations.SimilarImages;
using Api.Implementations.SimilarImages.ImageHashGenerators;
using Api.Implementations.SimilarImages.ImageProcessors;
using Core.Entities;
using Core.Entities.Redis;
using Core.Interfaces;
using Core.Interfaces.Common;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using FluentValidation;
using Microsoft.AspNetCore.Http.Connections;
using NetVips;
using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Giflib;
using PhotoSauce.NativeCodecs.Libheif;
using PhotoSauce.NativeCodecs.Libjpeg;
using PhotoSauce.NativeCodecs.Libjxl;
using PhotoSauce.NativeCodecs.Libpng;
using PhotoSauce.NativeCodecs.Libwebp;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using StackExchange.Redis;

namespace Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var services = builder.Services;

        services.ConfigureHttpJsonOptions(options =>
        {
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
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // Add signalR
        services.AddSignalR(options => { options.EnableDetailedErrors = true; });

        // Create index on database if not done
        var redis = ConnectionMultiplexer.Connect("localhost");
        services.AddSingleton(redis.GetDatabase());
        services.AddHostedService<RedisService>();

        // Qdrant
        // var client = new QdrantClient("localhost");
        // var collectionExists = await client.CollectionExistsAsync("europa_images");
        // if (!collectionExists)
        // {
        //     await client.CreateCollectionAsync(nameof(DifferenceHash), new VectorParams { Size = DifferenceHash.HashSize, Datatype = Datatype.Uint8, Distance = Distance.Dot});
        // }
        //
        // services.AddSingleton(client);

        // Initialize FFmpeg
        var current = AppDomain.CurrentDomain.BaseDirectory;
        var ffmpegPath = Path.Combine(current, "FFmpeg", "bin", "x64");
        DynamicallyLoadedBindings.LibrariesPath = ffmpegPath;
        DynamicallyLoadedBindings.Initialize();

        // Register images identifiers
        services.AddScoped<IFileTypeIdentifier, MagicScalerImageProcessor>();
        services.AddScoped<IFileTypeIdentifier, LibRawImageProcessor>();
        services.AddScoped<IFileTypeIdentifier, LibVipsImageProcessor>();
        services.AddScoped<IFileTypeIdentifier, FileTypeIdentifier>();

        // Register main thumbnail generators : these are to be used for libRaw only
        services.AddScoped<IMainThumbnailGenerator, MagicScalerImageProcessor>();
        services.AddScoped<IMainThumbnailGenerator, LibVipsImageProcessor>();

        // Register thumbnail generators
        services.AddScoped<IThumbnailGenerator, MagicScalerImageProcessor>();
        services.AddScoped<IThumbnailGenerator, LibRawImageProcessor>();
        services.AddScoped<IThumbnailGenerator, LibVipsImageProcessor>();

        // Register directory reader
        services.AddScoped<IDirectoryReader, DirectoryReader>();

        // Dependencies for finding duplicates by cryptographic hash.
        services.AddScoped<IHashGenerator, HashGenerator>();

        // Dependencies for finding similar audio files.
        services.AddScoped<IAudioHashGenerator, AudioHashGenerator>();

        // Dependencies for finding similar image files.
        services.AddScoped<IImageHash, DifferenceHash>();
        services.AddScoped<IImageHash, PerceptualHash>();
        services.AddScoped<IImageHash, BlockMeanHash>();

        // Dependencies for redis database
        services.AddScoped<IDbHelpers, DbHelpers>();

        // Register similar file search implementations for hash, audio and video
        services.AddScoped<ISimilarFilesFinder, DuplicateByHashFinder>();
        services.AddScoped<ISimilarFilesFinder, SimilarAudiosFinder>();
        services.AddScoped<ISimilarFilesFinder, SimilarImageFinder>();

        services.AddScoped<ISearchService, SearchService>();
        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(swaggerUiOptions => swaggerUiOptions.ConfigObject.AdditionalItems["syntaxHighlight"] =
                new Dictionary<string, object>
                {
                    ["activated"] = false
                });
        }

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

        app.MapHub<NotificationHub>("/notifications", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.AllowStatefulReconnects = true;
        });

        app.UseHttpsRedirection();

        app.MapControllers();

        app.Run();
    }
}