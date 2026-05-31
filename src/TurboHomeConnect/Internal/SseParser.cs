using System.Text;

namespace TurboHomeConnect.Internal;

/// <summary>One frame from a <c>text/event-stream</c> response.</summary>
internal sealed record ServerSentEvent(
    string Data,
    string? EventType,
    string? Id,
    TimeSpan? Retry);

/// <summary>
/// Minimal SSE frame reader. Reads one event at a time off a <see cref="StreamReader"/> wrapping
/// the HTTP response body. Matches the <a href="https://html.spec.whatwg.org/multipage/server-sent-events.html#parsing-an-event-stream">
/// WHATWG event-stream parsing rules</a>: <c>data</c> accumulates with newlines, <c>:</c>-prefixed
/// lines are comments, an empty line dispatches the event, trailing newline on accumulated data is stripped.
/// </summary>
internal static class SseParser
{
    /// <summary>
    /// Reads the next event off the stream. Returns <c>null</c> on EOF.
    /// </summary>
    public static async Task<ServerSentEvent?> ReadEventAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var data = new StringBuilder();
        string? eventType = null;
        string? id = null;
        TimeSpan? retry = null;
        var hasData = false;
        var sawAnyField = false;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                // EOF — only emit if we'd accumulated something already.
                return sawAnyField ? Build() : null;
            }

            if (line.Length == 0)
            {
                if (!sawAnyField)
                {
                    // Empty line between events without content; keep waiting.
                    continue;
                }
                return Build();
            }

            if (line[0] == ':')
            {
                // Comment line. SSE keep-alives often look like ": ping".
                sawAnyField = true;
                continue;
            }

            sawAnyField = true;

            var colon = line.IndexOf(':');
            string fieldName;
            string fieldValue;
            if (colon < 0)
            {
                fieldName = line;
                fieldValue = string.Empty;
            }
            else
            {
                fieldName = line[..colon];
                var start = colon + 1;
                if (start < line.Length && line[start] == ' ')
                {
                    start++;
                }
                fieldValue = start < line.Length ? line[start..] : string.Empty;
            }

            switch (fieldName)
            {
                case "data":
                    if (hasData)
                    {
                        data.Append('\n');
                    }
                    data.Append(fieldValue);
                    hasData = true;
                    break;
                case "event":
                    eventType = fieldValue;
                    break;
                case "id" when !fieldValue.Contains('\0'):
                    id = fieldValue;
                    break;
                case "retry" when int.TryParse(fieldValue, out var ms):
                    retry = TimeSpan.FromMilliseconds(ms);
                    break;
            }
        }

        ServerSentEvent Build()
        {
            var payload = data.ToString();
            if (payload.Length > 0 && payload[^1] == '\n')
            {
                payload = payload[..^1];
            }
            return new ServerSentEvent(payload, eventType ?? "message", id, retry);
        }
    }
}
