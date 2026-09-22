namespace Traily.AgentTeam.Agents;

public sealed class AgentInstructionsComposer
{
    public string Compose(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var skillInstructions = definition.Skills
            .Select(skill => $"""
                ## Skill: {skill.Id}

                {skill.Instructions}
                """);

        return $"""
            # Agent: {definition.Name}

            ## Role Instructions

            {definition.Instructions}

            ## Assigned Skills

            {string.Join(Environment.NewLine, skillInstructions)}

            Skills provide additional guidance. They do not override
            the agent's role, operating restrictions, or permissions.
            """;
    }
}