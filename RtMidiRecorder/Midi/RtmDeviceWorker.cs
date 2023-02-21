using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RtMidi.Net;
using RtMidi.Net.Enums;
using RtMidiRecorder.Midi.File;
using Timer = System.Timers.Timer;

namespace RtMidiRecorder.Midi;

internal sealed class RtmDeviceWorker : IMidiDeviceWorker, IDisposable
{
   readonly ILogger _logger;
   readonly IOptions<MidiSettings> _midiSettings;
   readonly IMidiEventCollector _midiEventCollector;
   readonly IMidiEventsSerialiser _midiEventsSerialiser;
   MidiInputClient? _midiInputClient;
   Timer _idleTimer;

   public RtmDeviceWorker(
      IMidiEventCollector eventCollector,
      IMidiEventsSerialiser eventsSerialiser,
      IOptions<MidiSettings> midiSettings,
      ILogger<RtmDeviceWorker> logger)
   {
      _midiEventCollector = eventCollector;
      _midiEventsSerialiser = eventsSerialiser;
      _midiSettings = midiSettings;
      _logger = logger;
   }

   public Task ConnectDevice()
   {
      uint? devicePort = null;
      while (devicePort == null)
      {
         try
         {
            devicePort = RequestDevicePort().Result;
         }
         catch (Exception e)
         {
            devicePort = null;
            _logger.LogError(e, e.Message);
         }
      }

      var device = MidiManager.GetDeviceInfo(devicePort.Value, MidiDeviceType.Input);
      _midiInputClient = new MidiInputClient(device);
      
      _midiInputClient.OnMessageReceived += (_, eventArgs) =>
      {
         _logger.LogDebug($"{eventArgs.Timestamp}: {eventArgs.Message}");
         _midiEventCollector.Add(eventArgs);

         if (!_idleTimer.Enabled)
         {
            _logger.LogInformation(
               $"New MIDI inputs detected from idle, starting idle timeout...");
            _idleTimer.Start();
         }
         else
         {
            _idleTimer.Stop();
            _idleTimer.Start();
         }
      };

      _midiInputClient.ActivateMessageReceivedEvent();
      _midiInputClient.Open();
      _logger.LogInformation($"Connected to Midi Input: {device.Name}.");
      
      _idleTimer = new Timer(_midiSettings.Value.IdleTimeoutSeconds * 1000.0);
      _idleTimer.Elapsed += (sender, args) =>
      {
         if (!_midiEventCollector.HasEvents()) return;
         var elapsedEvents = _midiEventCollector.Collect();
         _logger.LogInformation($"Collected {elapsedEvents.Length} events.");
         _idleTimer.Stop();
         
         
         _logger.LogInformation($"Saving events to file...");
         _midiEventsSerialiser.WriteEventsToFile($"{DateTime.Now:yyyy-dd-M--HH-mm-ss}.mid", elapsedEvents);
         _logger.LogInformation($"Saved.");
      };
      _logger.LogInformation($"Set idle detection timer to {_idleTimer.Interval}.");
      
      return Task.CompletedTask;
   }

   public Task Stop()
   {
      Dispose();
      return Task.CompletedTask;
   }

   Task<uint> RequestDevicePort()
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

   public void Dispose()
   {
      _midiInputClient?.Close();
      _midiInputClient?.Dispose();
   }
}