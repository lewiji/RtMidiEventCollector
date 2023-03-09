using RtMidiRecorder.Midi.File;

namespace RtMidiRecorder.Midi.Extensions;

public static class TimeSpanExtensions
{
   public static long MidiTicks(this TimeSpan timeSpan)
   {
      return (long)(timeSpan.TotalMicroseconds / MidiEventsSerialiser.USecPerMidiTick);
   }
}