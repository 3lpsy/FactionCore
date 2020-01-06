using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json;

using Faction.Common;
using Faction.Common.Backend.Database;
using Faction.Common.Backend.EventBus.Abstractions;
using Faction.Common.Messages;
using Faction.Common.Models;


namespace Faction.Core.Handlers
{
  public class NewPayloadEventHandler : IEventHandler<NewPayload>
  {
    private readonly IEventBus _eventBus;
    private static FactionRepository _taskRepository;

    public NewPayloadEventHandler(IEventBus eventBus, FactionRepository taskRepository)
    {
      _eventBus = eventBus; // Inject the EventBus into this Handler to Publish a message, insert AppDbContext here for DB Access
      _taskRepository = taskRepository;
    }

    public async Task Handle(NewPayload newPayload, string replyTo, string correlationId)
    {
      Console.WriteLine($"[i] Got New Payload Message.");
      Payload payload = new Payload();
      payload.AgentType = _taskRepository.GetAgentType(newPayload.AgentTypeId);
      payload.AgentTransportType = _taskRepository.GetAgentTransportType(newPayload.AgentTransportTypeId);
      payload.Transport = _taskRepository.GetTransport(newPayload.TransportId);
      payload.AgentTypeArchitectureId = newPayload.ArchitectureId;
      payload.AgentTypeFormatId = newPayload.FormatId;
      payload.AgentTypeVersionId = newPayload.VersionId;
      payload.AgentTypeConfigurationId = newPayload.AgentTypeConfigurationId;
      payload.AgentTypeOperatingSystemId = newPayload.OperatingSystemId;
      payload.Debug = newPayload.Debug;

      payload.Name = newPayload.Name;
      payload.Description = newPayload.Description;
      payload.Jitter = newPayload.Jitter;
      payload.BeaconInterval = newPayload.BeaconInterval;
      payload.ExpirationDate = newPayload.ExpirationDate;
      payload.BuildToken = newPayload.BuildToken;

      payload.Created = DateTime.UtcNow;
      payload.Enabled = true;
      payload.Visible = true;
      payload.Built = false;
      payload.Key = Utility.GenerateSecureString(32);
      payload.LanguageId = payload.AgentType.LanguageId;
      _taskRepository.Add(payload);
      _eventBus.Publish(payload, replyTo, correlationId);
      
     if (payload.AgentType.Development)
     {
       DevPayloadCreated devPayloadCreated = new DevPayloadCreated();
       devPayloadCreated.Success = true;
       devPayloadCreated.StagingKey = payload.Key;
       devPayloadCreated.Jitter = payload.Jitter;
       devPayloadCreated.BeaconInterval = payload.BeaconInterval;
       devPayloadCreated.ExpirationDate = payload.ExpirationDate;
       _eventBus.Publish(devPayloadCreated, replyTo, correlationId);
     }
     else
     {
       NewPayloadBuild newPayloadBuild = new NewPayloadBuild();
       newPayloadBuild.PayloadId = payload.Id;
       newPayloadBuild.AgentTypeId = payload.AgentTypeId;
       _eventBus.Publish(newPayloadBuild, replyTo, correlationId);
     }
    }
  }
}