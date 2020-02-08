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
using Faction.Common.Models;
using Faction.Common.Messages;

namespace Faction.Core.Handlers
{
  public class UpdatePayloadEventHandler : IEventHandler<UpdatePayload>
  {
    public string apiUrl = "http://api:5000/api/v1/payload";
    private readonly IEventBus _eventBus;
    private static FactionRepository _taskRepository;

    public UpdatePayloadEventHandler(IEventBus eventBus, FactionRepository taskRepository)
    {
      _eventBus = eventBus; // Inject the EventBus into this Handler to Publish a message, insert AppDbContext here for DB Access
      _taskRepository = taskRepository;
    }

    public async Task Handle(UpdatePayload updatePayload, string replyTo, string correlationId)
    {
      Console.WriteLine($"[i] Got New Payload Message.");
      // Decode and Decrypt AgentTaskResponse
      Payload payload = _taskRepository.GetPayload(updatePayload.Id);
      payload.Enabled = updatePayload.Enabled;
      payload.Visible = updatePayload.Visible;
      payload.Jitter = updatePayload.Jitter;
      payload.BeaconInterval = updatePayload.BeaconInterval;
      
      // This is a hack to keep expiration date from getting set to a default date
      if (updatePayload.ExpirationDate.HasValue) {
        int diff = DateTime.Compare(updatePayload.ExpirationDate.Value, new DateTime(2001, 01, 01));
        if (diff > 0 ) {
          payload.ExpirationDate = updatePayload.ExpirationDate;
        }
      }
      _taskRepository.Update(payload.Id, payload);

      PayloadUpdated payloadUpdated = new PayloadUpdated();
      payloadUpdated.Success = true;
      payloadUpdated.Payload = payload;
      _eventBus.Publish(payloadUpdated, replyTo, correlationId);
    }
  }
}