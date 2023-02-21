using System.Timers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RtMidi.Net;
using RtMidi.Net.Enums;
using RtMidi.Net.Events;
using RtMidiRecorder.Midi.Data;
using RtMidiRecorder.Midi.File;
using Timer = System.Timers.Timer;

namespace RtMidiRecorder.Midi;

internal sealed class MidiInputService : IHostedService, IMidiDeviceWorker, IDisposable
{
   readonly IHostApplicationLifetime _appLifetime;
   readonly ILogger _logger;
   readonly IMidiEventCollector _midiEventCollector;
   readonly IMidiEventsSerialiser _midiEventsSerialiser;
   readonly IOptions<MidiSettings> _midiSettings;
   readonly Timer _idleTimer;
   int? _exitCode;

   MidiInputClient? _midiInputClient;

   public MidiInputService(ILogger<MidiInputService> logger,
      IHostApplicationLifetime appLifetime,
      IMidiEventCollector eventCollector,
      IMidiEventsSerialiser eventsSerialiser,
      IOptions<MidiSettings> midiSettings)
   {
      _logger = logger;
      _appLifetime = appLifetime;
      _midiEventCollector = eventCollector;
      _midiEventsSerialiser = eventsSerialiser;
      _midiSettings = midiSettings;
      _idleTimer = new Timer(_midiSettings.Value.IdleTimeoutSeconds * 1000.0);
   }

   public Task StartAsync(CancellationToken cancellationToken)
   {
      _logger.LogInformation($"RtMidiRecorder {GetType().Assembly.GetName().Version!.ToString()}");
      Task.Run(TryConnectToMidiInput, cancellationToken);
      return Task.Delay(-1, cancellationToken);
   }

   public Task StopAsync(CancellationToken cancellationToken)
   {
      _logger.LogInformation(string.Format(ConsoleMessages.Exiting_with_return_code, _exitCode));
      Environment.ExitCode = _exitCode.GetValueOrDefault(-1);
      Dispose();
      return Task.CompletedTask;
   }

   async Task TryConnectToMidiInput()
   {
      try
      {
         _logger.LogDebug(ConsoleMessages.Starting_MIDI_device_worker);
         await InitiateMidiDeviceConnection();
         _exitCode = 0;
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, ConsoleMessages.Exception_initiating_MIDI_device);
         _exitCode = 1;
         _appLifetime.StopApplication();
      }
   }

   public Task InitiateMidiDeviceConnection()
   {
      var devicePort = RequestDevicePortInputLoop();
      var device = MidiManager.GetDeviceInfo(devicePort, MidiDeviceType.Input);

      _midiInputClient = new MidiInputClient(device);
      _midiInputClient.OnMessageReceived += OnMidiMessageReceived;
      _midiInputClient.ActivateMessageReceivedEvent();
      _midiInputClient.Open();
      _logger.LogInformation(string.Format(ConsoleMessages.Connected_to_Midi_Input_, device.Name));

      _idleTimer.Elapsed += OnIdleTimerElapsed;
      _logger.LogInformation(string.Format(ConsoleMessages.Started_idle_timer_, _idleTimer.Interval));

      return Task.CompletedTask;
   }

   void OnIdleTimerElapsed(object? sender, ElapsedEventArgs args)
   {
      if (!_midiEventCollector.HasEvents()) return;
      _idleTimer.Stop();
      
      var elapsedEvents = _midiEventCollector.Collect();
      _logger.LogInformation(string.Format(ConsoleMessages.Collected__n__events_, elapsedEvents.Length));
      
      _midiEventsSerialiser.WriteEventsToFile($"{DateTime.Now:yyyy-dd-M--HH-mm-ss}.mid", elapsedEvents);
   }


   uint RequestDevicePortInputLoop()
   {
      uint? devicePort = null;
      while (devicePort == null)
         try
         {
            devicePort = RequestDevicePort().Result;
         }
         catch (Exception e)
         {
            devicePort = null;
            _logger.LogError(e, e.Message);
         }

      return devicePort.Value;
   }

   public Task<uint> RequestDevicePort()
   {
      var devices = MidiManager.GetAvailableDevices();
      Console.WriteLine(ConsoleMessages.Heading_Midi_DeviceList);
      foreach (var midiDeviceInfo in devices.Where(midiDeviceInfo =>
                  midiDeviceInfo.Type == MidiDeviceType.Input))
         Console.WriteLine($@"{midiDeviceInfo.Port}: {midiDeviceInfo.Name}");

      Console.WriteLine(@"------------------------ ");
      Console.WriteLine(ConsoleMessages.Prompt_DevicePort_Entry);
      var portInput = Console.ReadLine();

      if (portInput == null) throw new Exception(ConsoleMessages.DevicePort_Invalid_null);

      uint? devicePort;
      try
      {
         devicePort = Convert.ToUInt32(portInput);
      }
      catch (Exception e)
      {
         throw new Exception(string.Format(ConsoleMessages.DevicePort_Conversion_Failed, portInput, e.Message));
      }

      _midiSettings.Value.DevicePort = devicePort.Value;
      return Task.FromResult(devicePort.Value);
   }

   void OnMidiMessageReceived(object? midiClient, MidiMessageReceivedEventArgs eventArgs)
   {
      _midiEventCollector.Add(eventArgs);

      if (_idleTimer is { Enabled: false })
      {
         _logger.LogInformation(ConsoleMessages.MIDI_events_detected);
         _idleTimer.Start();
      }
      else
      {
         // Always restart timer if a new event is received and it's Enabled
         _idleTimer.Stop();
         _idleTimer.Start();
      }
   }

   public void Dispose()
   {
      _midiInputClient?.Close();
      _midiInputClient?.Dispose();
   }
}