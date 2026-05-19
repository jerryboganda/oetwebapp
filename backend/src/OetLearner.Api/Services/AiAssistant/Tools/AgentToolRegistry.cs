using System;
using System.Collections.Generic;
using System.Linq;

namespace OetLearner.Api.Services.AiAssistant.Tools;

public sealed class AgentToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;

    public AgentToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IAgentTool Get(string name) => _tools[name];

    public IReadOnlyCollection<IAgentTool> All => _tools.Values.ToArray();
}
