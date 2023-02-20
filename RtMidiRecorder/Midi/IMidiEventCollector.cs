using RtMidi.Net.Events;

namespace RtMidiRecorder.Midi;

public interface IMidiEventCollector
{
   void Add(MidiMessageReceivedEventArgs eventArgs);
   void Clear();
   RtMidiEvent[] Collect(bool clear = true);
   bool HasEvents();
}