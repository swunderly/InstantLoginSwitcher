using InstantLoginSwitcher.Core.Models;

namespace InstantLoginSwitcher.Core.Services;

public sealed class HotkeyParser
{
    private static readonly HashSet<string> ModifierTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl", "LCtrl", "RCtrl",
        "Alt", "LAlt", "RAlt",
        "Shift", "LShift", "RShift",
        "LWin", "RWin"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CONTROL"] = "Ctrl",
        ["CTRL"] = "Ctrl",
        ["LCONTROL"] = "LCtrl",
        ["RCONTROL"] = "RCtrl",
        ["LCTRL"] = "LCtrl",
        ["RCTRL"] = "RCtrl",
        ["ALT"] = "Alt",
        ["LALT"] = "LAlt",
        ["RALT"] = "RAlt",
        ["SHIFT"] = "Shift",
        ["LSHIFT"] = "LShift",
        ["RSHIFT"] = "RShift",
        ["WIN"] = "LWin",
        ["WINDOWS"] = "LWin",
        ["LWIN"] = "LWin",
        ["RWIN"] = "RWin",
        ["BS"] = "Backspace",
        ["ESC"] = "Escape",
        ["ESCAPE"] = "Escape",
        ["ENTER"] = "Enter",
        ["RETURN"] = "Enter",
        ["SPACE"] = "Space",
        ["SPACEBAR"] = "Space",
        ["DEL"] = "Delete",
        ["DELETE"] = "Delete",
        ["INS"] = "Insert",
        ["INSERT"] = "Insert",
        ["PGUP"] = "PgUp",
        ["PAGEUP"] = "PgUp",
        ["PGDN"] = "PgDn",
        ["PAGEDOWN"] = "PgDn",
        ["UP"] = "Up",
        ["DOWN"] = "Down",
        ["LEFT"] = "Left",
        ["RIGHT"] = "Right",
        ["NUMPADDOT"] = "NumpadDot",
        ["NUMPADDEL"] = "NumpadDot",
        ["NUMPADADD"] = "NumpadAdd",
        ["NUMPADSUB"] = "NumpadSub",
        ["NUMPADMULT"] = "NumpadMult",
        ["NUMPADDIV"] = "NumpadDiv"
    };

    private static readonly Dictionary<string, int[]> KnownVirtualKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = [0x11],
        ["LCtrl"] = [0xA2],
        ["RCtrl"] = [0xA3],
        ["Alt"] = [0x12],
        ["LAlt"] = [0xA4],
        ["RAlt"] = [0xA5],
        ["Shift"] = [0x10],
        ["LShift"] = [0xA0],
        ["RShift"] = [0xA1],
        ["LWin"] = [0x5B],
        ["RWin"] = [0x5C],
        ["Enter"] = [0x0D],
        ["Escape"] = [0x1B],
        ["Space"] = [0x20],
        ["Tab"] = [0x09],
        ["Backspace"] = [0x08],
        ["Delete"] = [0x2E],
        ["Insert"] = [0x2D],
        ["Home"] = [0x24],
        ["End"] = [0x23],
        ["PgUp"] = [0x21],
        ["PgDn"] = [0x22],
        ["Up"] = [0x26],
        ["Down"] = [0x28],
        ["Left"] = [0x25],
        ["Right"] = [0x27],
        ["NumLock"] = [0x90],
        ["CapsLock"] = [0x14],
        ["ScrollLock"] = [0x91],
        ["PrintScreen"] = [0x2C],
        ["Pause"] = [0x13],
        ["Numpad0"] = [0x60, 0x2D],
        ["Numpad1"] = [0x61, 0x23],
        ["Numpad2"] = [0x62, 0x28],
        ["Numpad3"] = [0x63, 0x22],
        ["Numpad4"] = [0x64, 0x25],
        ["Numpad5"] = [0x65, 0x0C],
        ["Numpad6"] = [0x66, 0x27],
        ["Numpad7"] = [0x67, 0x24],
        ["Numpad8"] = [0x68, 0x26],
        ["Numpad9"] = [0x69, 0x21],
        ["NumpadDot"] = [0x6E, 0x2E],
        ["NumpadAdd"] = [0x6B],
        ["NumpadSub"] = [0x6D],
        ["NumpadMult"] = [0x6A],
        ["NumpadDiv"] = [0x6F]
    };

    public HotkeyDefinition Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("Hotkey cannot be blank.");
        }

        var parts = input
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        if (parts.Count < 2)
        {
            throw new InvalidOperationException("Hotkey must include at least two keys (example: Ctrl+Alt+S).");
        }

        if (parts.Count > 4)
        {
            throw new InvalidOperationException("Hotkey can include at most four keys.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = new List<HotkeyToken>();
        foreach (var rawPart in parts)
        {
            var normalized = NormalizeToken(rawPart);
            if (!seen.Add(normalized))
            {
                throw new InvalidOperationException($"Hotkey contains duplicate key '{normalized}'.");
            }

            if (!TryResolveVirtualKeys(normalized, out var virtualKeys))
            {
                throw new InvalidOperationException($"Unsupported hotkey token '{rawPart}'.");
            }

            tokens.Add(new HotkeyToken
            {
                Name = normalized,
                VirtualKeys = virtualKeys
            });
        }

        if (tokens.All(token => ModifierTokens.Contains(token.Name)))
        {
            throw new InvalidOperationException("Hotkey must include at least one non-modifier key (example: Ctrl+Alt+S).");
        }

        return new HotkeyDefinition
        {
            SourceText = input,
            CanonicalText = string.Join('+', tokens.Select(token => token.Name)),
            Tokens = tokens
        };
    }

    private static string NormalizeToken(string token)
    {
        var candidate = token.Trim();
        if (Aliases.TryGetValue(candidate, out var alias))
        {
            return alias;
        }

        if (candidate.Length == 1)
        {
            var charValue = char.ToUpperInvariant(candidate[0]);
            if (char.IsLetterOrDigit(charValue))
            {
                return charValue.ToString();
            }
        }

        var upper = candidate.ToUpperInvariant();
        if (upper.StartsWith("F", StringComparison.Ordinal) &&
            int.TryParse(upper[1..], out var fn) &&
            fn >= 1 &&
            fn <= 24)
        {
            return $"F{fn}";
        }

        if (upper.StartsWith("NUMPAD", StringComparison.Ordinal) &&
            upper.Length == "NUMPAD".Length + 1 &&
            char.IsDigit(upper[^1]))
        {
            return $"Numpad{upper[^1]}";
        }

        return candidate;
    }

    private static bool TryResolveVirtualKeys(string token, out int[] virtualKeys)
    {
        if (KnownVirtualKeys.TryGetValue(token, out var known))
        {
            virtualKeys = known;
            return true;
        }

        if (token.Length == 1)
        {
            var value = token[0];
            if (char.IsLetter(value))
            {
                virtualKeys = [char.ToUpperInvariant(value)];
                return true;
            }

            if (char.IsDigit(value))
            {
                virtualKeys = [value];
                return true;
            }
        }

        if (token.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(token[1..], out var functionNumber) &&
            functionNumber >= 1 &&
            functionNumber <= 24)
        {
            virtualKeys = [0x70 + functionNumber - 1];
            return true;
        }

        virtualKeys = Array.Empty<int>();
        return false;
    }
}
