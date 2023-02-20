using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RtMidiRecorder.Midi;

internal sealed class MidiInputService : BackgroundService
{
   readonly IHostApplicationLifetime _appLifetime;
   readonly ILogger _logger;
   readonly IMidiDeviceWorker _midiDeviceWorker;
   int? _exitCode;

   public MidiInputService(ILogger<MidiInputService> logger,
      IHostApplicationLifetime appLifetime,
      IMidiDeviceWorker deviceWorker)
   {
      _logger = logger;
      _appLifetime = appLifetime;
      _midiDeviceWorker = deviceWorker;
   }

   protected override Task ExecuteAsync(CancellationToken stoppingToken)
   {
      var ver = GetType().Assembly.GetName().Version?.ToString() ?? "prerelease";
      _logger.LogInformation($"RtMidiRecorder {ver}");
      _appLifetime.ApplicationStarted.Register(() => Task.Run(TryConnectToMidiInput, stoppingToken));
      
      return Task.Delay(-1, stoppingToken);
   }

   public override Task StopAsync(CancellationToken cancellationToken)
   {
      _logger.LogInformation($"Exiting with return code: {_exitCode}");
      _appLifetime.ApplicationStopping.Register(() =>
      {
         Task.Run(_midiDeviceWorker.Stop, cancellationToken);
      });
      Environment.ExitCode = _exitCode.GetValueOrDefault(-1);
      return Task.CompletedTask;
   }

   async Task TryConnectToMidiInput()
   {
      try
      {
         _logger.LogDebug("Starting MIDI device worker...");
         await _midiDeviceWorker.ConnectDevice();
         _exitCode = 0;
      }
      catch (Exception ex)
      {
         _logger.LogError(
            ex, "Unhandled exception when initiating MIDI device connection"
         );
         _exitCode = 1;
         
         _logger.LogDebug("Stopping service...");
         _appLifetime.StopApplication();
      }
   }
}