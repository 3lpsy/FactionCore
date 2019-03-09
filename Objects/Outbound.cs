using Faction.Common.Models;

namespace Faction.Core.Objects {
  public class OutboundTask
  {
    public string Name;
    public string AgentName;
    public string Action;
    public string Command;

    public OutboundTask(string AgentName, AgentTask agentTask)
    {
      this.Name = agentTask.Name;
      this.AgentName = AgentName;
      this.Action = agentTask.Action;
      this.Command = agentTask.Command;
    }
  }

  public class OutboundStagingResponse {
    public string AgentName;
    public string IV;
    public string Message;
    public string HMAC;
    public OutboundStagingResponse(StagingResponse stagingResponse) {
      this.AgentName = stagingResponse.Agent.Name;
      this.IV = stagingResponse.IV;
      this.Message = stagingResponse.Message;
      this.HMAC = stagingResponse.HMAC;
    }
  }
}