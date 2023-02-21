using Hearn.Midi;
using Microsoft.Extensions.Logging;
using RtMidi.Net.Enums;
using RtMidiRecorder.Midi.Data;

namespace RtMidiRecorder.Midi.File;

public class MidiEventsSerialiser : IMidiEventsSerialiser
{
   readonly ILogger _logger;

   public MidiEventsSerialiser(ILogger<MidiEventsSerialiser> logger)
   {
      _logger = logger;
   }

   public void WriteEventsToFile(string path, RtMidiEvent[] events)
   {
      try
      {
         _logger.LogInformation(string.Format(ConsoleMessages.Saving_midi_to_path, path));

         var midiStream = new FileStream(path, FileMode.OpenOrCreate);
         using var midiStreamWriter = new MidiStreamWriter(midiStream);

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
         var notesOff = events.Where(e => e.MessageType == MidiMessageType.NoteOff).ToList();

         foreach (var noteOnEvent in notesOn)
         {
            var noteOffEvent = RemoveAndReturnFirstMatchingNoteOff(notesOff, noteOnEvent);
            var noteLength = (noteOnEvent.Time.Duration().Ticks - noteOffEvent.Time.Duration().Ticks) / 120 / 60;

            midiStreamWriter.WriteNote(
               (byte)noteOnEvent.Channel,
               (MidiConstants.MidiNoteNumbers)noteOnEvent.Note.GetByteRepresentation(),
               (byte)noteOnEvent.Velocity,
               noteLength);
            midiStreamWriter.Tick(noteOffEvent.Time.Duration().Ticks / 120 / 60);
         }

         midiStreamWriter.WriteEndTrack();
         _logger.LogInformation(ConsoleMessages.Saved_midi_);
      }
      catch (Exception e)
      {
         _logger.LogError(e, ConsoleMessages.Error_while_serializing_midi_events_);
      }
   }

   static RtMidiEvent RemoveAndReturnFirstMatchingNoteOff(ICollection<RtMidiEvent> notesOff, RtMidiEvent noteOnEvent)
   {
      var noteOff = notesOff.FirstOrDefault(e =>
         e.MessageType == MidiMessageType.NoteOff &&
         e.Note.GetByteRepresentation() == noteOnEvent.Note.GetByteRepresentation());
      notesOff.Remove(noteOff);
      return noteOff;
   }
}