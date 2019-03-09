using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

using Newtonsoft.Json;
using Faction.Common.Backend.Database;
using Faction.Common.Backend.EventBus.RabbitMQ;
using Faction.Common.Backend.EventBus.Abstractions;
using Faction.Common.Models;
using Faction.Common.Messages;

namespace Faction.Core.Handlers
{
  public static class AgentDetails
  {
    public static AgentType AgentType;
    public static Language Language;
    public static List<Module> AvailableModules;
    public static List<AgentsModulesXref> LoadedModules;
    public static bool IsCommandAvailable(Command command)
    {
      if (command.AgentTypeId.HasValue)
      {
        if (AgentType.Id == command.AgentTypeId.Value)
        {
          return true;
        }
      }
      else if (command.ModuleId.HasValue)
      {
        return IsModuleLoaded(command.ModuleId.Value);
      }
      // Default to false
      return false;
    }
    
    public static bool IsModuleLoaded(Module module)
    {
      return IsModuleLoaded(module.Id);
    }
    
    public static bool IsModuleLoaded(int ModuleId)
    {
      foreach (AgentsModulesXref xref in LoadedModules) {
        if (xref.ModuleId == ModuleId)
        {
          return true;
        }
      }
      return false;
    }
  }

  public class FactionCommand
  {
    public FactionCommand() {
      Arguments = new Dictionary<string, string>();
    }
    public string Command;
    public Dictionary<string, string> Arguments;
    bool Loaded;
  }

  // Used to serialize commands in a "show commands" command
  public class ShowCommand
  {
    public string Name;
    public string Description;
    public string ModuleName;
    public bool ModuleLoaded; 

    public ShowCommand(Command command, bool loaded)
    {
      Name = command.Name;
      Description = command.Description;
      if (command.ModuleId.HasValue)
      {
        ModuleName = command.Module.Name;
      }
      else {
        ModuleName = "Builtin";
      }
      ModuleLoaded = loaded;
    }
  }

  public class ShowModule
  {
    public string Name;
    public string Description;
    public string Authors;
    public bool Loaded;

    public ShowModule(Module module, bool loaded = false)
    {
      Name = module.Name;
      Description = module.Description;
      Authors = module.Authors;
      Loaded = loaded;
    }
  }

  public class NewConsoleMessageEventHandler : IEventHandler<NewConsoleMessage>
  {
    private readonly IEventBus _eventBus;
    private static FactionRepository _taskRepository;
    
    private static bool error = false;
    private static string errorMessage = "";

    public NewConsoleMessageEventHandler(IEventBus eventBus, FactionRepository taskRepository)
    {
      _eventBus = eventBus; // Inject the EventBus into this Handler to Publish a message, insert AppDbContext here for DB Access
      _taskRepository = taskRepository;
    }

    // Stolen from: https://stackoverflow.com/a/2132004
    public static string[] SplitArguments(string commandLine)
    {
        var parmChars = commandLine.ToCharArray();
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var index = 0; index < parmChars.Length; index++)
        {
            if (parmChars[index] == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                if (inDoubleQuote)
                {
                  parmChars[index] = ' ';
                }
                else {
                  parmChars[index] = '\n';
                }
            }
            if (parmChars[index] == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                parmChars[index] = '\n';
            }
            if (!inSingleQuote && !inDoubleQuote && parmChars[index] == ' ')
                parmChars[index] = '\n';
        }
        return (new string(parmChars)).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public static ConsoleMessage ProcessHelpMessage(ConsoleMessage consoleMessage)
    {
      ConsoleMessage consoleResponse = new ConsoleMessage();
      consoleResponse.AgentId = consoleMessage.AgentId;
      consoleResponse.UserId = 1;

      consoleResponse.Received = DateTime.UtcNow;
      consoleResponse.Type = "HelpResponse";

      // Remove the leading "HELP ", if we can't do this lets assume its just "help"
      string recievedCommand = "";
      try
      {
        recievedCommand = consoleMessage.Content.Remove(0, 5);
      } 
      catch
      {
        consoleResponse.Display = $"Faction Agent Help:\n\n* 'show commands' will return a list of available commands.\n* 'show modules' will show available modules\n* 'help <command>` will give you details about a command\n* 'help <command> /<paramater>' will give you details about a commands paramter";
        return consoleResponse;
      }

      FactionCommand factionCommand = ProcessCommand(recievedCommand);
      if (factionCommand.Arguments.Count > 0) 
      {
        string ParameterName = factionCommand.Arguments.Keys.Last().ToString();
        CommandParameter parameter;
        try 
        {
          parameter = _taskRepository.GetCommandParameter(factionCommand.Command, ParameterName);
          if (String.IsNullOrEmpty(parameter.Help))
          {
            consoleResponse.Display = $"No help available for parameter: {parameter.Name} under command {factionCommand.Command}";
          }
          else
          {
            consoleResponse.Display = $"Name: {parameter.Name}\nRequired: {parameter.Required}\nAccepted Values: {parameter.Values}\n\n## Help\n{parameter.Help}";
          }
        }
        catch 
        {
          consoleResponse.Display = $"No parameter named {ParameterName} found for command {factionCommand.Command}";
        }
      }
      else {
        try
        {
          Command command = _taskRepository.GetCommand(factionCommand.Command);
          if (String.IsNullOrEmpty(command.Help))
          {
            consoleResponse.Display = $"No help available for command {command.Name}";
          }
          else {
            consoleResponse.Display = $"Name: {command.Name}";
            consoleResponse.Display += $"\nDescription: {command.Description}";
            consoleResponse.Display += $"\nMitre ATT&CK Reference: {command.MitreReference}";
            consoleResponse.Display += $"\nOpsecSafe: {command.OpsecSafe}";
            consoleResponse.Display += $"\nLoaded: {AgentDetails.IsCommandAvailable(command)}";
            consoleResponse.Display += $"\n\nHelp:\n{command.Help}\n";
            List<CommandParameter> parameters = _taskRepository.GetCommandParameters(command.Id);
            if (parameters.Count() > 0)
            {
              string parameterText = "\nParameters:";
              foreach (CommandParameter param in parameters){
                parameterText += $"\nName: {param.Name}";
                parameterText += $"\nRequired: {param.Required.ToString()}";
                if (param.Position.HasValue)
                {
                  parameterText += $"\nPosition: {param.Position.Value.ToString()}";
                }
                else
                {
                  parameterText += $"\nPosition: N/A";
                }
                parameterText += $"\nHelp: {param.Help}\n";
              }
              consoleResponse.Display += parameterText;
            }
            if (!String.IsNullOrEmpty(command.Artifacts)){
              consoleResponse.Display += "\n\nArtifacts:";
              string[] artifacts = command.Artifacts.Split(",");
              foreach (string artifact in artifacts)
              {
                consoleResponse.Display += $"\n* {artifact}";
              }
            }
            consoleResponse.Display += "\n";
          }
        }
        catch
        {
          consoleResponse.Display = $"No command found named {recievedCommand}";
        }
      }
      return consoleResponse;
    }

    public static ConsoleMessage ProcessShowMessage(ConsoleMessage consoleMessage)
    {
      ConsoleMessage consoleResponse = new ConsoleMessage();
      consoleResponse.AgentId = consoleMessage.AgentId;
      consoleResponse.UserId = 1;
      consoleResponse.Type = "ShowMessage";
      consoleResponse.Received = DateTime.UtcNow;

      List<string> availableShowCommands = new List<string>();
      availableShowCommands.Add("MODULES");
      availableShowCommands.Add("COMMANDS");

      string showCommand = "";

      string[] commandParts = consoleMessage.Content.Split(' ');
      if (availableShowCommands.Contains(commandParts[1].ToUpper()))
      {
        showCommand = commandParts[1].ToUpper();
      }
      else 
      {
        consoleResponse.Display = $"Unknown SHOW option {commandParts[1]}. Valid options are: {availableShowCommands.ToString()}";
      }

      if (String.IsNullOrEmpty(showCommand))
      {
        return consoleResponse;
      }

      if (showCommand == "MODULES")
      {
        List<ShowModule> showModules = new List<ShowModule>();
        foreach (Module module in AgentDetails.AvailableModules) {
          showModules.Add(new ShowModule(module, AgentDetails.IsModuleLoaded(module)));
        }
        consoleResponse.Display = JsonConvert.SerializeObject(showModules);
      }
      else if (showCommand == "COMMANDS")
      {
        List<ShowCommand> showCommands = new List<ShowCommand>();
        List<Command> agentTypeCommands = _taskRepository.GetAgentTypeCommands(AgentDetails.AgentType.Id);
        foreach (Command command in agentTypeCommands)
        {
          showCommands.Add(new ShowCommand(command, AgentDetails.IsCommandAvailable(command)));
        }
        foreach (Module module in AgentDetails.AvailableModules)
        {

          List<Command> commands = _taskRepository.GetCommands(module.Id);
          foreach (Command command in commands)
          {
            showCommands.Add(new ShowCommand(command, AgentDetails.IsCommandAvailable(command)));
          }
        }
        consoleResponse.Display = JsonConvert.SerializeObject(showCommands);
      }
      return consoleResponse;
    }

    // This tasks a command and returns a FactionCommand object of the command and arguments
    public static FactionCommand ProcessCommand(string Command, int AgentId=0) 
    {
      FactionCommand factionCommand = new FactionCommand();

      // split the command into the command and a string of arguments
      int index = Command.IndexOf(' ');
      if (index > 0) {
        factionCommand.Command = Command.Substring(0, index);
        string submittedArgs = Command.Substring(index + 1);

        if (submittedArgs.Length > 0) {
          int position = 0;
          string[] processedArgs = SplitArguments(submittedArgs);

          foreach (string arg in processedArgs) {
            if (!error) {
              string ParameterName = "";
              string ParameterValue = "";

              // Check to see if arg starts with a param name
              if (arg.StartsWith('/'))
              {
                index = arg.IndexOf(':');
                if (index > 0)
                {
                  ParameterName = arg.TrimStart('/').Substring(0, index).TrimEnd(':');
                  ParameterValue = arg.Substring(index + 1);
                }
                else
                {
                  // Hopefully catches the edge case where a positional argument starts with /
                  ParameterValue = arg;
                }
              }

              // Make sure that we have a proper parameter name either by name or position
              CommandParameter parameter;
              if (String.IsNullOrEmpty(ParameterName))
              {
                parameter = _taskRepository.GetCommandParameter(factionCommand.Command, position);
                ParameterValue = arg;
              }
              else 
              {
                parameter = _taskRepository.GetCommandParameter(factionCommand.Command, ParameterName);
            
              }

              // If we couldn't find a matching param, fail out. Else, make sure the param name matches whats was defined
              if (parameter == null) 
              {
                error = true;
              }
              else {
                ParameterName = parameter.Name;
              }

              // Everything should be defined now. If not, throw an error.
              if (String.IsNullOrEmpty(ParameterName) || String.IsNullOrEmpty(ParameterValue) || error) {
                error = true;
                errorMessage = $"ERROR: Unable to process argument: {arg}";
              }
              else {
                Dictionary<string, string> argDict = new Dictionary<string, string>();
                factionCommand.Arguments[ParameterName] = ParameterValue.Trim(' ');
              }
              position++;
            }
          }
          return factionCommand;
        }
      }
      factionCommand.Command = Command;
      return factionCommand;  
    }

    public async Task Handle(NewConsoleMessage newConsoleMessage, string replyTo, string correlationId)
    {
      // Reset Error stuff
      error = false;
      errorMessage = "";

      // figure out what agent we're dealing with
      Agent agent = _taskRepository.GetAgent(newConsoleMessage.AgentId);
      agent.AgentType = _taskRepository.GetAgentType(agent.AgentTypeId);

      // flesh out and save the ConsoleMessage object
      ConsoleMessage consoleMessage = new ConsoleMessage();
      consoleMessage.AgentId = newConsoleMessage.AgentId;
      consoleMessage.UserId = newConsoleMessage.UserId;
      consoleMessage.Agent = agent;
      consoleMessage.User = _taskRepository.GetUser(consoleMessage.UserId.Value);
      consoleMessage.Content = newConsoleMessage.Content;
      consoleMessage.Display = newConsoleMessage.Display;
      consoleMessage.Received = DateTime.UtcNow;
      consoleMessage.Type = "AgentTask";
      _taskRepository.Add(consoleMessage);

      // Announce our new message to Rabbit
      ConsoleMessageAnnouncement messageAnnouncement = new ConsoleMessageAnnouncement();
      messageAnnouncement.Success = true;
      messageAnnouncement.Username = consoleMessage.User.Username;
      messageAnnouncement.ConsoleMessage = consoleMessage;
      _eventBus.Publish(messageAnnouncement);

      // These are the commands we allow. If one of these isn't the first part of a command
      List<string> allowedActions = new List<string>();
      allowedActions.Add("HELP");
      allowedActions.Add("SHOW");
      allowedActions.Add("LOAD");
      allowedActions.Add("SET");
      allowedActions.Add("USE");
      allowedActions.Add("RUN");
      allowedActions.Add("EXIT");

      // we assume that the command is a RUN command
      string action = "RUN";

      string[] consoleMessageComponents = consoleMessage.Content.Split(' ');
      if (consoleMessageComponents.Length > 0)
      {
        if (allowedActions.Contains(consoleMessageComponents[0].ToUpper()))
        {
          action = consoleMessageComponents[0].ToUpper();
        }
      }

      // if this is a SHOW or HELP commmand, we won't be sending anything to the agent
      // so lets take care of that here:
      AgentDetails.Language = _taskRepository.GetLanguage(consoleMessage.Agent.AgentType.LanguageId);
      AgentDetails.AvailableModules = _taskRepository.GetModules(AgentDetails.Language.Id);
      AgentDetails.LoadedModules = _taskRepository.GetAgentModules(consoleMessage.AgentId);
      AgentDetails.AgentType = consoleMessage.Agent.AgentType;

      if (action == "HELP")
      {
        ConsoleMessage message = ProcessHelpMessage(consoleMessage);
        _taskRepository.Add(message);

        ConsoleMessageAnnouncement response = new ConsoleMessageAnnouncement();
        response.Success = true;
        response.Username = "SYSTEM";
        response.ConsoleMessage = message;
        _eventBus.Publish(response);
      }

      else if (action == "SHOW")
      {
        ConsoleMessage message = ProcessShowMessage(consoleMessage);
        _taskRepository.Add(message);

        ConsoleMessageAnnouncement response = new ConsoleMessageAnnouncement();
        response.Success = true;
        response.Username = "SYSTEM";
        response.ConsoleMessage = message;
        _eventBus.Publish(response);
      }

      else
      {
        // We'll be tasking the agent to do something so lets create an agentTask
        AgentTask agentTask = new AgentTask();
        agentTask.Action = action;
        agentTask.AgentId = consoleMessage.AgentId;
        agentTask.ConsoleMessageId = consoleMessage.Id;
        agentTask.ConsoleMessage = consoleMessage;
        agentTask.Agent = consoleMessage.Agent;

        // Package the AgentTask into a envelope for seralization & encryption.
        // Then process the ACTION and populate CONTENTS appropriately
        Dictionary<String, String> outboundMessage = new Dictionary<String, String>();
        outboundMessage.Add("AgentName", agentTask.Agent.Name);
        outboundMessage.Add("Name", agentTask.Name);
        outboundMessage.Add("Action", agentTask.Action);

        // Message formats
        // * load stdlib
        // * load dotnet/stdlib
        // * load transport/dns
        if (agentTask.Action == "LOAD")
        {
          LoadModule msg = new LoadModule();
          if (consoleMessageComponents[1].Contains("/"))
          {
            msg.Language = consoleMessageComponents[1].Split("/")[0];
            msg.Name = consoleMessageComponents[1].Split("/")[0];
          }
          else
          {
            msg.Language = (_taskRepository.GetLanguage(consoleMessage.Agent.AgentType.LanguageId)).Name;
            msg.Name = consoleMessageComponents[1];
          }
          _eventBus.Publish(msg, null, null, true);

          string message = _eventBus.ResponseQueue.Take();
          ModuleResponse moduleResponse = JsonConvert.DeserializeObject<ModuleResponse>(message);
          outboundMessage.Add("Command", moduleResponse.Contents);
        }

        // Message formats
        // * set beacon:5
        else if (agentTask.Action == "SET")
        {
          outboundMessage.Add("Command", consoleMessageComponents[1]);
        }

        else if (agentTask.Action == "EXIT")
        {
          outboundMessage.Add("Command", "exit");
        }
        // Example commands:
        // * ls
        // * ls "C:\Program Files"
        // * ls /path:"C:\Program Files"
        if (agentTask.Action == "RUN")
        {
          FactionCommand factionCommand = ProcessCommand(consoleMessage.Content);
          string command = $"{factionCommand.Command}";
          if (factionCommand.Arguments.Count > 0)
          {
            command = $"{factionCommand.Command} {JsonConvert.SerializeObject(factionCommand.Arguments)}";
          }
          outboundMessage.Add("Command", command);
        }

        // update agentTask with final command format and save it
        agentTask.Command = outboundMessage["Command"];
        _taskRepository.Add(agentTask);

        // update the incoming consoleMessage with this task Id
        consoleMessage.AgentTaskId = agentTask.Id;
        _taskRepository.Update(consoleMessage.Id, consoleMessage);

        // If there's an error, send it back
        if (error)
        {
          ConsoleMessage message = new ConsoleMessage();
          message.AgentId = consoleMessage.AgentId;
          message.AgentTaskId = agentTask.Id;
          message.UserId = 1;
          message.Type = "AgentTaskError";
          message.Content = errorMessage;
          _taskRepository.Add(message);

          ConsoleMessageAnnouncement response = new ConsoleMessageAnnouncement();
          response.Success = true;
          response.Username = "SYSTEM";
          response.ConsoleMessage = message;
          _eventBus.Publish(response);
        }
        // Else, create a new task for the agent
        else
        {
          string jsonOutboundMessage = JsonConvert.SerializeObject(outboundMessage);
          Dictionary<string, string> encCommand = Crypto.Encrypt(jsonOutboundMessage, agentTask.Id, agentTask.Agent.AesPassword);

          // Create a AgentTaskMessage object with the seralized/encrypted message contents
          AgentTaskMessage agentTaskMessage = new AgentTaskMessage();
          agentTaskMessage.Agent = agentTask.Agent;
          agentTaskMessage.Message = encCommand["encryptedMsg"];
          agentTaskMessage.AgentId = consoleMessage.Agent.Id;
          agentTaskMessage.AgentTaskId = agentTask.Id;
          agentTaskMessage.AgentTask = agentTask;
          agentTaskMessage.Hmac = encCommand["hmac"];
          agentTaskMessage.Iv = encCommand["iv"];
          agentTaskMessage.Sent = false;
          _taskRepository.Add(agentTaskMessage);
        }
      }
    }
  }
}