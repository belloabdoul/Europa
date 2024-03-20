using API.Common.Entities;
using API.Implementations.Common;
using API.Implementations.DuplicatesByHash;
using API.Implementations.SimilarAudios;
using API.Implementations.SimilarImages;
using API.Interfaces.Common;
using API.Interfaces.DuplicatesByHash;
using API.Interfaces.SimilarAudios;
using API.Interfaces.SimilarImages;
using Database.Implementations;
using Database.Interfaces;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using StackExchange.Redis;
using System.Text.Json.Serialization;

namespace Europa
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var config = builder.Configuration;
            var services = builder.Services;
            var apiCorsPolicy = "ApiCorsPolicy";

            // Add services to the container.
            services.AddCors(options => options.AddPolicy(apiCorsPolicy, builder =>
            {
                builder
                .SetIsOriginAllowed(host => host.Equals("http://localhost:4200") ? true : false)
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
            services.AddSignalR();

            // Initialize FFmpeg
            var current = AppDomain.CurrentDomain.BaseDirectory;
            var ffmpegPath = Path.Combine(current, "FFmpeg", "bin", "x64");
            DynamicallyLoadedBindings.LibrariesPath = ffmpegPath;
            DynamicallyLoadedBindings.Initialize();

            // Dependency for all or most features
            services.AddSingleton<IDirectoryReader, DirectoryReader>();
            services.AddSingleton<IFileReader, FileReader>();

            // Dependencies for finding duplicates by cryptographic hash.
            services.AddSingleton<IHashGenerator, HashGenerator>();
            services.AddSingleton<IDuplicateByHashFinder, DuplicateByHashFinder>();

            // Dependency for identifying the file's type.
            services.AddSingleton<IFileTypeIdentifier, FileTypeIdentifier>();

            // Dependencies for finding similar audio files.
            services.AddSingleton<IAudioHashGenerator, AudioHashGenerator>();
            services.AddSingleton<ISimilarAudiosFinder, SimilarAudiosFinder>();

            // Redis connection
            RedisConnection redisConnection = new();
            config.GetSection("RedisConnection").Bind(redisConnection);
            services.AddSingleton<IConnectionMultiplexer>(option =>
            {
                var connection = ConnectionMultiplexer.Connect(new ConfigurationOptions
                {
                    EndPoints = { $"{redisConnection.Host}:{redisConnection.Port}" },
                    AbortOnConnectFail = false,
                    Ssl = redisConnection.IsSSL,
                    Password = redisConnection.Password,
                    AllowAdmin = redisConnection.AllowAdmin,
                    SyncTimeout = 30000
                });

                return connection;
            });
            // Dependencies for finding similar image files.
            services.AddSingleton<IDbHelpers, DbHelpers>();
            services.AddSingleton<IImageHashGenerator, ImageHashGenerator>();
            services.AddSingleton<ISimilarImagesFinder, SimilarImageFinder>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(config => config.ConfigObject.AdditionalItems["syntaxHighlight"] = new Dictionary<string, object>
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
        }
    }
}
