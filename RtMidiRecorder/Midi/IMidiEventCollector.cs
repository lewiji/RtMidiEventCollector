using RtMidi.Net.Events;
using RtMidiRecorder.Midi.Data;

namespace RtMidiRecorder.Midi;

public interface IMidiEventCollector
{
   void Add(MidiMessageReceivedEventArgs eventArgs);
   void Clear();
   RtMidiEvent[] Collect(bool clear = true);
   bool HasEvents();
}