namespace RtMidiRecorder.Midi.Data;

internal sealed class MidiSettings
{
   /**
    * <summary>Set this to automatically connect to this port on startup.</summary>
    */
   public uint? DevicePort { get; set; }

   /**
    * <summary>How long to wait for silence before outputting the collected MIDI events.</summary>
    * *
    */
   public uint IdleTimeoutSeconds { get; set; } = 5;

   /**
    * <summary>Notes in the MIDI output will have their channel overridden by this value if set.</summary>
    * *
    */
   public uint? Channel { get; set; }

   /**
    * <summary>If true, note lengths will be set to 16ths and Channel will be overridden to 9.</summary>
    * *
    */
   public bool DrumMode { get; set; }
}