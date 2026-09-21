namespace Traily.AgentTeam.Agents;

public sealed class AgentInstructionsLoader
{
    private readonly string _rootDirectory;

    public AgentInstructionsLoader(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        _rootDirectory = Path.GetFullPath(rootDirectory);

        if (!Directory.Exists(_rootDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Traily root directory not found: {_rootDirectory}");
        }
    }

    public async Task<string> LoadAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var rolePath = ResolveRolePath(definition.InstructionsPath);

        var roleInstructions = await File.ReadAllTextAsync(
            rolePath,
            cancellationToken);

        var skillInstructions = new List<string>();

        foreach (var skillId in definition.SkillIds.Distinct())
        {
            var skillPath = ResolveSkillPath(skillId);

            var content = await File.ReadAllTextAsync(
                skillPath,
                cancellationToken);

            skillInstructions.Add($"""
                ## Skill: {skillId}

                {content}
                """);
        }

        return $"""
            # Agent: {definition.Role}

            ## Role Instructions

            {roleInstructions}

            ## Assigned Skills

            {string.Join(Environment.NewLine, skillInstructions)}

            Skills provide additional guidance. They do not override
            the agent's role, operating restrictions, or permissions.
            """;
    }


    private string ResolveRolePath(string instructionsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instructionsPath);

        var definitionsDirectory = Path.Combine(
            _rootDirectory,
            AgentFileConventions.DefinitionsDirectory);

        var fullPath = Path.GetFullPath(
            Path.Combine(_rootDirectory, instructionsPath));

        var relativePath = Path.GetRelativePath(
            definitionsDirectory,
            fullPath);

        return ResolvePath(
            definitionsDirectory,
            relativePath,
            AgentFileConventions.AgentInstructionsFileName);
    }

    private string ResolveSkillPath(string skillId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);

        if (skillId.Any(c =>
            !char.IsAsciiLetterLower(c) &&
            !char.IsAsciiDigit(c) &&
            c != '-'))
        {
            throw new ArgumentException(
                $"Invalid skill ID: {skillId}");
        }

        return ResolvePath(
            Path.Combine(_rootDirectory, AgentFileConventions.SkillsDirectory),
            Path.Combine(skillId, AgentFileConventions.SkillInstructionsFileName),
            AgentFileConventions.SkillInstructionsFileName);
    }


    private string ResolvePath(
        string allowedDirectory,
        string relativePath,
        string expectedFileName)
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(allowedDirectory, relativePath));

        var relativeToAllowed = Path.GetRelativePath(
            allowedDirectory,
            fullPath);

        if (Path.IsPathRooted(relativeToAllowed) ||
            relativeToAllowed == ".." ||
            relativeToAllowed.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Path is outside the allowed directory: {relativePath}");
        }

        if (!string.Equals(
            Path.GetFileName(fullPath),
            expectedFileName,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected an {expectedFileName} file.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Agent instruction file not found: {fullPath}",
                fullPath);
        }

        return fullPath;
    }
}