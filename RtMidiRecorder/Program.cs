using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
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

   static readonly Option[] Options =
   {
      new Option<uint?>(new[] { "--port", "-p" }, ConsoleMessages.Option_Device_port_for_MIDI_input_),
      new Option<uint?>(new[] { "--idle-timeout", "-i" }, ConsoleMessages.Option_Idle_timeout),
      new Option<uint?>(new[] { "--channel", "-c" }, ConsoleMessages.Option_Channel_Override),
      new Option<bool?>(new[] { "--drum-mode", "-d" }, ConsoleMessages.Option_Drum_mode),
      new Option<string?>(new[] { "--filepath", "-f" }, ConsoleMessages.FilePathOption_Path_to_output)
   };

   static async Task Main(string[] args)
   {
      try
      {
         var cmd = BuildCommandLine()
            .UseHost(_ => Host.CreateDefaultBuilder(args),
               builder =>
               {
                  builder
                     .UseSystemd()
                     .UseContentRoot(AppContext.BaseDirectory)
                     .ConfigureServices(ConfigureHostServices);
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

   static void ConfigureHostServices(HostBuilderContext context, IServiceCollection collection)
   {
      collection.AddHostedService<MidiInputService>()
         .AddScoped<IMidiEventCollector, MidiEventQueue>()
         .AddScoped<IMidiEventsSerialiser, MidiEventsSerialiser>()
         .AddLogging()
         .AddOptions<MidiSettings>()
         .Bind(context.Configuration.GetSection("Midi"))
         .Configure<BindingContext, ParseResult>(ConfigureCommandLineOptions);

      _logger = LoggerFactory.Create(config =>
         {
            config.AddConsole().AddConfiguration(context.Configuration.GetSection("Logging"));
         })
         .CreateLogger("Program");
   }

   static void ConfigureCommandLineOptions(MidiSettings opts, BindingContext bindingContext, ParseResult parseResult)
   {
      foreach (var option in Options)
      {
         if (parseResult.GetValueForOption(option) is not { } result) continue;

         _logger?.LogInformation($"Using {option.Name} value from cli: {result}");
         opts.SetOption(option.Name, result);
      }
   }

   static CommandLineBuilder BuildCommandLine()
   {
      RootCommand rootCommand = new(@"RtMidiRecorder is an automated MIDI device capture service/daemon.");

      foreach (var option in Options) rootCommand.AddOption(option);

      return new CommandLineBuilder(rootCommand);
   }
}