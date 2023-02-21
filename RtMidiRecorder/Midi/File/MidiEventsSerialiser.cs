using Hearn.Midi;
using RtMidi.Net.Enums;

namespace RtMidiRecorder.Midi.File;

public class MidiEventsSerialiser : IMidiEventsSerialiser
{
   public void WriteEventsToFile(string path, RtMidiEvent[] events)
   {
      var midiStream = new FileStream(path, FileMode.OpenOrCreate);
      using (var midiStreamWriter = new MidiStreamWriter(midiStream))
      {
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
         for (var index = 0; index < events.Length; index++)
         {
            var rtMidiEvent = events[index];
            if (rtMidiEvent.MessageType == MidiMessageType.NoteOn)
            {
               index += 1;
               var rtMidiEventOff = events[index];
               var noteLength = (rtMidiEventOff.Time.Ticks - rtMidiEvent.Time.Ticks) / 120 / 60;

               midiStreamWriter.WriteNoteAndTick(
                  (byte)rtMidiEvent.Channel,
                  (MidiConstants.MidiNoteNumbers)rtMidiEvent.Note.GetByteRepresentation(),
                  (byte)rtMidiEvent.Velocity,
                  noteLength);
            }
         }

         midiStreamWriter.WriteEndTrack();
      }
   }
}