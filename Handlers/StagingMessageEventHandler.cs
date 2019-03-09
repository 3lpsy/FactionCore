using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

using Faction.Common;
using Faction.Common.Backend.Database;
using Faction.Common.Backend.EventBus.Abstractions;
using Faction.Common.Models;

using Faction.Core.Objects;

namespace Faction.Core.Handlers
{
  public class StagingMessageEventHandler : IEventHandler<StagingMessage>
  {
    private readonly IEventBus _eventBus;
    private static FactionRepository _taskRepository;

    public StagingMessageEventHandler(IEventBus eventBus, FactionRepository taskRepository)
    {
      _eventBus = eventBus; // Inject the EventBus into this Handler to Publish a message, insert AppDbContext here for DB Access
      _taskRepository = taskRepository;
    }

    public async Task Handle(StagingMessage stagingMessage, string replyTo, string correlationId)
    {
      Console.WriteLine($"[i] Got StagingMessage Message.");
      // Decode and Decrypt AgentTaskResponse
      stagingMessage.Payload = _taskRepository.GetPayload(stagingMessage.PayloadName);
      stagingMessage.Received = DateTime.UtcNow;
      _taskRepository.Add(stagingMessage);
    
      // Decrypt Message from Agent
      string decryptedMessage = Crypto.Decrypt(stagingMessage);
      Console.WriteLine($"Got response {decryptedMessage}");
      
      // Process taskResults
      // TODO: Probably a better way to check if the message is blank.
      if ((decryptedMessage != "[]") || (!String.IsNullOrEmpty(decryptedMessage))) {
        Agent agent = JsonConvert.DeserializeObject<Agent>(decryptedMessage);
        agent.Name = Utility.GenerateSecureString(12);
        agent.AesPassword = Utility.GenerateSecureString(32);
        agent.InitialCheckin = DateTime.UtcNow;
        agent.LastCheckin = DateTime.UtcNow;
        agent.BeaconInterval = stagingMessage.Payload.BeaconInterval;
        agent.Jitter = stagingMessage.Payload.Jitter;
        agent.AgentType = _taskRepository.GetAgentType(stagingMessage.Payload.AgentTypeId);
        agent.AgentTypeId = stagingMessage.Payload.AgentType.Id;
        agent.Payload = stagingMessage.Payload;

        _taskRepository.Add(agent);
        _eventBus.Publish(agent);

        // Create Agent tasks to setup agent
        List<OutboundTask> stagingTasks = new List<OutboundTask>();

        AgentTask agentNameTask = new AgentTask();
        agentNameTask.AgentId = agent.Id;
        agentNameTask.Action = "SET";
        agentNameTask.Command = $"Name:{agent.Name}";
        _taskRepository.Add(agentNameTask);
        stagingTasks.Add(new OutboundTask(agent.Name, agentNameTask));

        AgentTask passwordTask = new AgentTask();
        passwordTask.AgentId = agent.Id;
        passwordTask.Action = "SET";
        passwordTask.Command = $"Password:{agent.AesPassword}";
        _taskRepository.Add(passwordTask);
        stagingTasks.Add(new OutboundTask(agent.Name, passwordTask));

        AgentTask beaconTask = new AgentTask();
        beaconTask.AgentId = agent.Id;
        beaconTask.Action = "SET";
        beaconTask.Command = $"BeaconInterval:{agent.BeaconInterval.ToString()}";
        _taskRepository.Add(beaconTask);
        stagingTasks.Add(new OutboundTask(agent.Name, beaconTask));

        AgentTask jitterTask = new AgentTask();
        jitterTask.AgentId = agent.Id;
        jitterTask.Action = "SET";
        jitterTask.Command = $"Jitter:{agent.Jitter.ToString()}";
        _taskRepository.Add(jitterTask);
        stagingTasks.Add(new OutboundTask(agent.Name, jitterTask));

        AgentTask payloadNameTask = new AgentTask();
        payloadNameTask.AgentId = agent.Id;
        payloadNameTask.Action = "SET";
        payloadNameTask.Command = $"PayloadName:null";
        _taskRepository.Add(payloadNameTask);
        stagingTasks.Add(new OutboundTask(agent.Name, payloadNameTask));

        AgentTask stagerIdTask = new AgentTask();
        stagerIdTask.AgentId = agent.Id;
        stagerIdTask.Action = "SET";
        stagerIdTask.Command = $"StagingId:null";
        _taskRepository.Add(stagerIdTask);
        stagingTasks.Add(new OutboundTask(agent.Name, stagerIdTask));

        // Convert outbound message to json and encrypt with the staging message password
        string jsonOutboundMessage = JsonConvert.SerializeObject(stagingTasks);
        Dictionary<string, string> encCommand = Crypto.Encrypt(jsonOutboundMessage, agent.Id, stagingMessage.Payload.Key);

        // Create a StagingResponse object with the seralized/encrypted message contents
        StagingResponse stagingResponse = new StagingResponse();
        stagingResponse.Agent = agent;
        stagingResponse.Message = encCommand["encryptedMsg"];
        stagingResponse.AgentId = agent.Id;
        stagingResponse.HMAC = encCommand["hmac"];
        stagingResponse.IV = encCommand["iv"];
        stagingResponse.Sent = false;

        _taskRepository.Add(stagingResponse);

        // Package for delivery
        string stagingJson = JsonConvert.SerializeObject(new OutboundStagingResponse(stagingResponse));
        string encodedMessage = Convert.ToBase64String(Encoding.UTF8.GetBytes(stagingJson));
        Dictionary<string, string> outboundMessage = new Dictionary<string, string>();
        outboundMessage["AgentName"] = agent.StagingId;
        outboundMessage["Message"] = encodedMessage;
        _eventBus.Publish(outboundMessage, replyTo, correlationId);
      }
    }

    }
  }