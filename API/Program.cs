using API.Common.Implementations;
using API.Common.Interfaces;
using API.Features.FindDuplicatesByHash.Implementations;
using API.Features.FindDuplicatesByHash.Interfaces;
using API.Features.FindSimilarAudios.Implementations;
using API.Features.FindSimilarAudios.Interfaces;
using API.Features.FindSimilarImages.Implementations;
using API.Features.FindSimilarImages.Interfaces;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Emy;
using SoundFingerprinting.InMemory;

namespace Europa
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Initialize FFmpeg
            var current = Environment.CurrentDirectory;
            var probe = Path.Combine("bin", "Debug", "net8.0", "FFmpeg", "bin", "x64");
            var ffmpegBinaryPath = Path.Combine(current, probe);
            DynamicallyLoadedBindings.LibrariesPath = ffmpegBinaryPath;
            DynamicallyLoadedBindings.Initialize();

            // Dependency for all features
            builder.Services.AddSingleton<IDirectoryReader, DirectoryReader>();

            // Dependencies for finding duplicates by cryptographic hash.
            builder.Services.AddSingleton<IHashGenerator, HashGenerator>();
            builder.Services.AddSingleton<IDuplicateFinderByHash, DuplicateFinderByHash>();

            // Dependency for identifying the file's type.
            builder.Services.AddSingleton<IFileTypeIdentifier, FileTypeIdentifier>();

            // Dependencies for finding similar audio files.
            builder.Services.AddSingleton<IModelService, InMemoryModelService>();
            builder.Services.AddSingleton<IAudioService, FFmpegAudioService>();
            builder.Services.AddSingleton<IAudioHashGenerator, AudioHashGenerator>();
            builder.Services.AddSingleton<ISimilarAudiosFinder, SimilarAudiosFinder>();

            // Dependencies for finding similar audio files.
            builder.Services.AddSingleton<IImageHashGenerator, ImageHashGenerator>();
            builder.Services.AddSingleton<ISimilarImagesFinder, SimilarImageFinder>();

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
