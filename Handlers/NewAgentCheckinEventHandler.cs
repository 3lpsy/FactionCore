using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json;

using Faction.Common.Backend.Database;
using Faction.Common.Backend.EventBus;
using Faction.Common.Backend.EventBus.Abstractions;
using Faction.Common.Models;
using Faction.Common.Messages;
using Faction.Common;

namespace Faction.Core.Handlers
{
  public class NewAgentCheckinEventHandler : IEventHandler<NewAgentCheckin>
  {
    public string apiUrl = "http://api:5000/api/v1";
    private readonly IEventBus _eventBus;
    private static FactionRepository _taskRepository;

    class RecievedCheckin {
      public string TaskName;

    }

    public NewAgentCheckinEventHandler(IEventBus eventBus, FactionRepository taskRepository)
    {
      _eventBus = eventBus; // Inject the EventBus into this Handler to Publish a message, insert AppDbContext here for DB Access
      _taskRepository = taskRepository;
    }

    public async Task Handle(NewAgentCheckin agentCheckingMsg, string replyTo, string correlationId)
    {
      Console.WriteLine($"[i] Got AgentCheckin Message.");
      // Check in agent
      Agent agent = _taskRepository.GetAgent(agentCheckingMsg.AgentName);
      agent.LastCheckin = DateTime.UtcNow;
      _taskRepository.Update(agent.Id, agent);

      AgentCheckinAnnouncement agentCheckinAnnouncement = new AgentCheckinAnnouncement();
      agentCheckinAnnouncement.Id = agent.Id;
      agentCheckinAnnouncement.Received = agent.LastCheckin.Value;
      _eventBus.Publish(agentCheckinAnnouncement);

      // Decode and Decrypt AgentTaskResponse
      if (!String.IsNullOrEmpty(agentCheckingMsg.Message)) 
      {
        AgentCheckin agentCheckin = new AgentCheckin();
        agentCheckin.HMAC = agentCheckingMsg.HMAC;
        agentCheckin.IV = agentCheckingMsg.IV;
        agentCheckin.Message = agentCheckingMsg.Message;
        agentCheckin.AgentId = agent.Id;
        agentCheckin.Agent = agent;
        _taskRepository.Add(agentCheckin);

        // Decrypt Message from Agent
        string decryptedMessage = Crypto.Decrypt(agentCheckin);
        Console.WriteLine($"Got response {decryptedMessage}");
        if (!agent.Visible)
        {
          agent.Visible = true;
          _taskRepository.Update(agent.Id, agent);
        }
        // Process taskResults
        // TODO: Probably a better way to check if the message is blank.
        if ((decryptedMessage != "[]") || (!String.IsNullOrEmpty(decryptedMessage)))
        {
          List<AgentTaskUpdate> taskUpdates = JsonConvert.DeserializeObject<List<AgentTaskUpdate>>(decryptedMessage);
          foreach (AgentTaskUpdate taskUpdate in taskUpdates)
          {
            taskUpdate.AgentTask = _taskRepository.GetAgentTask(taskUpdate.TaskName);
            taskUpdate.AgentId = taskUpdate.AgentTask.AgentId;
            taskUpdate.Received = DateTime.UtcNow;

            foreach (IOC ioc in taskUpdate.IOCs) {
              ioc.UserId = taskUpdate.AgentTask.ConsoleMessage.UserId.Value;
              ioc.AgentTaskUpdateId = taskUpdate.Id;
            }
            _taskRepository.Add(taskUpdate);

            if (taskUpdate.AgentTask.Action == "LOAD" && taskUpdate.Success.Value)
            {
              AgentsModulesXref xref = new AgentsModulesXref();
              xref.AgentId = taskUpdate.AgentId;
              string languageName = taskUpdate.Agent.AgentType.Language.Name;
              string moduleName = taskUpdate.AgentTask.Command.Split(" ")[1];
              if (moduleName.Contains("/"))
              {
                languageName = moduleName.Split("/")[0];
                moduleName = moduleName.Split("/")[1];
              }
              xref.ModuleId = (_taskRepository.GetModule(moduleName, languageName)).Id;
              _taskRepository.Add(xref);
            }

            if (taskUpdate.Type == "File" && !String.IsNullOrEmpty(taskUpdate.Content))
            {
              WebClient wc = new WebClient();
              FactionSettings factionSettings = Utility.GetConfiguration();
              wc.Headers[HttpRequestHeader.ContentType] = "application/json";
              string rsp = wc.UploadString($"{apiUrl}/login/", 
                $"{{\"Username\":\"{factionSettings.SYSTEM_USERNAME}\", \"Password\":\"{factionSettings.SYSTEM_PASSWORD}\"}}");
              Dictionary<string, string> responseDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(rsp);
              wc.Dispose();

              string apiKeyName = responseDict["AccessKeyId"];
              string apiSecret = responseDict["AccessSecret"];
              string uploadUrl = $"{apiUrl}/file/?token={apiKeyName}:{apiSecret}";

              Dictionary<string, string> upload = new Dictionary<string, string>();
              upload.Add("AgentName", taskUpdate.Agent.Name);
              upload.Add("FileName", taskUpdate.ContentId);
              upload.Add("FileContent", taskUpdate.Content);

              WebClient uploadClient = new WebClient();
              uploadClient.Headers[HttpRequestHeader.ContentType] = "application/json";
              string content = JsonConvert.SerializeObject(upload);
              Console.WriteLine(content);
              string uploadResponse = uploadClient.UploadString(uploadUrl, content);
            }

            ConsoleMessage consoleMessage = new ConsoleMessage();
            consoleMessage.Agent = taskUpdate.Agent;
            consoleMessage.Type = "AgentTaskResult";
            consoleMessage.AgentTask = taskUpdate.AgentTask;
            consoleMessage.AgentTaskId = taskUpdate.AgentTask.Id;
            consoleMessage.Display = taskUpdate.Message;
            _taskRepository.Add(consoleMessage);

            ConsoleMessageAnnouncement response = new ConsoleMessageAnnouncement();
            response.Success = true;
            response.Username = consoleMessage.Agent.Name;
            response.ConsoleMessage = consoleMessage;
            _eventBus.Publish(response);
          }
        }
      }

    }
  }
}