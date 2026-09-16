using System.Text.Json;

namespace MRR.Tools.FirmwareCompare;

/// <summary>
/// Compares two JSON responses (e.g. robot 1's vs robot 2's ACK/status for the same
/// command) by key-path shape rather than value, since values like battery %, robot_x/y,
/// or timestamps will legitimately differ between two robots. What matters for a firmware
/// diff is: did a field appear, disappear, or change JSON type. For fields that only exist
/// on one side, the value is included too -- that's the one case where "what does it say"
/// is exactly what you need to see (e.g. a new firmware field's value).
/// </summary>
public static class JsonShapeDiff
{
    public sealed record FieldValue(string Path, string Value);

    public sealed record Result(List<FieldValue> OnlyInA, List<FieldValue> OnlyInB, List<string> TypeMismatch)
    {
        public bool HasDifferences => OnlyInA.Count > 0 || OnlyInB.Count > 0 || TypeMismatch.Count > 0;
    }

    private readonly record struct Node(JsonValueKind Kind, string RawValue);

    public static Result Compare(string jsonA, string jsonB)
    {
        var a = new SortedDictionary<string, Node>(StringComparer.Ordinal);
        var b = new SortedDictionary<string, Node>(StringComparer.Ordinal);

        TryWalk(jsonA, a);
        TryWalk(jsonB, b);

        var onlyInA = a.Keys.Except(b.Keys).Select(k => new FieldValue(k, a[k].RawValue)).ToList();
        var onlyInB = b.Keys.Except(a.Keys).Select(k => new FieldValue(k, b[k].RawValue)).ToList();
        var typeMismatch = a.Keys.Intersect(b.Keys).Where(k => a[k].Kind != b[k].Kind).ToList();

        return new Result(onlyInA, onlyInB, typeMismatch);
    }

    private static void TryWalk(string json, SortedDictionary<string, Node> into)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, "$", into);
        }
        catch (JsonException)
        {
            into["$"] = new Node(JsonValueKind.Undefined, json); // records "not valid JSON" as its own shape
        }
    }

    private static void Walk(JsonElement el, string path, SortedDictionary<string, Node> into)
    {
        into[path] = new Node(el.ValueKind, el.GetRawText());
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                    Walk(prop.Value, $"{path}.{prop.Name}", into);
                break;
            case JsonValueKind.Array:
                // Sample only the first few elements -- arrays like aivision.objects.items
                // can vary in length run-to-run for reasons unrelated to firmware.
                int i = 0;
                foreach (var item in el.EnumerateArray())
                {
                    Walk(item, $"{path}[{i}]", into);
                    if (++i >= 3) break;
                }
                break;
        }
    }
}
