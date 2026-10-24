using System.Text.Json;

namespace BoostOS.Core;

/// <summary>The few things a player may want to change. Kept short on purpose.</summary>
public sealed record AppSettings
{
    public string Language { get; init; } = "he";
    public bool Sounds { get; init; } = true;
    public bool Rumble { get; init; } = true;
    public bool StartFullscreen { get; init; } = true;
    public bool TrackMatches { get; init; } = true;
    /// <summary>Game Mode turns on by itself when a game starts (from BoostOS, or from anywhere while the recorder runs).</summary>
    public bool GameModeOnPlay { get; init; } = true;
    /// <summary>The background recorder starts with Windows, so every game is recorded however it was started.</summary>
    public bool BackgroundRecorder { get; init; } = true;
    /// <summary>The game on the big first tile ("epic:Sugar", "steam:730"…). Null: Rocket League, or the first game.</summary>
    public string? FavoriteGame { get; init; }

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoostOS");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static AppSettings Load(string? path = null)
    {
        try
        {
            var file = path ?? FilePath;
            return File.Exists(file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Json) ?? new() : new();
        }
        catch
        {
            return new(); // a broken settings file should never stop the app from starting
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
    }
}
