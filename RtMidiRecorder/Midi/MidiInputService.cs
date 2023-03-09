using System.Timers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RtMidi.Net;
using RtMidi.Net.Enums;
using RtMidi.Net.Events;
using RtMidiRecorder.Midi.Configuration;
using RtMidiRecorder.Midi.Data;
using RtMidiRecorder.Midi.Extensions;
using RtMidiRecorder.Midi.File;
using Timer = System.Timers.Timer;

namespace RtMidiRecorder.Midi;

internal sealed class MidiInputService : IHostedService, IMidiDeviceWorker, IDisposable
{
   readonly IHostApplicationLifetime _appLifetime;
   readonly Queue<double> _clockEvents = new();
   readonly Timer _idleTimer;
   readonly ILogger _logger;
   readonly IMidiEventCollector _midiEventCollector;
   readonly IMidiEventsSerialiser _midiEventsSerialiser;
   readonly IOptions<MidiSettings> _midiSettings;
   double _currentTempo = 120.0;
   int? _exitCode;

   MidiInputClient? _midiInputClient;
   bool _resetClock;
   double _usSinceLastClock;

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

   public Task InitiateMidiDeviceConnection()
   {
      try
      {
         var devicePort = RequestDevicePortInputLoop();
         var device = MidiManager.GetDeviceInfo(devicePort, MidiDeviceType.Input);

         _midiInputClient = new MidiInputClient(device);
         _midiInputClient.IgnoreTimeMessages = false;
         _midiInputClient.OnMessageReceived += OnMidiMessageReceived;
         _midiInputClient.ActivateMessageReceivedEvent();
         _midiInputClient.Open();
         _logger.LogInformation(string.Format(ConsoleMessages.Connected_to_Midi_Input_, device.Name));
      }
      catch (Exception e)
      {
         return Task.FromException(e);
      }

      _idleTimer.Elapsed += OnIdleTimerElapsed;
      _logger.LogInformation(string.Format(ConsoleMessages.Started_idle_timer_, _idleTimer.Interval));

      return Task.CompletedTask;
   }

   public Task<uint> RequestDevicePort()
   {
      var devices = MidiManager.GetAvailableDevices();
      _logger.LogInformation(ConsoleMessages.Heading_Midi_DeviceList);

      foreach (var midiDeviceInfo in devices.Where(midiDeviceInfo => midiDeviceInfo.Type == MidiDeviceType.Input))
         _logger.LogInformation($"{midiDeviceInfo.Port}: {midiDeviceInfo.Name}");


      _logger.LogInformation("------------------------");
      _logger.LogInformation(ConsoleMessages.Prompt_DevicePort_Entry);
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

   public void Dispose()
   {
      _logger.LogInformation("MidiInputService being disposed.");
      _midiInputClient?.Close();
      _midiInputClient?.Dispose();
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

   void OnIdleTimerElapsed(object? sender, ElapsedEventArgs args)
   {
      if (!_midiEventCollector.HasEvents()) return;
      _idleTimer.Stop();

      var elapsedEvents = _midiEventCollector.Collect();
      var rtMidiEvents = elapsedEvents as RtMidiEvent[] ?? elapsedEvents.ToArray();
      _logger.LogInformation(string.Format(ConsoleMessages.Collected__n__events_, rtMidiEvents.Count()));

      _midiEventsSerialiser.WriteEventsToFile(
         Path.Combine(_midiSettings.Value.FilePath ?? "", $"{DateTime.Now:yyyy-dd-M--HH-mm-ss}.mid"),
         rtMidiEvents, _currentTempo);

      if (_midiInputClient != null)
         _midiInputClient.IgnoreTimeMessages = false;
      else
         _logger.LogError("_midiInputClient is null");
   }

   uint RequestDevicePortInputLoop()
   {
      uint? devicePort = null;

      while (devicePort == null)
         try
         {
            devicePort = _midiSettings.Value.DevicePort ?? RequestDevicePort().Result;
         }
         catch (Exception e)
         {
            devicePort = null;

            if (e is DllNotFoundException)
            {
               _logger.LogError(ConsoleMessages.Rtmidi_native_library_not_found);
               throw;
            }

            _logger.LogError(e, e.Message);
         }

      return devicePort.Value;
   }

   void OnMidiMessageReceived(object? midiClient, MidiMessageReceivedEventArgs eventArgs)
   {
      if (eventArgs.Message.Type == MidiMessageType.TimingClock)
      {
         ProcessMidiClocks(eventArgs);
         return;
      }

      _usSinceLastClock += eventArgs.Timestamp.Ticks;

      if (_idleTimer is { Enabled: false })
      {
         StartIdleTimerAndRecording();
      }
      else
      {
         // Always restart timer if a new event is received and it's Enabled
         _idleTimer.Stop();
         _idleTimer.Start();
      }

      _midiEventCollector.Add(eventArgs);
   }

   void StartIdleTimerAndRecording()
   {
      _logger.LogInformation(ConsoleMessages.MIDI_events_detected);

      if (_midiInputClient != null)
         _midiInputClient.IgnoreTimeMessages = true;

      _idleTimer.Start();
      _clockEvents.Clear();
      _resetClock = true;
      _usSinceLastClock = 0;
   }

   void ProcessMidiClocks(MidiMessageReceivedEventArgs eventArgs)
   {
      var uSecs = ResetClocks(eventArgs.Timestamp.TotalMicroseconds + _usSinceLastClock);
      if (uSecs < 1000)
         return;

      if (_clockEvents.Count >= MidiSettings.PpqDevice && Math.Abs(uSecs - _clockEvents.Median()) > 1600.0)
         _clockEvents.Clear();

      _clockEvents.Enqueue(uSecs);
      if (_clockEvents.Count < MidiSettings.PpqDevice * 4)
         return;

      var clockBpm = CalculateClockBpm();
      if (clockBpm < 0.5 || Math.Abs(clockBpm - _currentTempo) < 0.499 )
         return;

      
      _currentTempo = Math.Round(clockBpm * 2, MidpointRounding.AwayFromZero) / 2;
      _logger.LogInformation($"Current tempo: {_currentTempo}");
   }

   double CalculateClockBpm()
   {
      var sum = 0.0;
      double? lastClock = null;

      while (_clockEvents.Count > 0)
      {
         var currClock = _clockEvents.Dequeue();

         if (lastClock != null)
            sum += (lastClock.Value + currClock) / 2.0;

         lastClock = currClock;
      }

      var avgUSecs = sum / ((MidiSettings.PpqDevice * 4) - 1);
      var clockBpm = MidiSettings.USecPerMinute / MidiSettings.PpqDevice / avgUSecs;
      return clockBpm;
   }

   double ResetClocks(double ticks)
   {
      _usSinceLastClock = 0;

      if (!_resetClock)
         return ticks;

      _resetClock = false;
      ticks = 0;
      return ticks;
   }
}