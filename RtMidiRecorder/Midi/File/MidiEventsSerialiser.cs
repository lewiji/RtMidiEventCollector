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
         var heldNotesTickLengths = new Dictionary<RtMidiEvent, long>();

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

                  long noteLength = noteOff.Time.MidiTicks();
                  
                  foreach (var midiEvent in allNotes)
                  {
                     if (noteOff.Equals(midiEvent))
                     {
                        break;
                     }
                     noteLength += midiEvent.Time.MidiTicks();
                  }
                  
                  var channel = _midiSettings.Value.DrumMode ? 9 : _midiSettings.Value.Channel ?? noteEvent.Channel;

                  _logger.LogDebug($"NoteOn {noteOff.Note.GetName()}: {noteLength} midi ticks");
                  midiStreamWriter.WriteNote(
                     (byte)channel,
                     (MidiConstants.MidiNoteNumbers)noteEvent.Note.GetByteRepresentation(),
                     (byte)noteEvent.Velocity,
                     noteLength);
                  // If the next event in the queue is this note's NoteOff, process it immediately
                  if (allNotes.TryPeek(out var maybeNoteOff) && maybeNoteOff.Equals(noteOff))
                  {
                     var tickLength = noteLength;
                     allNotes.Dequeue();
                     
                     if (allNotes.TryPeek(out var nextNote))// && nextNote.MessageType == MidiMessageType.NoteOn)
                     {
                        tickLength += nextNote.Time.MidiTicks();
                     }

                     _logger.LogDebug($"NoteOff {noteOff.Note.GetName()}: {noteLength} midi ticks");
                     midiStreamWriter.Tick(tickLength);
                  }
                  else
                  {
                     // Tick for the next note
                     if (allNotes.TryPeek(out var nextNote) && nextNote.MessageType == MidiMessageType.NoteOn)
                     {
                        midiStreamWriter.Tick(nextNote.Time.MidiTicks());
                     }
                     
                     var deferredTickLength = noteLength;
                     foreach (var midiEvent in allNotes)
                     {
                        if (noteOff.Equals(midiEvent))
                        {
                           break;
                        }
                        deferredTickLength -= midiEvent.Time.MidiTicks();
                     }
                     heldNotesTickLengths.Add(noteOff, deferredTickLength);
                     _logger.LogDebug($"Deferring Tick command due to held note: {noteOff.Note.GetName()}: {deferredTickLength} midi ticks");
                  }

                  break;
               }
               case MidiMessageType.NoteOff:
               {
                  long tickDuration = 0;
                  if (heldNotesTickLengths.TryGetValue(noteEvent, out var heldLength))
                  {
                     _logger.LogDebug($"Held note NoteOff received, adding {heldLength} ticks...");
                     tickDuration += heldLength;
                     heldNotesTickLengths.Remove(noteEvent);
                  }
                  else
                  {
                     tickDuration = noteEvent.Time.MidiTicks();
                     if (allNotes.TryPeek(out var nextNote) && nextNote.MessageType == MidiMessageType.NoteOn)
                     {
                        tickDuration += nextNote.Time.MidiTicks();
                     }
                  }

                  _logger.LogDebug($"Note off: {noteEvent.Note.GetName()}: {tickDuration} midi ticks");
                  midiStreamWriter.Tick(tickDuration);
                  
                  break;
               }
            }
         }

         midiStreamWriter.WriteEndTrack();
         _logger.LogInformation(ConsoleMessages.Saved_midi_);

         if (heldNotesTickLengths.Count > 0)
         {
            _logger.LogWarning("Held notes dictionary contained entries after MIDI serialisation.");
         }
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