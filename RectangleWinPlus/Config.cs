using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RectangleWinPlus;

public sealed class AppConfig
{
    /// <summary>Pixels of breathing room. A full gap at the screen edge, a full gap between windows.</summary>
    public int Gap { get; set; }

    /// <summary>
    /// How long a single arrow waits to see whether a perpendicular arrow joins it, forming a
    /// quadrant chord. At 0 a half snaps the instant you press the arrow; quadrants still work,
    /// but you see the window land on the half first.
    /// </summary>
    public int ChordWindowMs { get; set; } = 120;

    public bool StartWithWindows { get; set; }

    public Dictionary<string, string> Shortcuts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // The default encoder writes "Ctrl+Win+Left". This file is meant to be edited by
        // hand, so keep '+' as '+'. Nothing here is ever interpolated into HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RectangleWinPlus");

    public static string FilePath { get; } = Path.Combine(Directory, "config.json");

    public static Dictionary<string, string> DefaultShortcuts() => new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(SnapAction.LeftHalf)] = "Ctrl+Win+Left",
        [nameof(SnapAction.RightHalf)] = "Ctrl+Win+Right",
        [nameof(SnapAction.TopHalf)] = "Ctrl+Win+Up",
        [nameof(SnapAction.BottomHalf)] = "Ctrl+Win+Down",
        [nameof(SnapAction.TopLeft)] = "Ctrl+Win+Left+Up",
        [nameof(SnapAction.TopRight)] = "Ctrl+Win+Right+Up",
        [nameof(SnapAction.BottomLeft)] = "Ctrl+Win+Left+Down",
        [nameof(SnapAction.BottomRight)] = "Ctrl+Win+Right+Down",
        [nameof(SnapAction.Maximize)] = "Ctrl+Win+Enter",
    };

    public static AppConfig CreateDefault() => new() { Shortcuts = DefaultShortcuts() };

    /// <summary>
    /// Reads the config, falling back to defaults on damage. A broken file is left on disk rather
    /// than overwritten, so a typo never silently costs the user their shortcuts.
    /// </summary>
    public static AppConfig Load(out List<string> problems)
    {
        problems = new List<string>();

        try
        {
            if (!File.Exists(FilePath))
            {
                var fresh = CreateDefault();
                fresh.Save();
                return fresh;
            }

            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions);
            if (config is null)
            {
                problems.Add("config.json was empty; using defaults.");
                return CreateDefault();
            }

            if (config.Shortcuts.Count == 0) config.Shortcuts = DefaultShortcuts();
            config.Gap = Math.Clamp(config.Gap, 0, 200);
            config.ChordWindowMs = Math.Clamp(config.ChordWindowMs, 0, 1000);
            return config;
        }
        catch (Exception ex)
        {
            problems.Add($"Could not read config.json ({ex.Message}); using defaults.");
            Log.Error("Config load failed", ex);
            return CreateDefault();
        }
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);

        // Write-then-replace, so a crash mid-write cannot leave a truncated config behind.
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Turns the on-disk strings into bindings, reporting anything it had to drop.</summary>
    public Dictionary<SnapAction, Binding> ResolveBindings(List<string> problems)
    {
        var result = new Dictionary<SnapAction, Binding>();
        var seen = new Dictionary<string, SnapAction>(StringComparer.Ordinal);

        foreach (var (name, text) in Shortcuts)
        {
            if (!Enum.TryParse<SnapAction>(name, ignoreCase: true, out var action))
            {
                problems.Add($"Unknown action '{name}' in config.json.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(text)) continue;  // deliberately unbound

            if (!Binding.TryParse(text, out var binding, out string? error))
            {
                problems.Add($"{action}: cannot read shortcut '{text}' ({error}).");
                continue;
            }

            if (seen.TryGetValue(binding.Signature, out var owner))
            {
                problems.Add($"{action}: shortcut {binding} is already used by {owner}.");
                continue;
            }

            seen[binding.Signature] = action;
            result[action] = binding;
        }

        return result;
    }
}
