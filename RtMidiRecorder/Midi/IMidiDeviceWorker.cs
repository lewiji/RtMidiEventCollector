namespace RtMidiRecorder.Midi;

public interface IMidiDeviceWorker
{
   Task ConnectDevice();
   Task<uint> RequestDevicePort();
   void Dispose();
}