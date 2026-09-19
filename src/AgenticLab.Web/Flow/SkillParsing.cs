using System.Text.Json;

namespace AgenticLab.Web.Flow;

/// <summary>
/// Parsing helpers for the harness Skills box: discovering the workspace's skill catalogue from a
/// captured llm-request payload, and detecting which skill a ReadSkill tool-call is loading. Pure.
/// </summary>
internal static class SkillParsing
{
    /// <summary>
    /// Extracts the workspace's skill catalogue from the captured llm-request payload by reading the
    /// <c>&lt;skills&gt;</c> block that the harness injects into the instructions (each entry is
    /// "- name: description").
    /// </summary>
    public static IReadOnlyList<SkillChip> ParseKnownSkills(string data)
    {
        string? instructions;
        try
        {
            using var document = JsonDocument.Parse(data);
            instructions = document.RootElement.TryGetProperty("instructions", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return Array.Empty<SkillChip>();
        }

        if (string.IsNullOrEmpty(instructions))
        {
            return Array.Empty<SkillChip>();
        }

        var open = instructions.IndexOf("<skills>", StringComparison.OrdinalIgnoreCase);
        var close = instructions.IndexOf("</skills>", StringComparison.OrdinalIgnoreCase);
        if (open < 0 || close <= open)
        {
            return Array.Empty<SkillChip>();
        }

        var block = instructions[(open + "<skills>".Length)..close];
        var skills = new List<SkillChip>();
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var entry = line[2..];
            var separator = entry.IndexOf(':');
            var name = (separator >= 0 ? entry[..separator] : entry).Trim();
            var description = separator >= 0 ? entry[(separator + 1)..].Trim() : string.Empty;
            if (name.Length > 0)
            {
                skills.Add(new SkillChip(name, description));
            }
        }

        return skills;
    }

    /// <summary>
    /// Returns the known skill a ReadSkill tool-call is loading, or null when the call is not a skill load.
    /// Matches by a known skill name appearing in the event text, so it works regardless of arg formatting.
    /// </summary>
    public static string? LoadedSkillFor(FlowEvent flowEvent, IEnumerable<SkillChip> knownSkills)
    {
        var text = flowEvent.Data ?? flowEvent.Label;
        if (text.IndexOf("ReadSkill", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        return knownSkills
            .Select(s => s.Name)
            .FirstOrDefault(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
}
