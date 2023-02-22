using RtMidiRecorder.Midi.Data;

namespace RtMidiRecorder.Midi.File;

public interface IMidiEventsSerialiser
{
   void WriteEventsToFile(string path, IEnumerable<RtMidiEvent> events, int tempo = 120);
}