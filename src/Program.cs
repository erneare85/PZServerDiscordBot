#define EXPORT_DEFAULT_LOCALIZATION

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordRPC;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public static class Application
{
    public const string BotRepoURL = "https://github.com/erneare85/PZServerDiscordBot";
    public static readonly SemanticVersion BotVersion = new SemanticVersion(1, 0, 0, DevelopmentStage.Release);
    public static Settings.BotSettings     BotSettings;

    public static DiscordSocketClient  Client;
    private static DiscordRpcClient _rpcClient;
    public static CommandService       Commands;
    public static IServiceProvider     Services;
    public static CommandHandler       CommandHandler;
    public static DateTime             StartTime = DateTime.UtcNow;

    private static bool botInitialCheck = false;

    private static void Main(string[] _) => MainAsync().GetAwaiter().GetResult();

    private static async Task MainAsync()
    {
        if(!File.Exists(Settings.BotSettings.SettingsFile))
        {
            BotSettings = new Settings.BotSettings();
            BotSettings.Save();
        }
        else
        {
            BotSettings = JsonConvert.DeserializeObject<Settings.BotSettings>(File.ReadAllText(Settings.BotSettings.SettingsFile), 
                new JsonSerializerSettings{ObjectCreationHandling = ObjectCreationHandling.Replace});
        }

        Localization.Load();
    #if EXPORT_DEFAULT_LOCALIZATION
        Localization.ExportDefault();
    #endif

    #if DEBUG
        Console.WriteLine(Localization.Get("warn_debug_mode"));
    #endif

        try
        {
            if(string.IsNullOrEmpty(DiscordUtility.GetToken()))
            {
                Console.WriteLine(Localization.Get("err_bot_token").KeyFormat(("repo_url", BotRepoURL)));
                await Task.Delay(-1);
            }
        }
        catch(Exception ex)
        {
            Logger.LogException(ex);
            Console.WriteLine(Localization.Get("err_retv_bot_token").KeyFormat(("log_file", Logger.LogFile), ("repo_url", BotRepoURL)));
            await Task.Delay(-1);
        }

    #if !DEBUG
        ServerPath.CheckCustomBasePath();
    #endif

        if(!Directory.Exists(Localization.LocalizationPath))
            Directory.CreateDirectory(Localization.LocalizationPath);

        Scheduler.AddItem(new ScheduleItem("ServerRestart",
                                           Localization.Get("sch_name_serverrestart"),
                                           BotSettings.ServerScheduleSettings.GetServerRestartSchedule(),
                                           Schedules.ServerRestart,
                                           null));
        Scheduler.AddItem(new ScheduleItem("ServerRestartAnnouncer",
                                           Localization.Get("sch_name_serverrestartannouncer"),
                                           Convert.ToUInt64(TimeSpan.FromSeconds(30).TotalMilliseconds),
                                           Schedules.ServerRestartAnnouncer,
                                           null));
        Scheduler.AddItem(new ScheduleItem("WorkshopItemUpdateChecker",
                                           Localization.Get("sch_name_workshopitemupdatechecker"),
                                           BotSettings.ServerScheduleSettings.WorkshopItemUpdateSchedule,
                                           Schedules.WorkshopItemUpdateChecker,
                                           null));
        Scheduler.AddItem(new ScheduleItem("AutoServerStart",
                                           Localization.Get("sch_name_autoserverstarter"),
                                           Convert.ToUInt64(TimeSpan.FromSeconds(30).TotalMilliseconds),
                                           Schedules.AutoServerStart,
                                           null));
        Scheduler.AddItem(new ScheduleItem("BotVersionChecker",
                                           Localization.Get("sch_name_botnewversioncchecker"),
                                           Convert.ToUInt64(TimeSpan.FromMinutes(5).TotalMilliseconds),
                                           Schedules.BotVersionChecker,
                                           null));
        Localization.AddSchedule();
        Scheduler.Start(1000);
        
    #if !DEBUG
        if (BotSettings.BotFeatureSettings.AutoServerStart)
        {
            ServerUtility.ServerProcess = ServerUtility.Commands.StartServer();
        }
    #endif

        Client   = new DiscordSocketClient(new DiscordSocketConfig() { GatewayIntents = GatewayIntents.All });
        Commands = new CommandService();
        Services = null;
        CommandHandler = new CommandHandler(Client, Commands, Services);

        await CommandHandler.SetupAsync();
        await Client.LoginAsync(TokenType.Bot, DiscordUtility.GetToken());
        await Client.SetGameAsync("chiteros en el servidor", type: ActivityType.Watching);
        await Client.StartAsync();

        // Iniciar Discord Rich Presence (RPC)
        //InitializeRichPresence();

        // Ejecutar el bucle en segundo plano para actualizar el Rich Presence dinámicamente
        //_ = Task.Run(SetBotPresence);


        DiscordUtility.OrganizeCommands();

        Client.Ready += async () =>
        {
            if(!botInitialCheck)
            {
                botInitialCheck = true;

                await DiscordUtility.DoChannelCheck();
                //await BotUtility.NotifyLatestBotVersion();
                await Localization.CheckUpdate();

            }
        };

        Client.Disconnected += async (ex) =>
        {
            Logger.LogException(ex);
            Logger.LogException(ex.InnerException);

            if(ex.InnerException.Message.Contains("Authentication failed"))
            {
                Console.WriteLine(Localization.Get("err_disc_auth_fail"));
                await Task.Delay(-1);
            }
            //else Console.WriteLine(Localization.Get("err_disc_disconn").KeyFormat(("log_file", Logger.LogFile), ("repo_url", BotRepoURL)));
        };

        await Task.Delay(-1);
    }

    private static async Task SetBotPresence()
    {
        await Client.SetGameAsync("Administrando en el servidor", type: ActivityType.CustomStatus);
    }

    private static void InitializeRichPresence()
    {
        _rpcClient = new DiscordRpcClient(DiscordUtility.GetToken());
        _rpcClient.Initialize();
        SetRichPresence();
    }

    private static void SetRichPresence()
    {
        _rpcClient.SetPresence(new RichPresence()
        {
            Details = "Administración",
            State = "Supervisando",
            Assets = new Assets()
            {
                LargeImageKey = "embedded_background",
                LargeImageText = "Shelter Of Wisdom - Project Zomboid Server",
                SmallImageKey = "shelter_of_wisdom_logo",
                SmallImageText = "Shelter Of Wisdom Agent"
            },
            Buttons = new Button[]
            {
                new Button() { Label = "Únete a mi Discord", Url = "https://discord.gg/SPupyQgD" }
            }
        });

        //Console.WriteLine("Rich Presence activado!");
    }

    private static async Task UpdateRichPresenceLoop()
    {
        while (true)
        {
            _rpcClient.SetPresence(new RichPresence()
            {
                Details = "Administración",
                State = "Supervisando",
                Assets = new Assets()
                {
                    LargeImageKey = "embedded_background",
                    LargeImageText = "Shelter Of Wisdom - Project Zomboid Server",
                    SmallImageKey = "shelter_of_wisdom_logo",
                    SmallImageText = "Shelter Of Wisdom Agent"
                },
                Buttons = new Button[]
                {
                    new Button() { Label = "Únete a mi Discord", Url = "https://discord.gg/SPupyQgD" }
                }
            });

            _rpcClient.Invoke(); // Mantiene la conexión activa
            await Task.Delay(5000); // Espera 5 segundos antes de la próxima actualización
        }
    }
}
