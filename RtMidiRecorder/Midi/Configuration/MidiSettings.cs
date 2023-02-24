using Hearn.Midi;

namespace RtMidiRecorder.Midi.Configuration;

public class MidiSettings
{
   public const int DefaultTempo = 120;
   public const int PpqDevice = 24; // hardcoded in Hearn.Midi
   public const int PpqSerialised = 96; // hardcoded in Hearn.Midi
   public const int USecPerMinute = 60000000;
   public const long DrumNoteDuration = (long) MidiStreamWriter.NoteDurations.SixtyFourthNote;
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

   /**
    * <summary>Path to output .mid files to. If not set, they will be saved to the current working directory.</summary>
    * *
    */
   public string? FilePath { get; set; }

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
         case "filepath":
            FilePath = (string) value;
            break;
      }
   }
}