using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

using Faction.Common.Backend.Database;
using Faction.Common.Backend.EventBus;
using Faction.Common.Backend.EventBus.Abstractions;
using Faction.Common.Models;
using Faction.Common.Messages;

namespace Faction.Core.Handlers
{
  public class UpdateTransportEventHandler : IEventHandler<UpdateTransport>
  {
    private readonly IEventBus _eventBus;
    private static FactionRepository _taskRepository;

    public UpdateTransportEventHandler(IEventBus eventBus, FactionRepository taskRepository)
    {
      _eventBus = eventBus; // Inject the EventBus into this Handler to Publish a message, insert AppDbContext here for DB Access
      _taskRepository = taskRepository;
    }

    public async Task Handle(UpdateTransport updateTransport, string replyTo, string correlationId)
    {
      Console.WriteLine($"[i] Updating Transport..");
      Transport transport = _taskRepository.GetTransport(updateTransport.Id);
      transport.Name = updateTransport.Name;
      transport.TransportType = updateTransport.TransportType;
      transport.Visible = updateTransport.Visible;
      transport.Enabled = updateTransport.Enabled;
      transport.Configuration = updateTransport.Configuration;
      transport.Guid = updateTransport.Guid;
      if (!transport.Visible) {
        transport.Enabled = false;
      }

      ApiKey apiKey = _taskRepository.GetApiKey(transport.ApiKeyId.Value);

      if (!transport.Enabled) {
        apiKey.Enabled = false;
        _taskRepository.Update(apiKey.Id, apiKey);
      }
      else if (!apiKey.Enabled) {
        apiKey.Enabled = true;
        _taskRepository.Update(apiKey.Id, apiKey);
      }
      
      transport = _taskRepository.Update(transport.Id, transport);

      TransportUpdated transportUpdated = new TransportUpdated();
      transportUpdated.Success = true;
      transportUpdated.Transport = transport;
      _eventBus.Publish(transportUpdated, replyTo, correlationId);
    }
  }
}