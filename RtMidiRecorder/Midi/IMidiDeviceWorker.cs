namespace RtMidiRecorder.Midi;

public interface IMidiDeviceWorker
{
   Task InitiateMidiDeviceConnection();
   Task<uint> RequestDevicePort();
   void Dispose();
}