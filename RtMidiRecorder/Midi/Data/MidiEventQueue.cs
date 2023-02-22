using Microsoft.Extensions.Logging;
using RtMidi.Net;
using RtMidi.Net.Events;
using RtMidiRecorder.Midi.Extensions;

namespace RtMidiRecorder.Midi.Data;

public class MidiEventQueue : IMidiEventCollector
{
   readonly ILogger _logger;
   readonly Queue<RtMidiEvent> _midiEvents = new();

   public MidiEventQueue(ILogger<MidiEventQueue> logger)
   {
      _logger = logger;
   }

   public void Add(MidiMessageReceivedEventArgs eventArgs)
   {
      var noteLogMsg = eventArgs.Message is MidiMessageNote noteMsg
         ? $" {noteMsg.Note.GetName()} v{noteMsg.Velocity}"
         : "";
      _logger.LogDebug(
         $"{eventArgs.Timestamp}: {eventArgs.Message.Type}{noteLogMsg} Duration: {eventArgs.Timestamp.MidiTicks()}");
      var mEvent = new RtMidiEvent
      {
         MessageType = eventArgs.Message.Type,
         Time = eventArgs.Timestamp
      };

      if (eventArgs.Message is MidiMessageNoteBase messageNote)
      {
         mEvent.Note = messageNote.Note;
         if (messageNote is MidiMessageNote note)
            mEvent.Velocity = note.Velocity;
         else if (messageNote is MidiMessageNoteAfterTouch afterTouch) mEvent.Velocity = afterTouch.Pressure;
         mEvent.Channel = (uint)messageNote.Channel;
      }

      _midiEvents.Enqueue(mEvent);
   }

   public void Clear()
   {
      _midiEvents.Clear();
   }

   public IEnumerable<RtMidiEvent> Collect(bool clear = true)
   {
      Queue<RtMidiEvent> collected = new(_midiEvents);
      if (clear)
         Clear();
      return collected;
   }

   public bool HasEvents()
   {
      return _midiEvents.Count > 0;
   }
}