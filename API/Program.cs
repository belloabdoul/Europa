using API.Common.Implementations;
using API.Common.Interfaces;
using API.Features.FindDuplicatesByHash.Implementations;
using API.Features.FindDuplicatesByHash.Interfaces;
using API.Features.FindSimilarAudios.Implementations;
using API.Features.FindSimilarAudios.Interfaces;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using SoundFingerprinting;
using SoundFingerprinting.Emy;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Media;

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

            builder.Services.AddSingleton<IDirectoryReader, DirectoryReader>();

            builder.Services.AddSingleton<IHashGenerator, HashGenerator>();
            builder.Services.AddSingleton<IDuplicateFinderByHash, DuplicateFinderByHash>();

            builder.Services.AddSingleton<IFileTypeIdentifier, FileTypeIdentifier>();
            builder.Services.AddSingleton<IModelService, InMemoryModelService>();
            builder.Services.AddSingleton<IMediaService, FFmpegAudioService>();
            builder.Services.AddSingleton<IAudioHashGenerator, AudioHashGenerator>();
            builder.Services.AddSingleton<ISimilarAudiosFinder, SimilarAudiosFinder>();


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
