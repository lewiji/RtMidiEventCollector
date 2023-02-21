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
         midiStreamWriter
            .WriteHeader(MidiConstants.Formats.MultiSimultaneousTracks, 2);

         midiStreamWriter
            .WriteStartTrack()
            .WriteTimeSignature(4, 4)
            .WriteTempo(120)
            .WriteString(MidiConstants.StringTypes.TrackName, "CollectedEvents")
            .WriteEndTrack();

         midiStreamWriter.WriteStartTrack();
         foreach (var rtMidiEvent in events)
         {
            if (rtMidiEvent.MessageType == MidiMessageType.NoteOn)
            {
               midiStreamWriter.WriteNoteAndTick(
                  (byte)rtMidiEvent.Channel, 
                  (MidiConstants.MidiNoteNumbers)rtMidiEvent.Note.GetByteRepresentation(),
                  (byte)rtMidiEvent.Velocity, 
                  MidiStreamWriter.NoteDurations.SixteenthNote);
            }
         }

         midiStreamWriter.WriteEndTrack();
      }
   }
}