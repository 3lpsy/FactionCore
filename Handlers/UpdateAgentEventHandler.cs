using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

using Faction.Common.Backend.Database;
using Faction.Common.Backend.EventBus;
using Faction.Common.Backend.EventBus.Abstractions;
using Faction.Common.Messages;
using Faction.Common.Models;

namespace Faction.Core.Handlers
{
  public class UpdateAgentEventHandler : IEventHandler<UpdateAgent>
  {
    private readonly IEventBus _eventBus;
    private static FactionRepository _taskRepository;

    public UpdateAgentEventHandler(IEventBus eventBus, FactionRepository taskRepository)
    {
      _eventBus = eventBus; // Inject the EventBus into this Handler to Publish a message, insert AppDbContext here for DB Access
      _taskRepository = taskRepository;
    }

    public async Task Handle(UpdateAgent updateAgent, string replyTo, string correlationId)
    {
      Console.WriteLine($"[i] Got Update Agent Message.");
      Agent agent = _taskRepository.GetAgent(updateAgent.Id);
      agent.Name = updateAgent.Name;
      agent.Visible = updateAgent.Visible;
      _taskRepository.Update(agent.Id, agent);

      AgentUpdated agentUpdated = new AgentUpdated();
      agentUpdated.Success = true;
      agentUpdated.Agent = agent;
      _eventBus.Publish(agentUpdated, replyTo, correlationId);
    }
  }
}