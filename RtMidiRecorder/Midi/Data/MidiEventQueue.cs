using Microsoft.Extensions.Logging;
using RtMidi.Net;
using RtMidi.Net.Events;
using RtMidiRecorder.Midi.Extensions;

namespace RtMidiRecorder.Midi.Data;

public class MidiEventQueue : IMidiEventCollector
{
   readonly Queue<RtMidiEvent> _midiEvents = new();

   public void Add(MidiMessageReceivedEventArgs eventArgs)
   {
      var mEvent = new RtMidiEvent {
         MessageType = (byte)eventArgs.Message.Type,
         Time = eventArgs.Timestamp.MidiTicks()
      };

      if (eventArgs.Message is MidiMessageNoteBase messageNote)
      {
         mEvent.Note = messageNote.Note.GetByteRepresentation();

         if (messageNote is MidiMessageNote note)
            mEvent.Velocity = note.Velocity;
         else if (messageNote is MidiMessageNoteAfterTouch afterTouch) mEvent.Velocity = afterTouch.Pressure;
         mEvent.Channel = (uint) messageNote.Channel;
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