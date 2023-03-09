using RtMidiRecorder.Midi.Data;

namespace RtMidiRecorder.Midi.File;

public interface IMidiEventsSerialiser
{
   void WriteEventsToFile(string path, RtMidiEvent[] events, double tempo = 120.0);
}