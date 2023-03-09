using RtMidi.Net;
using RtMidi.Net.Enums;

namespace RtMidiRecorder.Midi.Data;

public struct RtMidiEvent
{
   public long Time;
   public byte MessageType;
   public byte Note;
   public uint Velocity;
   public uint Channel;
}