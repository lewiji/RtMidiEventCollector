using System.Timers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RtMidi.Net;
using RtMidi.Net.Enums;
using RtMidi.Net.Events;
using RtMidiRecorder.Midi.Configuration;
using RtMidiRecorder.Midi.Data;
using RtMidiRecorder.Midi.File;
using Timer = System.Timers.Timer;

namespace RtMidiRecorder.Midi;

internal sealed class MidiInputService : IHostedService, IMidiDeviceWorker, IDisposable
{
   readonly IHostApplicationLifetime _appLifetime;
   readonly Queue<long> _clockEvents = new();
   readonly Timer _idleTimer;
   readonly ILogger _logger;
   readonly IMidiEventCollector _midiEventCollector;
   readonly IMidiEventsSerialiser _midiEventsSerialiser;
   readonly IOptions<MidiSettings> _midiSettings;
   readonly Queue<double> _tempoBuffer = new();
   double _currentTempo = 120.0;
   int? _exitCode;

   MidiInputClient? _midiInputClient;
   bool _resetClock;
   long _ticksSinceLastClock;

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

      _ticksSinceLastClock += eventArgs.Timestamp.Ticks;

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
      _tempoBuffer.Clear();
      _resetClock = true;
      _ticksSinceLastClock = 0;
   }

   void ProcessMidiClocks(MidiMessageReceivedEventArgs eventArgs)
   {
      var ticks = ProcessTicksAndClocks(eventArgs.Timestamp.Ticks + _ticksSinceLastClock);
      if (ticks < 1000)
         return;

      _clockEvents.Enqueue(ticks);
      if (_clockEvents.Count < MidiSettings.PpqDevice)
         return;

      var clockBpm = CalculateClockBpm();
      if (clockBpm <= 0)
         return;

      var tempoBufferAvg = _tempoBuffer.Count > 0 ? _tempoBuffer.Average() : 0;
      EnqueueClockBpm(clockBpm, tempoBufferAvg);
      CalculateTempoFromBuffer(tempoBufferAvg);
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

      var clockBpm = sum / (MidiSettings.PpqDevice - 1.0);
      return clockBpm;
   }

   long ProcessTicksAndClocks(long ticks)
   {
      _ticksSinceLastClock = 0;

      if (!_resetClock)
         return ticks;

      _resetClock = false;
      ticks = 0;
      return ticks;
   }

   void EnqueueClockBpm(double avg, double tempoBufferAvg)
   {
      var bpm = MidiSettings.USecPerMinute * 10.0 / MidiSettings.PpqDevice / avg;

      if (_tempoBuffer.Count > 0 && Math.Abs(bpm - tempoBufferAvg) > tempoBufferAvg / 180.0)
         // Discard buffered tempos if changing too much (higher tempos have a bigger change threshold than lower tempos)
         _tempoBuffer.Clear();

      _tempoBuffer.Enqueue(bpm);
   }

   void CalculateTempoFromBuffer(double tempoBufferAvg)
   {
      if (_tempoBuffer.Count < 2)
         return;

      var tempoBufferLength = Math.Min(15, Math.Max(tempoBufferAvg / 50.0, 2));

      if (_tempoBuffer.Count < tempoBufferLength)
         return;

      var tempoBufferSize = _tempoBuffer.Count;
      var tempoSum = 0.0;

      while (_tempoBuffer.Count > 0) tempoSum += _tempoBuffer.Dequeue();

      var bufferAvg = tempoSum / tempoBufferSize;

      // Discard if average is too close to the current tempo
      if (Math.Abs(bufferAvg - _currentTempo) < 0.35 || bufferAvg < 0.5)
         return;

      // Round to nearest .5
      _currentTempo = Math.Round(bufferAvg * 2, MidpointRounding.AwayFromZero) / 2;
      _logger.LogInformation($"Current tempo: {_currentTempo}");
   }
}