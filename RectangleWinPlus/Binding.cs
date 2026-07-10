using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace RectangleWinPlus;

[Flags]
public enum Mods
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A shortcut: a set of modifiers plus one or two ordinary keys that must be held together.
/// Two keys is what makes <c>Ctrl+Win+Left+Up</c> expressible; the Win32 RegisterHotKey API
/// cannot represent it, which is why this app drives a low-level keyboard hook instead.
/// </summary>
public sealed class Binding : IEquatable<Binding>
{
    public const int MaxKeys = 2;

    public Mods Mods { get; }

    /// <summary>Virtual-key codes, in canonical order (horizontal arrow first, then vertical).</summary>
    public IReadOnlyList<int> Keys { get; }

    public Binding(Mods mods, IEnumerable<int> keys)
    {
        Mods = mods;
        Keys = keys.Distinct().OrderBy(KeyRank).ThenBy(vk => vk).Take(MaxKeys).ToArray();
        if (Keys.Count == 0) throw new ArgumentException("A binding needs at least one key.", nameof(keys));
        Signature = MakeSignature(mods, Keys);
    }

    /// <summary>Stable identity for dictionary lookups: same modifiers, same key set.</summary>
    public string Signature { get; }

    public static string MakeSignature(Mods mods, IEnumerable<int> keys) =>
        $"{(int)mods}:{string.Join(",", keys.Distinct().OrderBy(vk => vk))}";

    // Arrows sort into reading order so Ctrl+Win+Left+Up never renders as "Up+Left".
    private static int KeyRank(int vk) => vk switch
    {
        VK.Left => 0,
        VK.Right => 1,
        VK.Up => 2,
        VK.Down => 3,
        _ => 10,
    };

    public bool Equals(Binding? other) => other is not null && Signature == other.Signature;
    public override bool Equals(object? obj) => Equals(obj as Binding);
    public override int GetHashCode() => Signature.GetHashCode();

    public override string ToString()
    {
        var sb = new StringBuilder();
        // Ctrl, Alt, Shift, Win, matching how the defaults read: "Ctrl+Win+Left+Up".
        if (Mods.HasFlag(Mods.Ctrl)) sb.Append("Ctrl+");
        if (Mods.HasFlag(Mods.Alt)) sb.Append("Alt+");
        if (Mods.HasFlag(Mods.Shift)) sb.Append("Shift+");
        if (Mods.HasFlag(Mods.Win)) sb.Append("Win+");
        sb.Append(string.Join("+", Keys.Select(KeyName)));
        return sb.ToString();
    }

    public static string Describe(Mods mods, IEnumerable<int> keys)
    {
        var sb = new StringBuilder();
        if (mods.HasFlag(Mods.Ctrl)) sb.Append("Ctrl+");
        if (mods.HasFlag(Mods.Alt)) sb.Append("Alt+");
        if (mods.HasFlag(Mods.Shift)) sb.Append("Shift+");
        if (mods.HasFlag(Mods.Win)) sb.Append("Win+");
        var ordered = keys.Distinct().OrderBy(KeyRank).ThenBy(vk => vk).Select(KeyName).ToArray();
        sb.Append(ordered.Length > 0 ? string.Join("+", ordered) : "…");
        return sb.ToString();
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out Binding? binding, out string? error)
    {
        binding = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text)) { error = "empty shortcut"; return false; }

        var mods = Mods.None;
        var keys = new List<int>();

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Mods.Ctrl; continue;
                case "alt" or "option" or "opt": mods |= Mods.Alt; continue;
                case "shift": mods |= Mods.Shift; continue;
                case "win" or "windows" or "meta" or "cmd" or "super": mods |= Mods.Win; continue;
            }

            if (!TryParseKey(raw, out int vk)) { error = $"unknown key '{raw}'"; return false; }
            if (keys.Contains(vk)) { error = $"key '{raw}' listed twice"; return false; }
            keys.Add(vk);
        }

        if (keys.Count == 0) { error = "no key, only modifiers"; return false; }
        if (keys.Count > MaxKeys) { error = $"at most {MaxKeys} keys per shortcut"; return false; }
        if (mods == Mods.None) { error = "needs at least one of Ctrl, Alt, Shift, Win"; return false; }

        binding = new Binding(mods, keys);
        return true;
    }

    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = VK.Left,
        ["right"] = VK.Right,
        ["up"] = VK.Up,
        ["down"] = VK.Down,
        ["enter"] = VK.Return,
        ["return"] = VK.Return,
        ["esc"] = VK.Escape,
        ["escape"] = VK.Escape,
        ["space"] = VK.Space,
        ["tab"] = VK.Tab,
        ["backspace"] = VK.Back,
        ["delete"] = VK.Delete,
        ["del"] = VK.Delete,
        ["insert"] = VK.Insert,
        ["ins"] = VK.Insert,
        ["home"] = VK.Home,
        ["end"] = VK.End,
        ["pageup"] = VK.PageUp,
        ["pgup"] = VK.PageUp,
        ["pagedown"] = VK.PageDown,
        ["pgdn"] = VK.PageDown,
        ["numadd"] = 0x6B,
        ["numsubtract"] = 0x6D,
        ["nummultiply"] = 0x6A,
        ["numdivide"] = 0x6F,
        ["numdecimal"] = 0x6E,
    };

    private static bool TryParseKey(string name, out int vk)
    {
        if (NamedKeys.TryGetValue(name, out vk)) return true;

        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z') { vk = c; return true; }
            if (c is >= '0' and <= '9') { vk = c; return true; }
        }

        if (name.StartsWith("num", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(3), out int digit) && digit is >= 0 and <= 9)
        {
            vk = 0x60 + digit;
            return true;
        }

        if (name.StartsWith('F') && int.TryParse(name.AsSpan(1), out int fn) && fn is >= 1 and <= 24)
        {
            vk = 0x6F + fn;
            return true;
        }

        vk = 0;
        return false;
    }

    public static string KeyName(int vk) => vk switch
    {
        VK.Left => "Left",
        VK.Right => "Right",
        VK.Up => "Up",
        VK.Down => "Down",
        VK.Return => "Enter",
        VK.Escape => "Esc",
        VK.Space => "Space",
        VK.Tab => "Tab",
        VK.Back => "Backspace",
        VK.Delete => "Delete",
        VK.Insert => "Insert",
        VK.Home => "Home",
        VK.End => "End",
        VK.PageUp => "PageUp",
        VK.PageDown => "PageDown",
        0x6A => "NumMultiply",
        0x6B => "NumAdd",
        0x6D => "NumSubtract",
        0x6E => "NumDecimal",
        0x6F => "NumDivide",
        >= 'A' and <= 'Z' => ((char)vk).ToString(),
        >= '0' and <= '9' => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => "Num" + (vk - 0x60),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        _ => $"0x{vk:X2}",
    };
}

/// <summary>Virtual-key codes used by name elsewhere in the app.</summary>
internal static class VK
{
    public const int Back = 0x08;
    public const int Tab = 0x09;
    public const int Return = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int PageUp = 0x21;
    public const int PageDown = 0x22;
    public const int End = 0x23;
    public const int Home = 0x24;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Insert = 0x2D;
    public const int Delete = 0x2E;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;
}
