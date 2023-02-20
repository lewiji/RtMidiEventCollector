using RtMidi.Net;
using RtMidi.Net.Enums;

namespace RtMidiRecorder.Midi;

public struct RtMidiEvent
{
   public TimeSpan Time;
   public MidiMessageType MessageType;
   public MidiNote Note;
   public uint Velocity;
}