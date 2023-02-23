using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RtMidiRecorder.Midi;
using RtMidiRecorder.Midi.Configuration;
using RtMidiRecorder.Midi.Data;
using RtMidiRecorder.Midi.File;

namespace RtMidiRecorder;

internal sealed class Program
{
   static ILogger? _logger;

   static async Task Main(string[] args)
   {
      try
      {
         var cmd = BuildCommandLine()
            .UseHost(_ => Host.CreateDefaultBuilder(args), builder => {
               builder.UseSystemd()
                  .UseContentRoot(AppContext.BaseDirectory)
                  .ConfigureServices((context, collection) => {
                     collection.AddHostedService<MidiInputService>()
                        .AddScoped<IMidiEventCollector, MidiEventQueue>()
                        .AddScoped<IMidiEventsSerialiser, MidiEventsSerialiser>()
                        .AddLogging()
                        .AddOptions<MidiSettings>()
                        .Bind(context.Configuration.GetSection("Midi"))
                        .Configure<BindingContext, ParseResult>((opts, bindingContext, parseResult) => {
                           foreach (var symbolResult in parseResult.RootCommandResult.Children)
                           {
                              if (symbolResult is OptionResult optionResult)
                              {
                                 switch (optionResult.Option.Name)
                                 {
                                    case "port":
                                       var port = optionResult.GetValueOrDefault<uint?>();
                                       if (port != null)
                                       {
                                          opts.DevicePort = port.Value;
                                       }
                                       break;
                                    case "idle-timeout":
                                       var idle = optionResult.GetValueOrDefault<uint?>();
                                       if (idle != null)
                                       {
                                          opts.IdleTimeoutSeconds = idle.Value;
                                       }
                                       break;
                                    case "channel":
                                       var channel = optionResult.GetValueOrDefault<uint?>();
                                       if (channel != null)
                                       {
                                          opts.Channel = channel.Value;
                                       }
                                       break;
                                    case "drum-mode":
                                       var drumMode = optionResult.GetValueOrDefault<bool?>();
                                       if (drumMode != null)
                                       {
                                          opts.DrumMode = drumMode.Value;
                                       }
                                       break;
                                 }
                              }
                           }
                        });

                     _logger = LoggerFactory.Create(config => {
                           config.AddConsole().AddConfiguration(context.Configuration.GetSection("Logging"));
                        })
                        .CreateLogger("Program");
                  });
            })
            .UseDefaults()
            .Build();

         await cmd.InvokeAsync(args);
      }
      catch (TaskCanceledException)
      {
         _logger?.LogInformation(ConsoleMessages.Task_canceled_shutting_down);
      }
   }

   static CommandLineBuilder BuildCommandLine()
   {
      var devicePortOption = new Option<uint?>(new[] {"--port", "-p"}, "Device port for MIDI input.");

      var idleTimeoutOption = new Option<uint?>(new[] {"--idle-timeout", "-i"}, "How long (in seconds) to wait for silence before outputting the collected MIDI events.");

      var channelOption = new Option<uint?>(new[] {"--channel", "-c"},          "Notes in the MIDI output will have their channel overridden by this value if set.");

      var drumModeOption = new Option<bool?>(new[] {"--drum-mode", "-d"},          "Force the MIDI inputs to be treated as percussion events.");

      RootCommand rootCommand = new($@"RtMidiRecorder is an automated MIDI device capture service/daemon.") {
         devicePortOption,
         idleTimeoutOption,
         channelOption,
         drumModeOption
      };
      
      return new CommandLineBuilder(rootCommand);
   }
}