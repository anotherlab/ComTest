using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComTest.Core;

public sealed class ConfigurationStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public string FilePath { get; } = Path.GetFullPath(filePath);

    public static string DefaultPath => Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComTest", "ComTest.json");

    public async Task<AppProfile?> LoadAsync(string profileName, CancellationToken cancellationToken = default)
    {
        ValidateName(profileName);
        if (!File.Exists(FilePath)) return null;
        var config = await ReadAsync(cancellationToken);
        if (!config.Profiles.TryGetValue(profileName.Trim(), out var profile)) return null;
        if (profile is null) throw new InvalidDataException("The saved profile is empty.");
        profile.Validate();
        return profile;
    }

    public async Task SaveAsync(string profileName, AppProfile profile, CancellationToken cancellationToken = default)
    {
        ValidateName(profileName);
        profile.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Lock the entire read/merge/replace operation across independent app processes.
        await using var fileLock = await AcquireLockAsync(cancellationToken);
        var config = File.Exists(FilePath) ? await ReadAsync(cancellationToken) : new Configuration();
        config.Profiles[profileName.Trim()] = profile;
        var temp = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(config, JsonOptions), cancellationToken);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private async Task<Configuration> ReadAsync(CancellationToken cancellationToken)
    {
        // Delete sharing permits atomic replacement while another instance reads the old file.
        await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        var config = await JsonSerializer.DeserializeAsync<Configuration>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("ComTest.json is empty.");
        if (config.SchemaVersion != 1 || config.Profiles is null)
            throw new InvalidDataException("ComTest.json has an unsupported format (expected SchemaVersion 1 and Profiles).");
        return config;
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            throw new ArgumentException("Enter a profile name of 1–80 characters.");
    }

    private sealed class Configuration
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, AppProfile> Profiles { get; set; } = new(StringComparer.Ordinal);
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalSections { get; set; }
    }
}
