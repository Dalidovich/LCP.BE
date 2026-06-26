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
        var sharedConfigPath = Environment.GetEnvironmentVariable("SHARED_CONFIG_PATH");

        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true);

        if (!string.IsNullOrEmpty(sharedConfigPath))
            configBuilder.AddJsonFile(sharedConfigPath, optional: true);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configBuilder.Build())
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            if (!string.IsNullOrEmpty(sharedConfigPath))
                builder.Configuration.AddJsonFile(sharedConfigPath, optional: true, reloadOnChange: false);

            builder.Services.AddControllers();
            builder.Services.AddSwaggerGen();

            builder.Services.Configure<LibrarySettings>(
                builder.Configuration.GetSection(LibrarySettings.SectionName));

            builder.Services.AddSingleton<IVideoRepository, JsonVideoRepository>();
            builder.Services.AddSingleton<ITagRepository, JsonTagRepository>();
            builder.Services.AddSingleton<IProductionInfoRepository, JsonProductionInfoRepository>();
            builder.Services.AddSingleton<ISettingsRepository, JsonSettingsRepository>();
            builder.Services.AddScoped<IVideoService, VideoService>();
            builder.Services.AddScoped<ITagService, TagService>();
            builder.Services.AddScoped<IProductionInfoService, ProductionInfoService>();
            builder.Services.AddScoped<ISettingsService, SettingsService>();
            builder.Services.AddSingleton<IThumbnailService, ThumbnailService>();
            builder.Services.AddSingleton<IPreviewService, PreviewService>();
            builder.Services.AddSingleton<ISmartGroupingService, SmartGroupingService>();
            builder.Services.AddSingleton<ILibrarySyncService, LibrarySyncService>();

            builder.Services.AddHostedService<LibrarySeedService>();
            builder.Services.AddHostedService<LibrarySyncBackgroundService>();

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
