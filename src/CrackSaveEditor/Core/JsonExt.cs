using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrackSaveEditor.Core;

public static class JsonExt
{
    /// <summary>Compact output like Unity's JsonUtility, without escaping non-ASCII names.</summary>
    public static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static readonly JsonSerializerOptions Pretty = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentSize = 4,
    };

    public static string ToCompact(this JsonNode node) => node.ToJsonString(Compact);

    public static double Num(this JsonNode? node, string key, double fallback = 0)
    {
        if (node?[key] is JsonValue v)
        {
            if (v.TryGetValue(out double d)) return d;
            if (v.TryGetValue(out bool b)) return b ? 1 : 0;
        }
        return fallback;
    }

    public static int Int(this JsonNode? node, string key, int fallback = 0) =>
        node?[key] is JsonValue v && v.TryGetValue(out int i) ? i : (int)Math.Round(node.Num(key, fallback));

    public static long Long(this JsonNode? node, string key, long fallback = 0)
    {
        if (node?[key] is JsonValue v)
        {
            if (v.TryGetValue(out long l)) return l;
            if (v.TryGetValue(out ulong u)) return unchecked((long)u);
        }
        return fallback;
    }

    public static bool Bool(this JsonNode? node, string key, bool fallback = false)
    {
        if (node?[key] is JsonValue v)
        {
            if (v.TryGetValue(out bool b)) return b;
            if (v.TryGetValue(out int i)) return i != 0;
        }
        return fallback;
    }

    public static string Str(this JsonNode? node, string key, string fallback = "") =>
        node?[key] is JsonValue v && v.TryGetValue(out string? s) ? s : fallback;

    public static JsonArray Arr(this JsonNode? node, string key)
    {
        if (node?[key] is JsonArray a) return a;
        var created = new JsonArray();
        if (node is JsonObject o) o[key] = created;
        return created;
    }

    public static JsonObject Obj(this JsonNode? node, string key)
    {
        if (node?[key] is JsonObject o) return o;
        var created = new JsonObject();
        if (node is JsonObject parent) parent[key] = created;
        return created;
    }

    public static IEnumerable<JsonObject> Objects(this JsonArray array) => array.OfType<JsonObject>();

    /// <summary>Human readable value for UI/diff output.</summary>
    public static string Describe(this JsonNode? node) => node switch
    {
        null => "null",
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonArray a => $"[{a.Count}]",
        JsonObject o => $"{{{o.Count}}}",
        _ => node.ToJsonString(Compact),
    };
}
