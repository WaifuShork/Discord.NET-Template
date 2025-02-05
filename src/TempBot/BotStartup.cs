﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Addons.Hosting;
using Discord.Addons.Interactive;

using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using TempBot.Services;

using Serilog;
using Serilog.Events;
using TempBot.Infrastructure;
using TempBot.Infrastructure.Models.Impl;

namespace TempBot
{
    internal static class TemplateBot
    {
        public static async Task<int> RunAsync()
        {
            // Migrate the database before even starting to login or boot
            await MigrateAsync();
            
            var path = Path.Combine("logs", "log.txt");
            
            // Create a new logger that appends a new log file to `logs/log.txt' with the date appended to the end every 24hrs
            // Also sends the same logs to the console
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Error)
                .WriteTo.Async(cf =>
                {
                    cf.Console();
                    cf.File(path, LogEventLevel.Verbose, rollingInterval: RollingInterval.Day);
                })
                .CreateLogger();

            try
            {
                // You can call Log from anywhere, it's a static property in Serilog
                Log.Information("Starting Template Bot");
                await CreateDefaultBuilder().Build().RunAsync();
                return 0;
            }
            catch (Exception exception)
            {
                Log.Fatal(exception, "Host terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static IHostBuilder CreateDefaultBuilder()
        {
            return Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureAppConfiguration(x =>
                {
                    var configuration = new ConfigurationBuilder()
                        .SetBasePath(Directory.GetCurrentDirectory())
                        // path, is it optional?, reload on change?
                        .AddJsonFile("appsettings.json", false, true)
                        .Build();

                    x.AddConfiguration(configuration);
                })
                .ConfigureDiscordHost((context, config) => 
                {
                    config.SocketConfig = new DiscordSocketConfig
                    {
                        LogLevel = LogSeverity.Verbose,
                        AlwaysDownloadUsers = true,
                        MessageCacheSize = 200,
                    };
                    
                    config.Token = context.Configuration["token"];
                })
                .UseCommandService((_, config) =>
                {
                    config.CaseSensitiveCommands = false;
                    config.LogLevel = LogSeverity.Verbose;
                    config.DefaultRunMode = RunMode.Async;
                })
                .ConfigureServices((_, services) =>
                {
                    services.AddHostedService<CommandHandler>()
                            .AddHostedService<StartupService>();
                    
                    services.AddSingleton<GuildConfigs>()
                            .AddSingleton<InteractiveService>();
                    
                    services.AddDbContext<TemplateBotContext>();
                })
                .UseConsoleLifetime();
        }

        public static async Task MigrateAsync()
        {
            var context = new TemplateBotContext();
            var migrations = await context.Database.GetPendingMigrationsAsync();
            if (migrations.Any())
            {
                await context.Database.MigrateAsync();
            }
        }
    }
}