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
  public class NewErrorMessageEventHandler : IEventHandler<NewErrorMessage>
  {
    private readonly IEventBus _eventBus;
    private static FactionRepository _taskRepository;

    public NewErrorMessageEventHandler(IEventBus eventBus, FactionRepository taskRepository)
    {
      _eventBus = eventBus; // Inject the EventBus into this Handler to Publish a message, insert AppDbContext here for DB Access
      _taskRepository = taskRepository;
    }

    public async Task Handle(NewErrorMessage newErrorMessage, string replyTo, string correlationId)
    {
      Console.WriteLine($"[i] Got ErrorMessage Message.");
      ErrorMessage errorMessage = new ErrorMessage();
      errorMessage.Source = newErrorMessage.Source;
      errorMessage.Message = newErrorMessage.Message;
      errorMessage.Details = newErrorMessage.Details;
      _taskRepository.Add(errorMessage);

      ErrorMessageAnnouncement errorMessageAnnouncement = new ErrorMessageAnnouncement();
      errorMessageAnnouncement.Id = errorMessage.Id;
      errorMessageAnnouncement.Source = errorMessage.Source;
      errorMessageAnnouncement.Message = errorMessage.Message;
      errorMessageAnnouncement.Details = errorMessage.Details;
      errorMessageAnnouncement.Timestamp = errorMessage.Timestamp;
      _eventBus.Publish(errorMessageAnnouncement, replyTo, correlationId);
    }
  }
}