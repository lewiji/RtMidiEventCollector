using Hearn.Midi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RtMidi.Net.Enums;
using RtMidiRecorder.Midi.Configuration;
using RtMidiRecorder.Midi.Data;
using RtMidiRecorder.Midi.Extensions;

namespace RtMidiRecorder.Midi.File;

internal sealed class MidiEventsSerialiser : IMidiEventsSerialiser
{
   public static long USecPerMidiTick = CalculateUSecPerMidiTick(MidiSettings.DefaultTempo);

   readonly ILogger _logger;
   readonly IOptions<MidiSettings> _midiSettings;

   public MidiEventsSerialiser(ILogger<MidiEventsSerialiser> logger, IOptions<MidiSettings> midiSettings)
   {
      _logger = logger;
      _midiSettings = midiSettings;
   }

   public void WriteEventsToFile(string path, RtMidiEvent[] rtMidiEvents, int tempo = MidiSettings.DefaultTempo)
   {
      USecPerMidiTick = CalculateUSecPerMidiTick(tempo);

      try
      {
         _logger.LogInformation(string.Format(ConsoleMessages.Saving_midi_to_path, path));
         using var midiStreamWriter = new MidiStreamWriter(new FileStream(path, FileMode.OpenOrCreate));
         WriteMidiMetadata(tempo, midiStreamWriter);

         var noteEventQueue = new Queue<RtMidiEvent>(rtMidiEvents.Where(e =>
            e.MessageType is MidiMessageType.NoteOn or MidiMessageType.NoteOff));
         var noteOffEvents = rtMidiEvents.Where(e => e.MessageType == MidiMessageType.NoteOff).ToList();
         var deferredNoteDurationStorage = new Dictionary<RtMidiEvent, long>();

         while (noteEventQueue.Count > 0)
         {
            ProcessNoteEvent(noteEventQueue, noteOffEvents, midiStreamWriter, deferredNoteDurationStorage);
         }

         midiStreamWriter.WriteEndTrack();
         _logger.LogInformation(ConsoleMessages.Saved_midi_);

         if (deferredNoteDurationStorage.Count > 0)
         {
            _logger.LogWarning("Held notes dictionary contained entries after MIDI serialisation.");
         }
      }
      catch (Exception e)
      {
         _logger.LogError(e, ConsoleMessages.Error_while_serializing_midi_events_);
      }
   }

   void ProcessNoteEvent(Queue<RtMidiEvent> noteEventQueue,
      List<RtMidiEvent> noteOffEvents,
      MidiStreamWriter midiStreamWriter,
      Dictionary<RtMidiEvent, long> deferredNoteDurationStorage)
   {
      var noteEvent = noteEventQueue.Dequeue();
      var channel = _midiSettings.Value.DrumMode ? 9 : _midiSettings.Value.Channel ?? noteEvent.Channel;
      RtMidiEvent? pairedNoteOffEvent = null;

      switch (noteEvent.MessageType)
      {
         case MidiMessageType.NoteOn: {
            var noteLength =
               // Drums on MIDI channel 10 (9 here, zero indexed) may send 2 NoteOn messages with 2nd message's velocity
               // set to 0 being equivalent to NoteOff messages, other channels may use held notes/polyphony
               channel == 9
                  ? DrumsGetNoteLengthAndDiscardZeroVelNoteOn(noteEventQueue)
                  : FindPairedNoteOff(noteOffEvents, noteEvent.Note.GetByteRepresentation(), noteEventQueue, out pairedNoteOffEvent);

            _logger.LogDebug(
               $"NoteOn {noteEvent.Note.GetName()}: {(channel == 9 ? MidiSettings.DrumNoteDuration : noteLength)} midi ticks");

            midiStreamWriter.WriteNote((byte) channel, (MidiConstants.MidiNoteNumbers) noteEvent.Note.GetByteRepresentation(),
               (byte) noteEvent.Velocity, channel == 9 ? MidiSettings.DrumNoteDuration : noteLength);

            // Ticks (note release & timing between notes) have several cases to handle differently
            ProcessOrDeferTicksForNoteOn(noteEvent, noteLength, midiStreamWriter, noteEventQueue, pairedNoteOffEvent,
               deferredNoteDurationStorage);
            break;
         }
         case MidiMessageType.NoteOff: {
            long tickDuration = 0;

            if (deferredNoteDurationStorage.TryGetValue(noteEvent, out var heldLength))
            {
               _logger.LogDebug($"Held note NoteOff received, adding {heldLength} ticks...");
               tickDuration += heldLength;
               deferredNoteDurationStorage.Remove(noteEvent);
            } else
            {
               tickDuration = noteEvent.Time.MidiTicks();

               if (noteEventQueue.TryPeek(out var nextNote) && nextNote.MessageType == MidiMessageType.NoteOn)
               {
                  tickDuration += nextNote.Time.MidiTicks();
               }
            }

            _logger.LogDebug($"Note off: {noteEvent.Note.GetName()}: {tickDuration} midi ticks");
            midiStreamWriter.Tick(tickDuration);
            break;
         }
         default:
            _logger.LogDebug($"Unhandled MIDI message: {noteEvent.MessageType}");
            break;
      }
   }

   void WriteMidiMetadata(int tempo, MidiStreamWriter midiStreamWriter)
   {
      midiStreamWriter.WriteHeader(MidiConstants.Formats.MultiSimultaneousTracks, 2)
         .WriteStartTrack()
         .WriteTimeSignature(4, 4)
         .WriteTempo(tempo)
         .WriteString(MidiConstants.StringTypes.TrackName, "Metadata")
         .WriteEndTrack()
         .WriteStartTrack()
         .WriteString(MidiConstants.StringTypes.TrackName, "CollectedEvents");
      _logger.LogInformation($"Wrote MIDI header, tempo: {tempo}");
   }

   void ProcessOrDeferTicksForNoteOn(RtMidiEvent noteEvent,
      long noteLength,
      MidiStreamWriter midiStreamWriter,
      Queue<RtMidiEvent> noteEventQueue,
      RtMidiEvent? pairedNoteOffEvent,
      Dictionary<RtMidiEvent, long> deferredNoteDurationStorage)
   {
      // Process drum ticks right away as there are no held notes
      if (noteEvent.Channel == 9)
      {
         if (noteLength <= 0) return;
         _logger.LogDebug($"Tick (drums) {noteEvent.Note.GetName()}: {noteLength} midi ticks");
         midiStreamWriter.Tick(noteLength);
      }
      // If the next event in the queue is this note's paired NoteOff, process it immediately
      else if (noteEventQueue.TryPeek(out var maybeNoteOff) && maybeNoteOff.Equals(pairedNoteOffEvent))
      {
         noteEventQueue.Dequeue();
         var tickLength = noteLength;

         if (noteEventQueue.TryPeek(out var nextNote)) // && nextNote.MessageType == MidiMessageType.NoteOn)
         {
            tickLength += nextNote.Time.MidiTicks();
         }

         midiStreamWriter.Tick(tickLength);
         _logger.LogDebug($"Tick (immediate) {pairedNoteOffEvent.Value.Note.GetName()}: {noteLength} midi ticks");
      }
      // Tick for the current note should be deferred until later, so store the duration length until it's dequeued
      else
      {
         var deferredTickLength = noteLength;

         // With 2 NoteOns in a row, a Tick may be required for timing without ending the note
         if (noteEventQueue.TryPeek(out var nextNote) && nextNote.MessageType == MidiMessageType.NoteOn)
         {
            midiStreamWriter.Tick(nextNote.Time.MidiTicks());
            _logger.LogDebug($"Tick (drums, immediate) {nextNote.Note.GetName()}: {noteLength} midi ticks");
         }

         // there should be a paired note off for every note on, but it's not guaranteed due to early exit or weirdness
         if (pairedNoteOffEvent == null) return;

         // The deferred tick duration for the note playing now should account for any durations in in-between events 
         foreach (var midiEvent in noteEventQueue)
         {
            if (pairedNoteOffEvent.Equals(midiEvent))
               break;

            deferredTickLength -= midiEvent.Time.MidiTicks();
         }

         deferredNoteDurationStorage.Add(pairedNoteOffEvent.Value, deferredTickLength);
         _logger.LogDebug($"Deferring Tick: {pairedNoteOffEvent.Value.Note.GetName()}: {deferredTickLength} ticks");
      }
   }

   long DrumsGetNoteLengthAndDiscardZeroVelNoteOn(Queue<RtMidiEvent> noteEventQueue)
   {
      long duration = 0;

      // discard the second NoteOn message if velocity is 0, it's equiv to NoteOff
      if (noteEventQueue.TryPeek(out var zeroVelNoteOn) && zeroVelNoteOn is {MessageType: MidiMessageType.NoteOn, Velocity: 0})
      {
         noteEventQueue.Dequeue();
         _logger.LogDebug($"Discarding 0 velocity NoteOn (drums): {zeroVelNoteOn.Note.GetName()}");
      }

      if (noteEventQueue.TryPeek(out var nextNote))
      {
         if (nextNote is {MessageType: MidiMessageType.NoteOn, Velocity: > 0})
            duration = nextNote.Time.MidiTicks();
      } else
      {
         // last note
         duration = (long) MidiStreamWriter.NoteDurations.WholeNote;
      }

      return duration;
   }

   static long FindPairedNoteOff(ICollection<RtMidiEvent> noteOffEvents,
      byte currNote,
      Queue<RtMidiEvent> noteEventQueue,
      out RtMidiEvent? pairedNoteOffEvent)
   {
      pairedNoteOffEvent = noteOffEvents.FirstOrDefault(e =>
         e.MessageType == MidiMessageType.NoteOff && e.Note.GetByteRepresentation() == currNote);

      if (pairedNoteOffEvent.Equals(default(RtMidiEvent))) return 0;

      var noteLength = pairedNoteOffEvent.Value.Time.MidiTicks();

      foreach (var midiEvent in noteEventQueue)
      {
         if (pairedNoteOffEvent.Equals(midiEvent))
            break;
         noteLength += midiEvent.Time.MidiTicks();
      }

      noteOffEvents.Remove(pairedNoteOffEvent.Value);
      return noteLength;
   }

   static long CalculateUSecPerMidiTick(int tempo)
   {
      var uSecPerBeat = MidiSettings.USecPerMinute / tempo;
      return uSecPerBeat / MidiSettings.PpqSerialised;
   }
}