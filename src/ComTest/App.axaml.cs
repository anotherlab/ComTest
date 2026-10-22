using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ComTest.Core;

namespace ComTest;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = LaunchOptions.Parse(desktop.Args ?? []);
            var store = new ConfigurationStore(options.ConfigPath ?? ConfigurationStore.DefaultPath);
            desktop.MainWindow = new MainWindow(new MainViewModel(new SerialSession(), store, options.Profile, options.Error));
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public sealed record LaunchOptions(string? ConfigPath, string Profile, string? Error = null)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? path = null;
        var profile = "Default";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--config" or "--profile") || i + 1 == args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                return new(null, "Default", "Usage: ComTest [--config path/to/ComTest.json] [--profile name]");
            if (args[i] == "--config") path = args[++i];
            else profile = args[++i];
        }
        try { if (path is not null) path = Path.GetFullPath(path); }
        catch (Exception ex) { return new(null, profile, $"Invalid configuration path: {ex.Message}"); }
        return new(path, profile);
    }
}
