using System.Text.RegularExpressions;

namespace GeminiLiveTranslate.Ui;

public sealed class RollingTextTrack
{
    private const int MaxTextLength = 6000;
    private readonly List<string> _segments = [];
    private string _active = "";
    private DateTime _activeSince = DateTime.MinValue;
    private DateTime _lastInputAt = DateTime.MinValue;

    public string Update(string text)
    {
        var incoming = Normalize(text);
        if (incoming.Length == 0) return DisplayText;

        var now = DateTime.UtcNow;
        if (_lastInputAt != DateTime.MinValue && now - _lastInputAt > TimeSpan.FromMilliseconds(2400))
        {
            CommitActive();
        }

        _lastInputAt = now;
        if (_activeSince == DateTime.MinValue) _activeSince = now;

        incoming = StripCommittedPrefix(incoming);
        if (incoming.Length > 0) _active = AppendDistinct(_active, incoming);

        var activeAge = now - _activeSince;
        if (IsTerminal(_active) || (activeAge > TimeSpan.FromMilliseconds(3600) && IsMeaningful(_active)))
        {
            CommitActive();
        }

        return DisplayText;
    }

    public void Clear()
    {
        _segments.Clear();
        _active = "";
        _activeSince = DateTime.MinValue;
        _lastInputAt = DateTime.MinValue;
    }

    public string DisplayText => TrimToMaxLength(SoftJoin(string.Join(" ", _segments), _active));

    public string ExportText => SoftJoin(string.Join(" ", _segments), _active);

    private void CommitActive()
    {
        var text = Normalize(_active);
        _active = "";
        _activeSince = DateTime.MinValue;
        if (text.Length == 0) return;

        var last = _segments.Count > 0 ? _segments[^1] : "";
        if (last.Length == 0 || Similarity(last, text) < 0.96)
        {
            var merged = last.Length > 0 ? AppendDistinct(last, text) : text;
            if (last.Length > 0 && merged != last && merged.Length <= last.Length + Math.Max(120, text.Length + 8))
            {
                _segments[^1] = merged;
            }
            else
            {
                _segments.Add(text);
            }
        }

        Prune();
    }

    private void Prune()
    {
        while (_segments.Count > 80) _segments.RemoveAt(0);
        while (Normalize(string.Join(" ", _segments)).Length > MaxTextLength && _segments.Count > 1)
        {
            _segments.RemoveAt(0);
        }
    }

    private string StripCommittedPrefix(string incoming)
    {
        var text = Normalize(incoming);
        if (text.Length == 0 || _segments.Count == 0) return text;
        var lower = text.ToLowerInvariant();

        for (var count = Math.Min(4, _segments.Count); count >= 1; count--)
        {
            var recent = Normalize(string.Join(" ", _segments.Skip(_segments.Count - count)));
            if (recent.Length == 0) continue;
            if (lower.StartsWith(recent.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return Normalize(text[recent.Length..]);
            }
        }

        var last = Normalize(_segments[^1]);
        if (last.Length > 0)
        {
            var overlap = OverlapLength(last, text);
            if (overlap >= Math.Min(24, Math.Max(1, (int)(last.Length * 0.6))))
            {
                return Normalize(text[overlap..]);
            }
        }

        return text;
    }

    private static string Normalize(string value) => Regex.Replace(value ?? "", @"\s+", " ").Trim();

    private static string AppendDistinct(string committed, string next)
    {
        var current = Normalize(committed);
        var incoming = Normalize(next);
        if (incoming.Length == 0) return current;
        if (current.Length == 0) return incoming;
        if (current.Contains(incoming, StringComparison.OrdinalIgnoreCase) && incoming.Length >= 4) return current;
        if (incoming.Contains(current, StringComparison.OrdinalIgnoreCase) && incoming.Length > current.Length) return incoming;
        if (Similarity(current, incoming) > 0.95) return current;
        var overlap = OverlapLength(current, incoming);
        var tail = overlap > 0 ? incoming[overlap..] : incoming;
        return SoftJoin(current, tail);
    }

    private static string SoftJoin(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        if (Regex.IsMatch(b, @"^[,.;:!?，。！？；：、)]")) return $"{a}{b}";
        if (Regex.IsMatch(a, @"[([{（「『《]$")) return $"{a}{b}";
        if (IsNonAsciiEnd(a) || IsNonAsciiStart(b)) return $"{a}{b}";
        return $"{a} {b}";
    }

    private static double Similarity(string a, string b)
    {
        var left = Normalize(a);
        var right = Normalize(b);
        if (left.Length == 0 || right.Length == 0) return 0;
        if (left == right) return 1;
        var shorter = left.Length < right.Length ? left : right;
        var longer = left.Length >= right.Length ? left : right;
        if (longer.Contains(shorter, StringComparison.OrdinalIgnoreCase) && shorter.Length / (double)longer.Length > 0.78) return 0.96;
        return 0;
    }

    private static int OverlapLength(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        var max = Math.Min(180, Math.Min(a.Length, b.Length));
        for (var size = max; size >= 4; size--)
        {
            if (string.Equals(a[^size..], b[..size], StringComparison.OrdinalIgnoreCase)) return size;
        }
        return 0;
    }

    private static bool IsTerminal(string text) => Regex.IsMatch(Normalize(text), @"[.!?。！？;；:]$");

    private static bool IsMeaningful(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) return false;
        if (Regex.IsMatch(normalized, @"[\u3040-\u30ff\u3400-\u9fff\uf900-\ufaff\uac00-\ud7af]")) return normalized.Length >= 6;
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3 || normalized.Length >= 18;
    }

    private static string TrimToMaxLength(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length <= MaxTextLength) return normalized;
        var trimmed = normalized[^MaxTextLength..];
        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace >= 0 ? trimmed[(firstSpace + 1)..].Trim() : trimmed;
    }

    private static bool IsNonAsciiEnd(string text) => text.Length > 0 && text[^1] > 127;
    private static bool IsNonAsciiStart(string text) => text.Length > 0 && text[0] > 127;
}
