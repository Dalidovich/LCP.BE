using LCP.API.Authorization;
using LCP.API.BackgroundServices;
using LCP.API.Middleware;
using LCP.BLL.Interfaces;
using LCP.BLL.Services;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.DAL.Repositories;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Serilog;

namespace LCP.API;

public class Program
{
    public static void Main(string[] args)
    {
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configBuilder.Build())
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddSwaggerGen();

            builder.Services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.Cookie.Name = "lcp_session";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.Cookie.IsEssential = true;
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                    options.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };
                    options.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                });

            builder.Services.AddSingleton<IAuthorizationHandler, PasswordGateHandler>();

            builder.Services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                    .AddRequirements(new PasswordGateRequirement())
                    .Build());

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
            builder.Services.AddSingleton<IVideoProcessingService, VideoProcessingService>();
            builder.Services.AddSingleton<IMediaWarmupService, MediaWarmupService>();
            builder.Services.AddSingleton<ILibrarySyncService, LibrarySyncService>();
            builder.Services.AddSingleton<IRandomSortSeedProvider, RandomSortSeedProvider>();
            builder.Services.AddSingleton(typeof(IInfoCache<>), typeof(InfoCache<>));

            builder.Services.AddHostedService<LibraryStartupService>();

            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>();

            if (allowedOrigins == null || allowedOrigins.Length == 0)
                allowedOrigins = ["http://localhost:4200"];

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            builder.Host.UseSerilog();


            var app = builder.Build();

            app.UseMiddleware<ExceptionHandlingMiddleware>();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseCors();

            app.UseAuthentication();

            app.UseAuthorization();

            if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot")))
            {
                app.UseDefaultFiles();
                app.UseStaticFiles();
            }

            app.MapControllers();

            if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot")))
            {
                app.MapWhen(
                    context => !context.Request.Path.StartsWithSegments("/api"),
                    spa => spa.Run(async context =>
                    {
                        context.Response.ContentType = "text/html";
                        await context.Response.SendFileAsync(
                            Path.Combine(app.Environment.WebRootPath, "index.html"));
                    }));
            }

            Log.Information("Application starting");
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
