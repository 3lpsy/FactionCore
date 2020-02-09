using System;
using System.Text;
using System.Linq;
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
  class FactionCoreDbContext : FactionDbContext { }
  class Program
  {
    // this is the first migration added to Faction.Common
    private static string initialMigrationName = "20191019033321_Initial";

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
            string assemblyName = typeof(FactionDbContext).Assembly.FullName;
            services.AddEntityFrameworkNpgsql().AddDbContext<FactionDbContext>(options =>
                options.UseNpgsql(connectionString,
                optionsBuilder => optionsBuilder.MigrationsAssembly(assemblyName))
            );

            // Check to see if the database is listening and receptive to commands. 
            // does not check if database is configured/setup
            ConfirmDbReady(services);


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

          })
          .Build();

      AutoMigrateSchema(host);
      ConfirmDbSetup(host);
      AutoSeedDb(host);

      Console.WriteLine("Starting Faction Core Server...");
      using (host) {
        host.Start();
      }
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
      services.AddTransient<NewPayloadEventHandler>();
      services.AddTransient<NewTransportEventHandler>();
      services.AddTransient<NewStagingMessageEventHandler>();
      services.AddTransient<UpdateAgentEventHandler>();
      services.AddTransient<UpdatePayloadEventHandler>();
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
      eventBus.Subscribe<NewPayload, NewPayloadEventHandler>();
      eventBus.Subscribe<NewTransport, NewTransportEventHandler>();
      eventBus.Subscribe<NewStagingMessage, NewStagingMessageEventHandler>();
      eventBus.Subscribe<UpdateAgent, UpdateAgentEventHandler>();
      eventBus.Subscribe<UpdatePayload, UpdatePayloadEventHandler>();
      eventBus.Subscribe<UpdateTransport, UpdateTransportEventHandler>();
    }


    public static IHost AutoMigrateSchema(IHost host)
    {
      char[] trimQuotes = { '"', '\'' };

      // check if auto migration is enabled
      string shouldAutoMigrate = Environment.GetEnvironmentVariable("POSTGRES_AUTO_MIGRATE") ?? "0";
      shouldAutoMigrate = shouldAutoMigrate.ToLower().Trim(trimQuotes);
      if (shouldAutoMigrate == "1" || shouldAutoMigrate == "true") {
        Console.WriteLine("Checking for new schemas to auto migrate...");
        using (var scope = host.Services.CreateScope()) {
          // confirm that this deployment is compatible with automigration by checking first migration was initialMigrationName
          try {
            var dbContext = scope.ServiceProvider.GetService<FactionDbContext>();
            var migrations = dbContext.Database.GetMigrations();
            bool isMigrationCompatible = false;
            foreach (string migration in migrations) {
              if (migration == initialMigrationName) {
                isMigrationCompatible = true;
              }
              Console.WriteLine($"Found migration: {migration}");
            }

            // apply migration if the project was (presumably) built after static migrations were introduced
            if (isMigrationCompatible) {
              ApplyMigrations(dbContext);
            } else {
              // allow user to attempt to apply self-built migrations. this will most likely fail, but it's up to them
              // the reason it'll fail is because their migrations were most likely generated in the Core project, not Faction.Common
              // EF will (probably) fail to find these migrations. however, properly generated ones should work even if self-built.
              string shouldForceAutoMigrate = Environment.GetEnvironmentVariable("POSTGRES_AUTO_MIGRATE_FORCE") ?? "0";
              shouldForceAutoMigrate = shouldForceAutoMigrate.ToLower().Trim(trimQuotes);
              Console.WriteLine($"Could not find the official initial migration '{initialMigrationName}' in current or pending migrations. The current database either has migrations applied from a self-built migration set or Faction.Common is outdated or contains self-built migrations");
              if (shouldForceAutoMigrate == "1" || shouldForceAutoMigrate == "true") {
                ApplyMigrations(dbContext);
              } else {
                // otherwise, just exit and tell the user not to use auto migrations
                Console.WriteLine("Auto migrating is discouraged with self built migrations. Please either disable auto migrations by setting 'POSTGRES_AUTO_MIGRATE' to '0' or forcing automatic migrations with self-built migrations by setting 'POSTGRES_AUTO_MIGRATE_FORCE' to '1'. Please note forcing migrations may corrupt data or have unknown consequences");
                Environment.Exit(1);
              }
            }

          } catch (System.IO.FileNotFoundException ex) {
            // this is one of the original error that occurs when EF Core cannot find the Migrations inside of Faction.Common.
            Console.WriteLine($"Unable to automatically apply migrations! The Faction.Common assembly most likely does not contain Migrations.");
            Console.WriteLine("Please pull an updated version of Faction.Common or disable auto migrate by setting 'POSTGRES_AUTO_MIGRATE' to 0.");
            Console.WriteLine($"Error: {ex.GetType()}");
            Environment.Exit(1);
          } catch (System.IO.FileLoadException ex) {
            // this is one of the original error that occurs when EF Core cannot find the Migrations inside of Faction.Common.
            Console.WriteLine($"Unable to automatically apply migrations! The Faction.Common assembly most likely does not contain Migrations.");
            Console.WriteLine("Please pull an updated version of Faction.Common or disable auto migrate by setting 'POSTGRES_AUTO_MIGRATE' to 0.");
            Console.WriteLine($"Error: {ex.GetType()}");
            Environment.Exit(1);
          }
          // there is the potential for an uncaught aggregate exception here.
        }
      } else {
        Console.WriteLine("Skipping auto migration of schemas...");
      }

      return host;
    }

    public static void ApplyMigrations(DbContext dbContext)
    {
      var pendingMigrations = dbContext.Database.GetPendingMigrations();
      var pendingMigrationsCount = 0;
      foreach (string pendingMigration in pendingMigrations) {
        Console.WriteLine($"Found pending migration: {pendingMigration}");
        pendingMigrationsCount += 1;
      }
      Console.WriteLine($"Applying {pendingMigrationsCount} migrations...");
      dbContext.Database.Migrate();
    }

    public static void ConfirmDbReady(IServiceCollection services)
    {
      bool dbReady = false;
      var dbContext = services.BuildServiceProvider().GetService<FactionDbContext>();
      while (!dbReady) {
        using (var command = dbContext.Database.GetDbConnection().CreateCommand()) {
          command.CommandText = "SELECT 1;";
          command.CommandType = System.Data.CommandType.Text;
          try {
            dbContext.Database.OpenConnection();
          } catch (System.InvalidOperationException ex) {
            // TODO: handle errors
            Console.WriteLine($"Database not ready yet. Waiting 5 seconds. Error: {ex.GetType()}");
            Task.Delay(5000).Wait();
            continue;
          }
          using (var reader = command.ExecuteReader()) {
            while (reader.HasRows) {
              while (reader.Read()) {
                var result = (int)reader.GetInt32(0);
                if (result == 1) {
                  Console.WriteLine("Database is listening...");
                  dbReady = true;
                }
              }
              reader.NextResult();
            }
          }
        }
      }
    }

    public static IHost ConfirmDbSetup(IHost host)
    {
      bool dbLoaded = false;
      Console.WriteLine("Checking if Database is setup...");
      using (var scope = host.Services.CreateScope()) {
        var dbContext = scope.ServiceProvider.GetService<FactionDbContext>();
        while (!dbLoaded) {
          try {
            var language = dbContext.Language.CountAsync();
            language.Wait();
            dbLoaded = true;
            Console.WriteLine("Database is setup");
          } catch (Exception exception) {
            Console.WriteLine($"Database not setup, waiting 5 seconds. Error: {exception.GetType()} - {exception.Message}");
            Task.Delay(5000).Wait();
          }
        }
      }
      return host;
    }

    public static void AutoSeedRoles(FactionDbContext dbContext)
    {
      // the necessary default roles
      string[] roleNames = { "system", "admin", "operator", "readonly" };
      foreach (string roleName in roleNames) {
        // check if the role exists
        var existingRole = dbContext.UserRole.FirstOrDefault(r => r.Name.ToLower() == roleName);
        if (existingRole == null) {
          // if not, create it
          var role = new UserRole
          {
            Name = roleName
          };
          dbContext.Add(role);
          Console.WriteLine($"Saving role for {roleName}");
          dbContext.SaveChanges();
        } else {
          Console.WriteLine($"Role already exists for {existingRole.Name}");
        }
      }
    }

    public static void AutoSeedDefaultUsers(FactionDbContext dbContext)
    {
      // loop over roles and see if env variables are set for those users
      var roles = dbContext.UserRole.ToList();
      foreach (UserRole role in roles) {
        // create the env key (i.e. ADMIN_USERNAME, SYSTEM_USERNAME)
        string roleEnvPrefix = role.Name.ToUpper();
        string roleEnvUsernameKey = roleEnvPrefix + "_USERNAME";
        string username = Environment.GetEnvironmentVariable(roleEnvUsernameKey);
        if (!String.IsNullOrEmpty(username)) {
          // make sure there is a corresponding password set
          string roleEnvPasswordKey = roleEnvPrefix + "_PASSWORD";
          string password = Environment.GetEnvironmentVariable(roleEnvPasswordKey);
          if (!String.IsNullOrEmpty(password)) {
            // if the user already exists, just continue, otherwise create the user
            var existingUser = dbContext.User.FirstOrDefault(r => r.Username.ToLower() == username);
            if (existingUser == null) {
              // HashPassword returns a string, convert it byte[]
              byte[] passwordHash = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(password));
              var user = new User
              {
                Username = username,
                RoleId = role.Id,
                Enabled = true,
                Visible = true,
                Password = passwordHash
              };
              dbContext.Add(user);
              Console.WriteLine($"Saving {role.Name} user {user.Username}");
              dbContext.SaveChanges();
            } else {
              Console.WriteLine($"The user {existingUser.Username} already exists. Skipping..");
            }
          } else {
            Console.WriteLine($"No value found for {roleEnvPasswordKey}. Skipping...");
          }
        } else {
          Console.WriteLine($"No value found for {roleEnvUsernameKey}. Skipping...");
        }
      }
    }

    public static string GenerateUrlSafeSecret(int keyLength)
    {
      char[] padding = { '=' };
      RNGCryptoServiceProvider rngCryptoServiceProvider = new RNGCryptoServiceProvider();
      byte[] targetBytes = new byte[keyLength];
      rngCryptoServiceProvider.GetBytes(targetBytes);
      return Convert.ToBase64String(targetBytes).TrimEnd(padding).Replace('+', '-').Replace('/', '_');
    }

    public static void AutoSeedDefaultApiKeyAndTransport(FactionDbContext dbContext)
    {
      // generate name (12 bytes) and secret (48 bytes)
      var existingApiKey = dbContext.ApiKey.FirstOrDefault();
      if (existingApiKey == null) {
        var apiKeyName = GenerateUrlSafeSecret(12);
        var apiKeyToken = GenerateUrlSafeSecret(48);
        var apiKeyTokenHash = BCrypt.Net.BCrypt.HashPassword(apiKeyToken);
        var apiKeyBytes = Encoding.UTF8.GetBytes(apiKeyTokenHash);
        var defaultSystemUser = dbContext.User.FirstOrDefault(u => u.Role.Name.ToLower() == "system");

        if (defaultSystemUser != null) {
          var apiKey = new ApiKey
          {
            Name = apiKeyName,
            Key = apiKeyBytes,
            UserId = defaultSystemUser.Id,
            OwnerId = defaultSystemUser.Id,
            Enabled = true,
            Visible = true
          };
          dbContext.Add(apiKey);
          Console.WriteLine($"Saving Api Key {apiKey.Name}");
          dbContext.SaveChanges();

          string transportExternalAddress = Environment.GetEnvironmentVariable("EXTERNAL_ADDRESS");
          if (!String.IsNullOrEmpty(transportExternalAddress)) {
            var existingTransport = dbContext.Transport.FirstOrDefault();
            if (existingTransport == null) {
              string transportName = "DIRECT Transport";
              string transportType = "DIRECT";
              string transportGUID = "0000-0000-0000-0000-0000";
              string transportConfiguration = "{\"TransportId\": 1, \"ApiUrl\":\""
                  + transportExternalAddress +
                  "\",\"ApiKeyName\":\"" +
                  apiKey.Name +
                  "\",\"ApiSecret\":\"" +
                  apiKeyToken +
                  "\"}";

              var transport = new Transport
              {
                Name = transportName,
                TransportType = transportType,
                Guid = transportGUID,
                Configuration = transportConfiguration,
                ApiKeyId = apiKey.Id,
                Enabled = true,
                Visible = true
              };
              dbContext.Add(transport);
              Console.WriteLine($"Saving new default transport {transport.Name}");
              dbContext.SaveChanges();
            } else {
              Console.WriteLine("A transport already exists. Skipping creating default transport");
            }
          } else {
            Console.WriteLine("No external address defined. Unable to create direct transport. Skipping");
          }
        } else {
          Console.WriteLine("Unable to create API Key. No system user found. Skipping.");
        }
      } else {
        Console.WriteLine("An ApiKey already exists. Skipping.");
      }
    }

    public static IHost AutoSeedDb(IHost host)
    {
      char[] trimQuotes = { '"', '\'' };
      // check if auto seed is enabled
      string shouldAutoSeed = Environment.GetEnvironmentVariable("POSTGRES_AUTO_SEED") ?? "0";
      shouldAutoSeed = shouldAutoSeed.ToLower().Trim(trimQuotes);
      if (shouldAutoSeed == "1" || shouldAutoSeed == "true") {
        Console.WriteLine("Seeding Database...");
        using (var scope = host.Services.CreateScope()) {
          var dbContext = scope.ServiceProvider.GetService<FactionDbContext>();
          // first, seed the default roles
          AutoSeedRoles(dbContext);
          // second, seed the users
          AutoSeedDefaultUsers(dbContext);
          // next seed the default api key and default transport
          AutoSeedDefaultApiKeyAndTransport(dbContext);
        }
      }
      return host;
    }
  }
}