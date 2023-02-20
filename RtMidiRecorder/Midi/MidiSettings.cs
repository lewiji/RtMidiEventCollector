namespace RtMidiRecorder.Midi;

internal sealed class MidiSettings
{
   public uint? DevicePort { get; set; }
   public uint IdleTimeoutSeconds { get; set; } = 5;
}