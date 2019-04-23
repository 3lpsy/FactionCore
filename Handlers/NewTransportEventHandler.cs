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
  public class NewTransportEventHandler : IEventHandler<NewTransport>
  {
    private readonly IEventBus _eventBus;
    private static FactionRepository _taskRepository;

    public NewTransportEventHandler(IEventBus eventBus, FactionRepository taskRepository)
    {
      _eventBus = eventBus; // Inject the EventBus into this Handler to Publish a message, insert AppDbContext here for DB Access
      _taskRepository = taskRepository;
    }

    public async Task Handle(NewTransport newTransport, string replyTo, string correlationId)
    {
      Console.WriteLine($"[i] Got Transport Message.");
      Transport transport = new Transport();
      transport.Name = newTransport.Name;
      transport.ApiKeyId = newTransport.ApiKeyId;
      _taskRepository.Add(transport);
      TransportCreated transportCreated = new TransportCreated();
      transportCreated.Success = true;
      transportCreated.Transport = transport;
      _eventBus.Publish(transportCreated, replyTo, correlationId);
    }
  }
}