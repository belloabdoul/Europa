using System.Text.Json;
using System.Text.Json.Serialization;
using API.Implementations.Common;
using API.Implementations.DuplicatesByHash;
using API.Implementations.SimilarAudios;
using API.Implementations.SimilarImages;
using API.Implementations.SimilarImages.ImageHashGenerators;
using API.Implementations.SimilarImages.ImageIdentifiers;
using Core.Entities;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Core.Interfaces.SimilarAudios;
using Core.Interfaces.SimilarImages;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using FluentValidation;

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
                .SetIsOriginAllowed(host => host == "http://localhost:4200")
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
        services.AddSignalR(hubConnection => { hubConnection.ClientTimeoutInterval = TimeSpan.FromHours(1); });

        // Initialize FFmpeg
        var current = AppDomain.CurrentDomain.BaseDirectory;
        var ffmpegPath = Path.Combine(current, "FFmpeg", "bin", "x64");
        DynamicallyLoadedBindings.LibrariesPath = ffmpegPath;
        DynamicallyLoadedBindings.Initialize();

        // Dependency for all or most features
        services.AddScoped<IFileTypeIdentifier, LibVipsImageIdentifier>();
        services.AddScoped<IDirectoryReader, DirectoryReader>();
        services.AddScoped<IFileReader, FileReader>();

        // Dependencies for finding duplicates by cryptographic hash.
        services.AddTransient<IHashGenerator, HashGenerator>();
        services.AddScoped<IDuplicateByHashFinder, DuplicateByHashFinder>();

        // Dependencies for finding similar audio files.
        services.AddScoped<IAudioHashGenerator, AudioHashGenerator>();
        services.AddScoped<ISimilarAudiosFinder, SimilarAudiosFinder>();

        // Dependencies for finding similar image files.
        services.AddTransient<IImageHash, DifferenceHash>();
        // services.AddScoped<IDbHelpers, DbHelpers>();
        services.AddScoped<ISimilarImagesFinder, SimilarImageFinder>();

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
        
        app.UseHttpsRedirection();

        app.UseCors(apiCorsPolicy);

        app.UseAuthorization();

        app.MapControllers();

        app.MapHub<NotificationHub>("/notifications");

        app.Run();
    }
}