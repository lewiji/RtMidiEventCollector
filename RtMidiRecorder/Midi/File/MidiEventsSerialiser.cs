using Hearn.Midi;
using RtMidi.Net.Enums;
using RtMidiRecorder.Midi.Data;

namespace RtMidiRecorder.Midi.File;

public class MidiEventsSerialiser : IMidiEventsSerialiser
{
   public void WriteEventsToFile(string path, RtMidiEvent[] events)
   {
      var midiStream = new FileStream(path, FileMode.OpenOrCreate);
      
      using var midiStreamWriter = new MidiStreamWriter(midiStream);
      
      long duration = 0;
      midiStreamWriter
         .WriteHeader(MidiConstants.Formats.MultiSimultaneousTracks, 2);

      midiStreamWriter
         .WriteStartTrack()
         .WriteTimeSignature(4, 4)
         .WriteTempo(120)
         .WriteString(MidiConstants.StringTypes.TrackName, "CollectedEvents")
         .WriteEndTrack();

      midiStreamWriter.WriteStartTrack();
      var notesOn = events.Where(e => e.MessageType == MidiMessageType.NoteOn);
      var notesOffList =
         events.Where(e => e.MessageType == MidiMessageType.NoteOff).ToList();
      foreach (var noteOnEvent in notesOn)
      {
         if (noteOnEvent.MessageType == MidiMessageType.NoteOn)
         {
            var noteOff = notesOffList.FirstOrDefault(e =>
               e.MessageType == MidiMessageType.NoteOff && e.Note.GetByteRepresentation() == noteOnEvent.Note.GetByteRepresentation());
            var noteLength = (noteOnEvent.Time.Duration().Ticks - noteOff.Time.Duration().Ticks) / 120 / 60;

            midiStreamWriter.WriteNote(
               (byte)noteOnEvent.Channel,
               (MidiConstants.MidiNoteNumbers)noteOnEvent.Note.GetByteRepresentation(),
               (byte)noteOnEvent.Velocity,
               noteLength);
            
            midiStreamWriter.Tick(noteOnEvent.Time.Duration().Ticks / 120 / 60);
            notesOffList.Remove(noteOff);
         }
      }

      midiStreamWriter.WriteEndTrack();
   }
}