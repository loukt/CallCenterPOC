namespace ContactCenterPOC.Services.Tools
{
    /// <summary>
    /// Lightweight descriptor for an agent tool definition used to create Foundry FunctionTool objects.
    /// </summary>
    public record AgentToolDefinition(string Name, string Description, string ParametersJson);
}
