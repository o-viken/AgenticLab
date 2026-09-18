namespace TheSeries.AiService.Application.Skills;

/// <summary>
/// Resolves a skill name supplied by the model to one of the skills discovered in the workspace. The
/// model is asked to use a skill's exact name, but it may paraphrase or change case, so matching is
/// forgiving: an exact (case-insensitive) match is preferred, falling back to a unique partial match.
/// Registered as a singleton; it is stateless and operates on the list it is given.
/// </summary>
public sealed class SkillMatcher
{
    /// <summary>
    /// Finds the skill that best matches <paramref name="query"/> within <paramref name="skills"/>.
    /// Prefers an exact, case-insensitive name match; otherwise accepts a partial match only when it is
    /// unambiguous (exactly one skill contains, or is contained by, the query).
    /// </summary>
    /// <param name="skills">The skills available in the workspace.</param>
    /// <param name="query">The skill name the model asked for.</param>
    /// <param name="match">The resolved skill when one is found.</param>
    /// <returns><c>true</c> when a skill was resolved; otherwise <c>false</c>.</returns>
    internal bool TryMatch(IReadOnlyList<SkillDefinition> skills, string query, out SkillDefinition match)
    {
        match = null!;
        if (skills.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var trimmed = query.Trim();

        var exact = skills.FirstOrDefault(s => s.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            match = exact;
            return true;
        }

        var partial = skills
            .Where(s =>
                s.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (partial.Count == 1)
        {
            match = partial[0];
            return true;
        }

        return false;
    }
}
