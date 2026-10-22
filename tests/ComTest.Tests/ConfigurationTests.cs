using System.Text.Json;
using ComTest.Core;
using Xunit;

namespace ComTest.Tests;

public sealed class ConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ComTest.Tests", Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_directory, "ComTest.json");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static AppProfile Profile(string port) => new() { SerialPort = new() { PortName = port } };

    [Fact]
    public async Task ConcurrentInstancesPreserveTheirNamedSections()
    {
        var a = new ConfigurationStore(ConfigPath);
        var b = new ConfigurationStore(ConfigPath);
        await Task.WhenAll(a.SaveAsync("Port A", Profile("COM3"), Token), b.SaveAsync("Port B", Profile("/dev/cu.usbserial"), Token));
        Assert.Equal(Profile("COM3"), await a.LoadAsync("Port A", Token));
        Assert.Equal(Profile("/dev/cu.usbserial"), await b.LoadAsync("Port B", Token));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(ConfigPath, Token));
        Assert.True(json.RootElement.GetProperty("Profiles").GetProperty("Port A").TryGetProperty("SerialPort", out _));
        Assert.True(json.RootElement.GetProperty("Profiles").GetProperty("Port A").TryGetProperty("Sending", out _));
        Assert.True(json.RootElement.GetProperty("Profiles").GetProperty("Port A").TryGetProperty("Display", out _));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task InvalidConfigurationIsReportedAndNeverOverwritten()
    {
        Directory.CreateDirectory(_directory);
        const string invalid = "{ invalid json";
        await File.WriteAllTextAsync(ConfigPath, invalid, Token);
        var store = new ConfigurationStore(ConfigPath);
        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync("Default", Token));
        await Assert.ThrowsAsync<JsonException>(() => store.SaveAsync("Default", Profile("COM3"), Token));
        Assert.Equal(invalid, await File.ReadAllTextAsync(ConfigPath, Token));
    }

    [Fact]
    public async Task MissingProfilesAndInvalidSettingsAreHandled()
    {
        var store = new ConfigurationStore(ConfigPath);
        Assert.Null(await store.LoadAsync("Default", Token));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("Default", Profile(""), Token));
        await store.SaveAsync("Default", Profile("COM3"), Token);
        Assert.Null(await store.LoadAsync("Other", Token));
        var invalid = Profile("COM3") with { SerialPort = new() { PortName = "COM3", BaudRate = 0 } };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("Default", invalid, Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
