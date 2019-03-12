using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using BCrypt.Net;
using RabbitMQ.Client;
using Microsoft.EntityFrameworkCore;

using Faction.Common;
using Faction.Common.Messages;
using Faction.Common.Models;
using Faction.Common.Backend.Database;
using Faction.Common.Backend.EventBus.Abstractions;
using Faction.Common.Backend.EventBus.RabbitMQ;
using Faction.Common.Backend.EventBus;
using Faction.Core.Handlers;

namespace Faction.Core
{
  // We need a dbcontext for dotnet ef commands to work. Don't know if this
  // is the best way to to do it, but it made the errors stop.
  class FactionCoreDbContext : FactionDbContext {}
  class Program
  {
    public static void Main(string[] args)
    {
      FactionSettings factionSettings = Utility.GetConfiguration();
      string connectionString = $"Host={factionSettings.POSTGRES_HOST};Database={factionSettings.POSTGRES_DATABASE};Username={factionSettings.POSTGRES_USERNAME};Password={factionSettings.POSTGRES_PASSWORD}";
      
      var host = new HostBuilder()
          .ConfigureAppConfiguration((hostingContext, config) =>
          {
            // config.AddJsonFile("appsettings.json", optional: true);
          })
          .ConfigureLogging((hostingContext, logging) =>
          {
            logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
            logging.AddConsole();
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

          })
          .ConfigureServices((hostContext, services) =>
          {
            string assemblyName = typeof(FactionDbContext).Namespace;
            services.AddEntityFrameworkNpgsql().AddDbContext<FactionDbContext>(options =>
                      options.UseNpgsql(connectionString,
                      optionsBuilder => optionsBuilder.MigrationsAssembly(assemblyName))
                  );

            // Open a connection to RabbitMQ and register it with DI
            services.AddSingleton<IRabbitMQPersistentConnection>(options =>
            {
              var factory = new ConnectionFactory()
              {
                HostName = factionSettings.RABBIT_HOST,
                UserName = factionSettings.RABBIT_USERNAME,
                Password = factionSettings.RABBIT_PASSWORD
              };
              return new DefaultRabbitMQPersistentConnection(factory);
            });

            services.AddSingleton<FactionRepository>();

            // Register the RabbitMQ EventBus with all the supporting Services (Event Handlers) with DI  
            RegisterEventBus(services);

            // Configure the above registered EventBus with all the Event to EventHandler mappings
            ConfigureEventBus(services);

            // Ensure the DB is initalized and seeding data
            // SeedData(services);
            bool dbLoaded = false;
            Console.WriteLine("Checking if database is ready");
            using (var context = new FactionDbContext())
            {
              while (!dbLoaded) {
                try {
                  var language = context.Language.CountAsync();
                  language.Wait();
                  dbLoaded = true;
                  Console.WriteLine("Database is ready");
                }
                catch (Exception exception) {
                  Console.WriteLine($"Database not ready, waiting 5 seconds. Error: {exception.Message}");
                  Task.Delay(5000).Wait();
                }
              }
            }

          })
          .Build();
      host.Start();
    }

    // TODO: Pass in the Exchange and Queue names to the constrcutors here (from appsettings.json)
    private static void RegisterEventBus(IServiceCollection services)
    {
      services.AddSingleton<IEventBus, EventBusRabbitMQ>(sp =>
      {
        var rabbitMQPersistentConnection = sp.GetRequiredService<IRabbitMQPersistentConnection>();
        var logger = sp.GetRequiredService<ILogger<EventBusRabbitMQ>>();
        var eventBusSubcriptionsManager = sp.GetRequiredService<IEventBusSubscriptionsManager>();
        return new EventBusRabbitMQ("Core", "FactionCore", rabbitMQPersistentConnection, eventBusSubcriptionsManager, sp, logger);
      });

      // Internal Service for keeping track of Event Subscription handlers (which Event maps to which Handler)
      services.AddSingleton<IEventBusSubscriptionsManager, InMemoryEventBusSubscriptionsManager>();

      // Add instances of our Message Event Handler to the DI pipeline
      services.AddTransient<NewAgentCheckinEventHandler>();
      services.AddTransient<NewConsoleMessageEventHandler>();
      services.AddTransient<NewErrorMessageEventHandler>();
      services.AddTransient<NewTransportEventHandler>();
      services.AddTransient<NewStagingMessageEventHandler>();
      services.AddTransient<UpdateAgentEventHandler>();
      services.AddTransient<UpdateTransportEventHandler>();
    }
    private static void ConfigureEventBus(IServiceCollection services)
    {
      var sp = services.BuildServiceProvider();
      var eventBus = sp.GetRequiredService<IEventBus>();
      // Map the Message Event Type to the proper Event Handler
      eventBus.Initialize();
      eventBus.Subscribe<NewAgentCheckin, NewAgentCheckinEventHandler>();
      eventBus.Subscribe<NewConsoleMessage, NewConsoleMessageEventHandler>();
      eventBus.Subscribe<NewErrorMessage, NewErrorMessageEventHandler>();
      eventBus.Subscribe<NewTransport, NewTransportEventHandler>();
      eventBus.Subscribe<NewStagingMessage, NewStagingMessageEventHandler>();
      eventBus.Subscribe<UpdateAgent, UpdateAgentEventHandler>();
      eventBus.Subscribe<UpdateTransport, UpdateTransportEventHandler>();
    }
  }
}