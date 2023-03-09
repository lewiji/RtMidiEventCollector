using RtMidi.Net.Events;

namespace RtMidiRecorder.Midi.Data;

public interface IMidiEventCollector
{
   void Add(MidiMessageReceivedEventArgs eventArgs);
   void Clear();
   IEnumerable<RtMidiEvent> Collect(bool clear = true);
   bool HasEvents();
}