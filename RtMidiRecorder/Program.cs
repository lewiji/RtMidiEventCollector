using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
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
                        .Bind(context.Configuration.GetSection("Midi"));

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
      var devicePortOption = new Option<uint?>(new[] {"--port", "-p"}, () => null, "Device port for MIDI input");

      RootCommand rootCommand = new($@"RtMidiRecorder is an automated MIDI device capture service/daemon.") {
         devicePortOption
      };

      return new CommandLineBuilder(rootCommand);
   }
}