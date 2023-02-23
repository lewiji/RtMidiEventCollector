using PeanutButter.EasyArgs.Attributes;

namespace RtMidiRecorder.Midi.Configuration;

[Description(@"
RtMidiRecorder is an automated MIDI device capture service/daemon.
Usage: rtmidirec [OPTION]...
")]
public interface ICliArgs
{
   uint? DevicePort { get; set; }
}