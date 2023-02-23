using RtMidiRecorder.Midi.Data;

namespace RtMidiRecorder.Midi.File;

public interface IMidiEventsSerialiser
{
   void WriteEventsToFile(string path, RtMidiEvent[] events, int tempo = 120);
}