using System.Diagnostics;
using System.Text.Json;

namespace LCP.Startup;

public class Program
{
    public static async Task Main(string[] args)
    {
        var config = await LoadConfigAsync();
        var backendDir = ResolveDir(config.BackendPath, "LCP.API", "BackendPath");
        var frontendDir = ResolveDir(config.FrontendPath, null, "FrontendPath");

        var backend = StartProcess(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --configuration Release --project \"{backendDir}\""
        }, $"dotnet run --configuration Release --project \"{backendDir}\"");

        var frontend = StartProcess(new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = "/c npm start",
            WorkingDirectory = frontendDir
        }, $"npm start in \"{frontendDir}\"");

        Console.WriteLine($"Backend PID: {backend.Id}   Frontend PID: {frontend.Id}");
        Console.WriteLine("Press Ctrl+C to stop all processes.");

        var killed = false;
        var exitTcs = new TaskCompletionSource();

        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            KillAll(backend, frontend, ref killed);
            exitTcs.TrySetResult();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!killed) KillAll(backend, frontend, ref killed);
        };

        var completed = await Task.WhenAny(exitTcs.Task, WaitForExitAsync(backend), WaitForExitAsync(frontend));

        if (completed != exitTcs.Task)
        {
            Console.WriteLine("A process exited unexpectedly. Stopping...");
            KillAll(backend, frontend, ref killed);
        }
    }

    private static async Task<StartupConfig> LoadConfigAsync()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "StartupSetting.json");

        if (!File.Exists(configPath))
            Fail("StartupSetting.json not found next to executable.");

        return JsonSerializer.Deserialize<StartupConfig>(await File.ReadAllTextAsync(configPath))
            ?? Fail<StartupConfig>("Failed to parse StartupSetting.json.");
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

    private static T Fail<T>(string message)
    {
        Console.Error.WriteLine(message);
        Environment.Exit(1);
        throw new InvalidOperationException(message);
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

    private static void KillAll(Process a, Process b, ref bool killed)
    {
        killed = true;
        try { a.Kill(true); } catch { }
        try { b.Kill(true); } catch { }
    }

    private record StartupConfig(string BackendPath, string FrontendPath);
}
