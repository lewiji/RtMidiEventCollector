namespace RtMidiRecorder.Midi;

public interface IMidiDeviceWorker
{
   Task ConnectDevice();
   Task Stop();
}