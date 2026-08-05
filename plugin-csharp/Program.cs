using StreamDockVoicemeeter;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

AppDomain.CurrentDomain.ProcessExit += (_, _) => VoicemeeterRuntime.Dispose();
Console.CancelKeyPress += (_, _) => VoicemeeterRuntime.Dispose();

try
{
    using var plugin = new VoicemeeterPlugin();
    await plugin.RunAsync(args);
}
catch (Exception ex)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
    await File.AppendAllTextAsync(logPath, $"[{DateTimeOffset.Now:O}] {ex}\n");
    throw;
}
