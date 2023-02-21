using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.CompilerServices;
using RtMidiRecorder.Midi;
using RtMidiRecorder.Midi.Data;
using RtMidiRecorder.Midi.File;

namespace RtMidiRecorder;

internal static class Program
{
   static async Task Main(string[] args)
   {
      try
      {
         await Host.CreateDefaultBuilder(args)
            .UseSystemd()
            .UseContentRoot(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!)
            .ConfigureServices((context, collection) =>
            {
               collection
                  .AddHostedService<MidiInputService>()
                  .AddScoped<IMidiEventCollector, MidiEventQueue>()
                  .AddScoped<IMidiEventsSerialiser, MidiEventsSerialiser>()
                  .AddLogging()
                  .AddOptions<MidiSettings>()
                  .Bind(context.Configuration.GetSection("Midi"));
            })
            .RunConsoleAsync();
      }
      catch (TaskCanceledException)
      {
         Console.WriteLine(ConsoleMessages.Task_canceled_shutting_down);
      }
   }
}