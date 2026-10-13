using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Peeko.Core;

/// <summary>
/// Same JSON shape the macOS app writes (Swift's synthesized enum coding):
/// {"pending":{}}, {"inProgress":{"progress":0.5}}, {"done":{"at":"2026-01-01T10:00:00Z"}}, {"failed":{}}.
/// </summary>
[JsonConverter(typeof(TaskStateConverter))]
public abstract record TaskState
{
    public sealed record Pending : TaskState;
    public sealed record InProgress(double Progress) : TaskState;
    public sealed record Done(DateTimeOffset At) : TaskState;
    public sealed record Failed : TaskState;

    public bool IsDone => this is Done;
}

public static class IsoDate
{
    public static string Format(DateTimeOffset d) =>
        d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}

/// <summary>Writes dates the way Swift's .iso8601 strategy reads them (UTC, no fractional seconds).</summary>
public sealed class IsoDateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        IsoDate.Parse(r.GetString()!);

    public override void Write(Utf8JsonWriter w, DateTimeOffset v, JsonSerializerOptions o) =>
        w.WriteStringValue(IsoDate.Format(v));
}

public sealed class TaskStateConverter : JsonConverter<TaskState>
{
    public override TaskState Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("status must be an object");
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "pending": return new TaskState.Pending();
                case "failed": return new TaskState.Failed();
                case "inProgress":
                    return new TaskState.InProgress(p.Value.GetProperty("progress").GetDouble());
                case "done":
                    return new TaskState.Done(IsoDate.Parse(p.Value.GetProperty("at").GetString()!));
            }
        }
        throw new JsonException("unknown status");
    }

    public override void Write(Utf8JsonWriter w, TaskState v, JsonSerializerOptions o)
    {
        w.WriteStartObject();
        switch (v)
        {
            case TaskState.Pending:
                w.WriteStartObject("pending"); w.WriteEndObject(); break;
            case TaskState.Failed:
                w.WriteStartObject("failed"); w.WriteEndObject(); break;
            case TaskState.InProgress ip:
                w.WriteStartObject("inProgress"); w.WriteNumber("progress", ip.Progress); w.WriteEndObject(); break;
            case TaskState.Done d:
                w.WriteStartObject("done"); w.WriteString("at", IsoDate.Format(d.At)); w.WriteEndObject(); break;
        }
        w.WriteEndObject();
    }
}
