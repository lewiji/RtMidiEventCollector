using Hearn.Midi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RtMidi.Net.Enums;
using RtMidiRecorder.Midi.Data;
using RtMidiRecorder.Midi.Extensions;

namespace RtMidiRecorder.Midi.File;

internal sealed class MidiEventsSerialiser : IMidiEventsSerialiser
{
   const int DefaultTempo = 120;
   const int Ppq = 96; // hardcoded in Hearn.Midi
   const float USecPerMinute = 60000000.0f;
   public static float USecPerMidiTick = CalculateUSecPerMidiTick(DefaultTempo);
   readonly ILogger _logger;
   readonly IOptions<MidiSettings> _midiSettings;

   public MidiEventsSerialiser(ILogger<MidiEventsSerialiser> logger, IOptions<MidiSettings> midiSettings)
   {
      _logger = logger;
      _midiSettings = midiSettings;
      WriteTestMidi();
   }

   public void WriteEventsToFile(string path, IEnumerable<RtMidiEvent> events, int tempo = DefaultTempo)
   {
      USecPerMidiTick = CalculateUSecPerMidiTick(tempo);

      _logger.LogDebug($"usecs per tick: {USecPerMidiTick}");
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
            .WriteTempo(tempo)
            .WriteString(MidiConstants.StringTypes.TrackName, "Metadata")
            .WriteEndTrack();

         midiStreamWriter.WriteStartTrack();
         midiStreamWriter.WriteString(MidiConstants.StringTypes.TrackName, "CollectedEvents");

         var rtMidiEvents = events as RtMidiEvent[] ?? events.ToArray();
         var allNotes =
            new Queue<RtMidiEvent>(rtMidiEvents.Where(e =>
               e.MessageType is MidiMessageType.NoteOn or MidiMessageType.NoteOff));
         var onNotes = rtMidiEvents.Where(e => e.MessageType == MidiMessageType.NoteOn).ToList();
         onNotes.RemoveAt(0);
         var offNotes = rtMidiEvents.Where(e => e.MessageType == MidiMessageType.NoteOff).ToList();
         var tickWriteStack = new Stack<RtMidiEvent>();

         while (allNotes.Count > 0)
         {
            var noteEvent = allNotes.Dequeue();
            switch (noteEvent.MessageType)
            {
               case MidiMessageType.NoteOn:
               {
                  var noteOff = offNotes.FirstOrDefault(e =>
                     e.MessageType == MidiMessageType.NoteOff &&
                     e.Note.GetByteRepresentation() == noteEvent.Note.GetByteRepresentation());
                  offNotes.Remove(noteOff);
                  tickWriteStack.Push(noteOff);

                  var noteLength = _midiSettings.Value.DrumMode
                     ? (long)MidiStreamWriter.NoteDurations.SixteenthNote
                     : noteOff.Time.MidiTicks();

                  _logger.LogDebug($"Note on {noteOff.Note.GetName()}: {noteLength} midi ticks");

                  var channel = _midiSettings.Value.DrumMode ? 9 : _midiSettings.Value.Channel ?? noteEvent.Channel;

                  midiStreamWriter.WriteNote(
                     (byte)channel,
                     (MidiConstants.MidiNoteNumbers)noteEvent.Note.GetByteRepresentation(),
                     (byte)noteEvent.Velocity,
                     noteLength);


                  break;
               }
               case MidiMessageType.NoteOff:
               {
                  var nextNoteOn = onNotes.FirstOrDefault(e => e.MessageType == MidiMessageType.NoteOn);
                  onNotes.Remove(nextNoteOn);
                  // tick duration is noteOff duration + time til next note. last tick has no duration
                  var tickDuration = Equals(nextNoteOn, default(RtMidiEvent))
                     ? noteEvent.Time.MidiTicks()
                     : noteEvent.Time.MidiTicks() + nextNoteOn.Time.MidiTicks();
                  midiStreamWriter.Tick(tickDuration);
                  _logger.LogDebug($"Note off: {noteEvent.Note.GetName()}: {tickDuration} midi ticks");
                  break;
               }
            }
         }

         midiStreamWriter.WriteEndTrack();
         _logger.LogInformation(ConsoleMessages.Saved_midi_);
      }
      catch (Exception e)
      {
         _logger.LogError(e, ConsoleMessages.Error_while_serializing_midi_events_);
      }
   }

   static float CalculateUSecPerMidiTick(float tempo)
   {
      var uSecPerBeat = USecPerMinute / tempo;
      return uSecPerBeat / Ppq;
   }

   public void WriteTestMidi()
   {
      var tempo = DefaultTempo;
      var path = $"test-{DateTime.Now:M-dd-HHmmss}.mid";
      USecPerMidiTick = CalculateUSecPerMidiTick(tempo);

      _logger.LogDebug($"usecs per tick: {USecPerMidiTick}");
      _logger.LogInformation(string.Format(ConsoleMessages.Saving_midi_to_path, path));

      var midiStream = new FileStream(path, FileMode.OpenOrCreate);
      using var midiStreamWriter = new MidiStreamWriter(midiStream);

      midiStreamWriter
         .WriteHeader(MidiConstants.Formats.MultiSimultaneousTracks, 2);

      midiStreamWriter
         .WriteStartTrack()
         .WriteTimeSignature(4, 4)
         .WriteTempo(tempo)
         .WriteString(MidiConstants.StringTypes.TrackName, "Metadata")
         .WriteEndTrack();

      midiStreamWriter.WriteStartTrack();
      midiStreamWriter.WriteString(MidiConstants.StringTypes.TrackName, "CollectedEvents");
      midiStreamWriter
         .WriteNote(0, MidiConstants.MidiNoteNumbers.C4, 127, MidiStreamWriter.NoteDurations.Breve)
         .WriteNote(0, MidiConstants.MidiNoteNumbers.E4, 127, MidiStreamWriter.NoteDurations.Triplet)
         .Tick(MidiStreamWriter.NoteDurations.EighthNote)
         .WriteNote(0, MidiConstants.MidiNoteNumbers.E4, 127, MidiStreamWriter.NoteDurations.Triplet)
         .Tick(MidiStreamWriter.NoteDurations.EighthNote)
         .WriteNote(0, MidiConstants.MidiNoteNumbers.E4, 127, MidiStreamWriter.NoteDurations.Triplet)
         .Tick(MidiStreamWriter.NoteDurations.EighthNote)
         .WriteNote(0, MidiConstants.MidiNoteNumbers.E4, 127, MidiStreamWriter.NoteDurations.Triplet)
         .Tick(MidiStreamWriter.NoteDurations.EighthNote)
         .Tick(MidiStreamWriter.NoteDurations.WholeNote);

      midiStreamWriter.WriteEndTrack();
      _logger.LogInformation(ConsoleMessages.Saved_midi_);
   }
}