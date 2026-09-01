using StreamDockVoicemeeter;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

AppDomain.CurrentDomain.ProcessExit += (_, _) => VoicemeeterRuntime.Dispose();
Console.CancelKeyPress += (_, _) => VoicemeeterRuntime.Dispose();

VoicemeeterPlugin? plugin = null;

try
{
    using var instanceGuard = SingleInstanceGuard.Acquire(() =>
    {
        try
        {
            plugin?.Dispose();
        }
        finally
        {
            VoicemeeterRuntime.Dispose();
            Environment.Exit(0);
        }
    });

    plugin = new VoicemeeterPlugin();
    using (plugin)
    {
        await plugin.RunAsync(args);
    }
}
catch (Exception ex)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
    await File.AppendAllTextAsync(logPath, $"[{DateTimeOffset.Now:O}] {ex}\n");
    throw;
}
