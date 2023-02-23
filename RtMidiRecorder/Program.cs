using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeanutButter.EasyArgs;
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
         var opts = args.ParseTo<ICliArgs>();
         var builder = Host.CreateDefaultBuilder(args)
            .UseSystemd()
            //.UseContentRoot(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!)
            .ConfigureServices((context, collection) =>
            {
               collection
                  .AddHostedService<MidiInputService>()
                  .AddScoped<IMidiEventCollector, MidiEventQueue>()
                  .AddScoped<IMidiEventsSerialiser, MidiEventsSerialiser>()
                  .AddSingleton(opts)
                  .AddLogging()
                  .AddOptions<MidiSettings>()
                  .Bind(context.Configuration.GetSection("Midi"));

               _logger = LoggerFactory.Create(config =>
               {
                  config
                     .AddConsole()
                     .AddConfiguration(context.Configuration.GetSection("Logging"));
               }).CreateLogger("Program");
            });


         await builder.RunConsoleAsync();
      }
      catch (TaskCanceledException)
      {
         _logger?.LogInformation(ConsoleMessages.Task_canceled_shutting_down);
      }
   }
}