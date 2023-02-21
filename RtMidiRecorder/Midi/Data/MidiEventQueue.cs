using Microsoft.Extensions.Logging;
using RtMidi.Net;
using RtMidi.Net.Events;

namespace RtMidiRecorder.Midi.Data;

public class MidiEventQueue : IMidiEventCollector
{
   readonly Queue<RtMidiEvent> _midiEvents = new();
   readonly ILogger _logger;

   public MidiEventQueue(ILogger<MidiEventQueue> logger)
   {
      _logger = logger;
   }

   public void Add(MidiMessageReceivedEventArgs eventArgs)
   {
      _logger.LogDebug($"{eventArgs.Timestamp}: {eventArgs.Message}");
      var mEvent = new RtMidiEvent
      {
         MessageType = eventArgs.Message.Type,
         Time = eventArgs.Timestamp
      };

      if (eventArgs.Message is MidiMessageNote messageNote)
      {
         mEvent.Note = messageNote.Note;
         mEvent.Velocity = messageNote.Velocity;
         mEvent.Channel = (uint)messageNote.Channel;
      }

      _midiEvents.Enqueue(mEvent);
   }

   public void Clear()
   {
      _midiEvents.Clear();
   }

   public RtMidiEvent[] Collect(bool clear = true)
   {
      var enumerable = _midiEvents.ToArray();
      if (clear) Clear();
      return enumerable;
   }

   public bool HasEvents()
   {
      return _midiEvents.Count > 0;
   }
}