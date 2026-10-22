using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComTest.Core;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ComTest.Tests.TestApplication))]

namespace ComTest.Tests;

public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class UiTests
{
    [AvaloniaFact]
    public async Task MainWindowBindsBothBuffersAndGuardsDisconnectedActions()
    {
        var config = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ComTest.json");
        var vm = new MainViewModel(new(), new(config));
        var window = new MainWindow(vm);
        window.Show();
        Assert.True(window.FindControl<Button>("SettingsButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("OpenPortButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("ClosePortButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("SendButton")!.IsEnabled);
        vm.ApplyProfile(new() { SerialPort = new() { PortName = "COM3" } }, "Port A");
        vm.Session.InBuffer.Append([0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x0D, 0x0A]);
        vm.Session.OutBuffer.Append([0x41, 0x54, 0x0D]);
        window.RefreshBuffers();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<Button>("OpenPortButton")!.IsEnabled);
        Assert.Equal("48 65 6C 6C 6F 0D 0A", vm.Incoming.Hex);
        Assert.Equal("Hello..", vm.Incoming.Ascii);
        Assert.Equal("AT.", vm.Outgoing.Ascii);
        Capture(window, "main-window.png");
        await vm.ShutdownAsync();
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task SettingsDialogValidatesAndSavesThenLoadsSections()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ComTest.Tests", Guid.NewGuid().ToString("N"));
        var vm = new SettingsViewModel(new(Path.Combine(directory, "ComTest.json")), new(), "Port A");
        var window = new SettingsWindow(vm);
        window.Show();
        Assert.Null(vm.BuildResult());
        Assert.Contains("port", vm.Message);
        var baudRateInput = window.FindControl<ComboBox>("BaudRateInput")!;
        Assert.True(baudRateInput.IsEditable);
        Assert.Same(vm.BaudRateOptions, baudRateInput.ItemsSource);
        baudRateInput.Text = "invalid";
        Assert.Null(vm.BuildResult());
        baudRateInput.SelectedItem = "19200";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("19200", vm.BaudRate);
        baudRateInput.Text = "14400";
        Assert.Equal("14400", vm.BaudRate);
        baudRateInput.Text = "9600";
        var portInput = window.FindControl<ComboBox>("PortInput")!;
        Assert.True(portInput.IsEditable);
        Assert.Same(vm.Ports, portInput.ItemsSource);
        portInput.Text = "COM4";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("COM4", vm.PortName);
        Assert.Equal("9600", vm.BaudRate);
        await vm.SaveAsync();
        Assert.Contains("Saved", vm.Message);
        vm.PortName = "COM99";
        await vm.LoadAsync();
        Assert.Equal("COM4", vm.PortName);
        Assert.Equal("COM4", portInput.Text);
        Assert.Equal(9600, vm.BuildResult()!.Profile.SerialPort.BaudRate);
        Capture(window, "settings-window.png");
        window.Close();
        Directory.Delete(directory, true);
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("COMTEST_SCREENSHOT_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
