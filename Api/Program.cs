using System.Text.Json;
using Api.Client;
using Api.Client.Repositories;
using Api.Controllers;
using Api.Implementations.Commons;
using Api.Implementations.DuplicatesByHash;
using Api.Implementations.SimilarAudios;
using Api.Implementations.SimilarImages;
using Api.Implementations.SimilarImages.ImageHashGenerators;
using Api.Implementations.SimilarImages.ImageProcessors;
using Core.Entities.Files;
using Core.Entities.SearchParameters;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarAudios;
using Core.Interfaces.SimilarImages;
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
using Sdcb.LibRaw;

namespace Api;

public class Program
{
    public static void Main(string[] args)
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

        // Initialize FFmpeg
        var current = AppDomain.CurrentDomain.BaseDirectory;
        var ffmpegPath = Path.Combine(current, "ffmpeg");
        DynamicallyLoadedBindings.LibrariesPath = ffmpegPath;
        DynamicallyLoadedBindings.Initialize();

        // Register directory reader
        services.AddScoped<IDirectoryReader, DirectoryReader>();

        // Register file type's identifiers for audio search
        services.AddKeyedScoped<IFileTypeIdentifier, MagicScalerImageProcessor>(FileSearchType.Audios);
        services.AddKeyedScoped<IFileTypeIdentifier, LibRawImageProcessor>(FileSearchType.Audios);
        services.AddKeyedScoped<IFileTypeIdentifier, LibVipsImageProcessor>(FileSearchType.Audios);
        services.AddKeyedScoped<IFileTypeIdentifier, FileTypeIdentifier>(FileSearchType.Audios);
        
        // Register file type's identifiers for image search
        services.AddKeyedScoped<IFileTypeIdentifier, MagicScalerImageProcessor>(FileSearchType.Images);
        services.AddKeyedScoped<IFileTypeIdentifier, LibRawImageProcessor>(FileSearchType.Images);
        services.AddKeyedScoped<IFileTypeIdentifier, LibVipsImageProcessor>(FileSearchType.Images);
        services.AddKeyedScoped<IFileTypeIdentifier, FileTypeIdentifier>(FileSearchType.Images);

        // Register main thumbnail generators : these are to be used for libRaw only
        services.AddKeyedScoped<IMainThumbnailGenerator, MagicScalerImageProcessor>(ProcessedImageType.Jpeg);
        services.AddKeyedScoped<IMainThumbnailGenerator, LibVipsImageProcessor>(ProcessedImageType.Bitmap);

        // Register thumbnail generators
        services.AddKeyedScoped<IThumbnailGenerator, MagicScalerImageProcessor>(FileType.MagicScalerImage);
        services.AddKeyedScoped<IThumbnailGenerator, LibRawImageProcessor>(FileType.LibRawImage);
        services.AddKeyedScoped<IThumbnailGenerator, LibVipsImageProcessor>(FileType.LibVipsImage);
        services.AddScoped<IThumbnailGeneratorResolver, ThumbnailGeneratorResolver>();

        // Dependencies for finding duplicates by cryptographic hash.
        services.AddScoped<IHashGenerator, HashGenerator>();

        // Dependencies for finding similar audio files.
        services.AddScoped<IAudioHashGenerator, AudioHashGenerator>();

        // Dependencies for finding similar image files.
        services.AddKeyedScoped<IImageHash, DifferenceHash>(PerceptualHashAlgorithm.DifferenceHash);
        services.AddKeyedScoped<IImageHash, PerceptualHash>(PerceptualHashAlgorithm.PerceptualHash);
        services.AddKeyedScoped<IImageHash>(PerceptualHashAlgorithm.BlockMeanHash,
            (_, _) => new BlockMeanHash(true));
        services.AddScoped<IImageHashResolver, ImageHashResolver>();
        
        // Dependencies for qdrant database
        services.AddSingleton<ICollectionRepository, QdrantRepository>();
        services.AddScoped<IIndexingRepository, QdrantRepository>();
        services.AddScoped<IImageInfosRepository, QdrantRepository>();
        services.AddScoped<ISimilarImagesRepository, QdrantRepository>();
        services.AddHostedService<QdrantConfig>();

        // Register similar file search implementations for hash, audio and video
        services.AddKeyedScoped<ISimilarFilesFinder, DuplicateByHashFinder>(FileSearchType.All);
        services.AddKeyedScoped<ISimilarFilesFinder, SimilarAudiosFinder>(FileSearchType.Audios);
        services.AddKeyedScoped<ISimilarFilesFinder, SimilarImageFinder>(FileSearchType.Images);

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