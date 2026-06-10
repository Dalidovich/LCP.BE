using LCP.API.BackgroundServices;
using LCP.API.Middleware;
using LCP.BLL.Interfaces;
using LCP.BLL.Services;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.DAL.Repositories;
using Serilog;

namespace LCP.API;

public class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .Build())
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddSwaggerGen();

            builder.Services.Configure<LibrarySettings>(
                builder.Configuration.GetSection(LibrarySettings.SectionName));

            builder.Services.AddSingleton<IVideoRepository, JsonVideoRepository>();
            builder.Services.AddSingleton<ITagRepository, JsonTagRepository>();
            builder.Services.AddScoped<IVideoService, VideoService>();
            builder.Services.AddScoped<ITagService, TagService>();
            builder.Services.AddScoped<IThumbnailService, ThumbnailService>();
            builder.Services.AddScoped<IPreviewService, PreviewService>();

            builder.Services.AddHostedService<LibrarySeedService>();
            builder.Services.AddHostedService<LibrarySyncService>();

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            builder.Host.UseSerilog();


            var app = builder.Build();

            app.UseMiddleware<ExceptionHandlingMiddleware>();

            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseCors();

            app.UseAuthorization();

            app.MapControllers();

            Log.Information("Application starting");
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
