using System.Text.Json.Serialization;
using API.Implementations.Common;
using API.Implementations.DuplicatesByHash;
using API.Implementations.SimilarAudios;
using API.Implementations.SimilarImages;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Core.Interfaces.SimilarAudios;
using Core.Interfaces.SimilarImages;
using Dapper;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using Microsoft.EntityFrameworkCore;
using Pgvector.Dapper;

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
        });

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
        services.AddScoped<IFileTypeIdentifier, ImageIdentifier>();
        services.AddScoped<IDirectoryReader, DirectoryReader>();
        services.AddScoped<IFileReader, FileReader>();

        // Dependencies for finding duplicates by cryptographic hash.
        services.AddTransient<IHashGenerator, HashGenerator>();
        services.AddScoped<IDuplicateByHashFinder, DuplicateByHashFinder>();

        // Dependencies for finding similar audio files.
        services.AddScoped<IAudioHashGenerator, AudioHashGenerator>();
        services.AddScoped<ISimilarAudiosFinder, SimilarAudiosFinder>();

        // Added pgvector dependency for dapper
        SqlMapper.AddTypeHandler(new VectorTypeHandler());
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // builder.Services.AddPooledDbContextFactory<SimilarityContext>(Options);
        // Dependencies for finding similar image files.
        services.AddTransient<IImageHashGenerator, ImageHashGenerator>();
        // services.AddScoped<IDbHelpers, DbHelpers>();
        services.AddScoped<ISimilarImagesFinder, SimilarImageFinder>();

        var app = builder.Build();

        // using (var scope = app.Services.CreateScope())
        // {
        //     var provider = scope.ServiceProvider;
        //     var contextFactory = provider.GetRequiredService<IDbContextFactory<SimilarityContext>>();
        //     using var context = contextFactory.CreateDbContext();
        //     context.Database.Migrate();
        // }

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

        app.UseHttpsRedirection();

        app.UseCors(apiCorsPolicy);

        app.UseAuthorization();

        app.MapControllers();

        app.MapHub<NotificationHub>("/notifications");

        app.Run();

        return;

        // Database
        void Options(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseNpgsql(builder.Configuration.GetConnectionString("SimilarityContext"), o =>
                {
                    o.UseVector();
                    o.MigrationsHistoryTable("__ef_migrations_history");
                })
                .EnableDetailedErrors().EnableSensitiveDataLogging();
            optionsBuilder.EnableThreadSafetyChecks(false);
        }
    }
}