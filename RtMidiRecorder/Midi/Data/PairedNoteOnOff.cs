namespace RtMidiRecorder.Midi.Data;

public struct PairedNoteOnOff
{
   public RtMidiEvent NoteOn { get; set; }
   public RtMidiEvent NoteOff { get; set; }
}