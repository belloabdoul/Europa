using System.Text.Json.Serialization;
using API.Implementations.Common;
using API.Implementations.DuplicatesByHash;
using API.Implementations.SimilarAudios;
using API.Implementations.SimilarImages;
using Core.Context;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Core.Interfaces.SimilarAudios;
using Core.Interfaces.SimilarImages;
using Database.Implementations;
using Database.Interfaces;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using Microsoft.EntityFrameworkCore;
using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Giflib;
using PhotoSauce.NativeCodecs.Libheif;
using PhotoSauce.NativeCodecs.Libjpeg;
using PhotoSauce.NativeCodecs.Libjxl;
using PhotoSauce.NativeCodecs.Libpng;
using PhotoSauce.NativeCodecs.Libwebp;

namespace API
{
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
                    .SetIsOriginAllowed(host => host.Equals("http://localhost:4200"))
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            }));

            services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
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
            services.AddSingleton<IDirectoryReader, DirectoryReader>();
            services.AddSingleton<IFileReader, FileReader>();

            // Dependencies for finding duplicates by cryptographic hash.
            services.AddSingleton<IHashGenerator, HashGenerator>();
            services.AddSingleton<IDuplicateByHashFinder, DuplicateByHashFinder>();
            
            // Dependency for identifying the file's type.
            CodecManager.Configure(codecs =>
            {            
                codecs.UseLibpng();
                codecs.UseLibheif();
                codecs.UseLibjpeg();
                codecs.UseLibjxl();
                codecs.UseLibwebp();
                codecs.UseGiflib();
            });
            services.AddSingleton<IFileTypeIdentifier, ImageIdentifier>();

            // Dependencies for finding similar audio files.
            services.AddSingleton<IAudioHashGenerator, AudioHashGenerator>();
            services.AddSingleton<ISimilarAudiosFinder, SimilarAudiosFinder>();

            builder.Services.AddPooledDbContextFactory<SimilarityContext>(Options);
            // Dependencies for finding similar image files.
            services.AddScoped<IImageHashGenerator, ImageHashGenerator>();
            services.AddScoped<IDbHelpers, DbHelpers>();
            services.AddScoped<ISimilarImagesFinder, SimilarImageFinder>();

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var provider = scope.ServiceProvider;
                var contextFactory = provider.GetRequiredService<IDbContextFactory<SimilarityContext>>();
                using var context = contextFactory.CreateDbContext();
                context.Database.Migrate();
            }

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
            void Options(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseNpgsql(builder.Configuration.GetConnectionString("SimilarityContext"), o => o.UseVector()).UseSnakeCaseNamingConvention().EnableDetailedErrors().EnableSensitiveDataLogging();
        }
    }
}