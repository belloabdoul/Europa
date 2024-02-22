using API.Common.Implementations;
using API.Common.Interfaces;
using API.Features.FindDuplicatesByHash.Implementations;
using API.Features.FindDuplicatesByHash.Interfaces;

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

            builder.Services.AddSingleton<IHashGenerator, HashGenerator>();
            builder.Services.AddSingleton<IDuplicateFinderByHash, DuplicateFinderByHash>();
            builder.Services.AddSingleton<IDirectoryReader, DirectoryReader>();

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
