using System.Text.Json;
using System.Text.Json.Serialization;
using API.Implementations.Common;
using API.Implementations.DuplicatesByHash;
using API.Implementations.SimilarAudios;
using API.Implementations.SimilarImages.ImageHashGenerators;
using API.Implementations.SimilarImages.ImageProcessors;
using Core.Entities;
using Core.Entities.Redis;
using Core.Interfaces;
using Core.Interfaces.Common;
using Database.Implementations;
using Database.Interfaces;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using FluentValidation;
using MessagePack;
using Microsoft.AspNetCore.Http.Connections;
using NetVips;
using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Giflib;
using PhotoSauce.NativeCodecs.Libheif;
using PhotoSauce.NativeCodecs.Libjpeg;
using PhotoSauce.NativeCodecs.Libjxl;
using PhotoSauce.NativeCodecs.Libpng;
using PhotoSauce.NativeCodecs.Libwebp;
using Redis.OM;

namespace API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var services = builder.Services;
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

        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        services.AddScoped<IValidator<SearchParameters>, SearchParametersValidator>();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddSignalR(hubConnection => { hubConnection.EnableDetailedErrors = true; })
            .AddMessagePackProtocol(options => { options.SerializerOptions = MessagePackSerializerOptions.Standard; });

        // Create index on database if not done
        services.AddSingleton(new RedisConnectionProvider(builder.Configuration["RedisConnectionString"]!));
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
        services.AddSingleton<IImageHash, DifferenceHash>();
        services.AddSingleton<IDbHelpers, DbHelpers>();

        services.AddSingleton<ISearchTypeImplementationFactory, SearchTypeImplementationFactory>();
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

        app.UseHttpsRedirection();

        app.UseCors(apiCorsPolicy);

        app.UseAuthorization();

        app.MapControllers();

        app.MapHub<NotificationHub>("/notifications", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.AllowStatefulReconnects = true;
        });

        app.Run();
    }
}