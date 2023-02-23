namespace RtMidiRecorder.Midi.Configuration;

public class MidiSettings
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

   public void SetOption(string name, object value)
   {
      switch (name)
      {
         case "port":
            DevicePort = (uint) value;
            break;
         case "idle-timeout":
            IdleTimeoutSeconds = (uint) value;
            break;
         case "channel":
            Channel = (uint) value;
            break;
         case "drum-mode":
            DrumMode = (bool) value;
            break;
      }
   }
}