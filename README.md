# RtMidiEventCollector
A C# .NET 7 cross-platform cli application that connects to a MIDI input device via rtmidi and collects and serialises MIDI events after a period of silence.

The application sits idle and waits for MIDI input events from the drums, and then begins collecting the notes.
When a period of time passes with no input events detected (by default, 5 seconds), the MIDI events are serialised to a timestamped file for later retrieval, and will sit idle waiting for further MIDI input, which will be saved to another file.

This application was designed, in my use case, to be run on a Raspberry Pi as a service, hooked up to an electronic drum kit via USB MIDI. 

This allows the user to have all of their practice sessions recorded automatically, to analyse practice sessions for mistakes, or use improvised parts in a DAW, without having to set up manual recording equipment and software each time.

**Implementation note:** Currently the MIDI implementation only extends to my use case, recording drums, i.e. the application will calculate the tempo from timing clock events and save note on/off events. 
Currently no control messages, sysex, etc are recorded.

If MIDI channel 10 (drums) is used, it will detect pairs of note on events with velocity > 0 and velocity == 0 to simulate note on/off as my Alesis drum module does this. 

## Installation
* Checkout w/ git submodules (i.e. `git clone --recursive https://github.com/lewiji/RtMidiEventCollector.git`)
* Build or publish the solution with .NET SDK 7
* Install (via package manager), download or compile `rtmidi >= v5.0.0`: https://github.com/thestk/rtmidi

## Usage

```sh
Description:
  RtMidiRecorder is an automated MIDI device capture service/daemon.

Usage:
  RtMidiRecorder [options]

Options:
  -p, --port <port>                  Device port for MIDI input.
  -i, --idle-timeout <idle-timeout>  How long (in seconds) to wait for silence before outputting the collected MIDI events.
  -c, --channel <channel>            Notes in the MIDI output will have their channel overridden by this value if set.
  -d, --drum-mode                    Force the MIDI inputs to be treated as percussion events.
  -f, --filepath <filepath>          Path to output .mid files to. If not set, they will be saved to the current working directory.
  --version                          Show version information
  -?, -h, --help                     Show help and usage information
  ```
  
  An `appsettings.json` file alongside the program binary can also be used to persist the above settings:
  
  ```json     
  {
   "Midi": {                                                                                                                                                                                                                                                             
     "DevicePort": 1,                                                                                                                                                                                                                                         
     "IdleTimeoutSeconds": 3,
     "Channel": 9,
     "DrumMode": false,
     "FilePath": "/home/user/somepath"
   }                                                                                                                                                                                                                                                                     
 }    
 ```
 
 ## Run as systemd service
 
 **TODO**
