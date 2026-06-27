using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace LCP.Startup;

public class Program
{
    public static async Task Main(string[] args)
    {
        var (cfg, backendPath, frontendPath) = await LoadConfigAsync();
        var backendDir = ResolveDir(backendPath, "LCP.API", "BackendPath");
        var frontendDir = ResolveDir(frontendPath, null, "FrontendPath");
        var sharedConfigPath = Path.Combine(backendPath, "appsettings.json");
        await WriteSharedConfigAsync(cfg, sharedConfigPath);

        var killed = false;
        var exitTcs = new TaskCompletionSource();

        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            killed = true;
            exitTcs.TrySetResult();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!killed) killed = true;
        };

        while (!killed)
        {
            var backend = StartBackend(backendDir, sharedConfigPath);
            var frontend = StartFrontend(frontendDir);

            Console.WriteLine($"Backend PID: {backend.Id}   Frontend PID: {frontend.Id}");
            Console.WriteLine("Press Ctrl+C to stop all processes.");

            var completed = await Task.WhenAny(exitTcs.Task, WaitForExitAsync(backend), WaitForExitAsync(frontend));

            if (killed) break;

            Console.WriteLine("A process exited unexpectedly. Stopping all and restarting...");
            KillAll(backend, frontend);
            await Task.Delay(3000);
        }
    }

    private static Process StartBackend(string backendDir, string sharedConfigPath)
    {
        Console.WriteLine("Starting backend...");
        return StartProcess(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --configuration Release --project \"{backendDir}\"",
            EnvironmentVariables =
            {
                ["SHARED_CONFIG_PATH"] = sharedConfigPath
            }
        }, $"dotnet run --configuration Release --project \"{backendDir}\"");
    }

    private static Process StartFrontend(string frontendDir)
    {
        Console.WriteLine("Starting frontend...");
        return StartProcess(new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = "/c npm start",
            WorkingDirectory = frontendDir
        }, $"npm start in \"{frontendDir}\"");
    }

    private static async Task<(IConfigurationRoot Config, string BackendPath, string FrontendPath)> LoadConfigAsync()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "StartupSetting.json");

        if (!File.Exists(configPath))
        {
            var defaultConfig = new { BackendPath = "", FrontendPath = "" };
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, json);
            Console.Error.WriteLine($"StartupSetting.json created at:\n{configPath}\nEdit it with your paths and restart.");
            Environment.Exit(1);
        }

        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("StartupSetting.json", optional: false, reloadOnChange: false);

        Console.WriteLine(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

#if DEBUG
        configBuilder.AddUserSecrets<Program>();
#endif

        var config = configBuilder.Build();

        var backendPath = config["BackendPath"] ?? "";
        var frontendPath = config["FrontendPath"] ?? "";

        if (string.IsNullOrWhiteSpace(backendPath) || string.IsNullOrWhiteSpace(frontendPath))
            Fail("BackendPath or FrontendPath is empty in StartupSetting.json or user secrets.");

        return (config, backendPath, frontendPath);
    }

    private static async Task WriteSharedConfigAsync(IConfigurationRoot config, string path)
    {
        var ls = new Dictionary<string, object?>
        {
            ["LibraryRootPath"] = config["LibrarySettings:LibraryRootPath"] ?? "",
            ["Password"] = config["LibrarySettings:Password"] ?? "",
            ["SmartVideoGrouping"] = bool.TryParse(config["LibrarySettings:SmartVideoGrouping"], out var sg) && sg
        };

        var root = new Dictionary<string, object?> { ["LibrarySettings"] = ls };
        var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        Console.WriteLine($"Wrote shared config: {path}");
    }

    private static string ResolveDir(string path, string? subDir, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            Fail($"{label} is empty in StartupSetting.json.");

        var fullPath = Path.GetFullPath(subDir is not null ? Path.Combine(path, subDir) : path);

        if (!Directory.Exists(fullPath))
            Fail($"{label} directory not found: \"{fullPath}\"");

        return fullPath;
    }

    private static Process StartProcess(ProcessStartInfo info, string description)
    {
        info.UseShellExecute = false;
        Console.WriteLine($"Starting: {description}");
        return Process.Start(info)!;
    }

    private static void Fail(string message)
    {
        Console.Error.WriteLine(message);
        Environment.Exit(1);
    }

    private static async Task WaitForExitAsync(Process process)
    {
        try { await process.WaitForExitAsync(); }
        catch { }
    }

    private static void KillAll(Process a, Process b)
    {
        try { a.Kill(true); } catch { }
        try { b.Kill(true); } catch { }
    }

}
