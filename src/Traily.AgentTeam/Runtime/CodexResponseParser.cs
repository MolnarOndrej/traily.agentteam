using System.Text.Json;

namespace Traily.AgentTeam.Runtime;

public sealed record CodexParsedResponse(
    bool Success,
    string Output);

public sealed class CodexResponseParser
{
    public CodexParsedResponse Parse(string jsonLines)
    {
        ArgumentNullException.ThrowIfNull(jsonLines);

        var completed = false;
        var failed = false;
        string? finalMessage = null;

        using var reader = new StringReader(jsonLines);

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);

            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var type))
            {
                continue;
            }

            switch (type.GetString())
            {
                case "turn.completed":
                    completed = true;
                    break;

                case "turn.failed":
                    failed = true;
                    break;

                case "item.completed":
                    finalMessage = GetAgentMessage(root)
                        ?? finalMessage;
                    break;
            }
        }

        if (failed || !completed ||
            string.IsNullOrWhiteSpace(finalMessage))
        {
            return new CodexParsedResponse(
                false,
                "Codex did not return a completed agent response.");
        }

        return new CodexParsedResponse(
            true,
            finalMessage);
    }

    private static string? GetAgentMessage(
        JsonElement root)
    {
        if (!root.TryGetProperty("item", out var item))
        {
            return null;
        }

        if (!item.TryGetProperty("type", out var itemType) ||
            itemType.GetString() != "agent_message")
        {
            return null;
        }

        if (!item.TryGetProperty("text", out var text))
        {
            return null;
        }

        return text.GetString();
    }
}