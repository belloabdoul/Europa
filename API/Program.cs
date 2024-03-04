using API.Common.Entities;
using API.Common.Implementations;
using API.Common.Interfaces;
using API.Features.FindDuplicatesByHash.Implementations;
using API.Features.FindDuplicatesByHash.Interfaces;
using API.Features.FindSimilarAudios.Implementations;
using API.Features.FindSimilarAudios.Interfaces;
using API.Features.FindSimilarImages.Implementations;
using API.Features.FindSimilarImages.Interfaces;
using Database.Implementations;
using Database.Interfaces;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using NRedisStack.Search.Literals.Enums;
using NRedisStack.Search;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Emy;
using SoundFingerprinting.InMemory;
using StackExchange.Redis;
using System.Data.Common;
using NRedisStack.RedisStackCommands;

namespace Europa
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var config = builder.Configuration;
            var services = builder.Services;

            // Add services to the container.
            services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();

            // Initialize FFmpeg
            var current = Environment.CurrentDirectory;
            var probe = Path.Combine("bin", "Debug", "net8.0", "FFmpeg", "bin", "x64");
            var ffmpegBinaryPath = Path.Combine(current, probe);
            DynamicallyLoadedBindings.LibrariesPath = ffmpegBinaryPath;
            DynamicallyLoadedBindings.Initialize();

            // Dependency for all features
            services.AddSingleton<IDirectoryReader, DirectoryReader>();

            // Dependencies for finding duplicates by cryptographic hash.
            services.AddSingleton<IHashGenerator, HashGenerator>();
            services.AddSingleton<IDuplicateFinderByHash, DuplicateFinderByHash>();

            // Dependency for identifying the file's type.
            services.AddSingleton<IFileTypeIdentifier, FileTypeIdentifier>();

            // Dependencies for finding similar audio files.
            services.AddSingleton<IModelService, InMemoryModelService>();
            services.AddSingleton<IAudioService, FFmpegAudioService>();
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

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
