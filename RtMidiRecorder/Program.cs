using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RtMidiRecorder.Midi;
using RtMidiRecorder.Midi.Data;
using RtMidiRecorder.Midi.File;

namespace RtMidiRecorder;

internal sealed class Program
{
   static async Task Main(string[] args)
   {
      await Host.CreateDefaultBuilder(args)
         .UseSystemd()
         .UseContentRoot(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!)
         .ConfigureServices((context, collection) =>
         {
            collection
               .AddHostedService<MidiInputService>()
               .AddScoped<IMidiEventCollector, MidiEventQueue>()
               .AddScoped<IMidiEventsSerialiser, MidiEventsSerialiser>();

            collection.AddOptions<MidiSettings>()
               .Bind(context.Configuration.GetSection("Midi"));

            collection.AddLogging();
         })
         .RunConsoleAsync();
   }
}