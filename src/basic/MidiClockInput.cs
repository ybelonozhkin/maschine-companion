using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

sealed class MidiClockInput : IDisposable {
 delegate void Callback(IntPtr handle,uint message,UIntPtr instance,UIntPtr data,UIntPtr timestamp);
 [DllImport("winmm.dll")]static extern uint midiInOpen(out IntPtr handle,uint id,Callback callback,UIntPtr instance,uint flags);
 [DllImport("winmm.dll")]static extern uint midiInStart(IntPtr handle);
 [DllImport("winmm.dll")]static extern uint midiInStop(IntPtr handle);
 [DllImport("winmm.dll")]static extern uint midiInReset(IntPtr handle);
 [DllImport("winmm.dll")]static extern uint midiInClose(IntPtr handle);
 readonly Callback callback;readonly Action<uint> receive;IntPtr handle;string opened="",requested="";long nextScan,nextPresence;public string Error {get;private set;}
 public MidiClockInput(Action<uint> onMessage){receive=onMessage;callback=OnMessage;Error="";}
 public static string[] Names(){var names=new List<string>();for(uint id=0;id<MidiInputTap.midiInGetNumDevs();id++){MidiInputTap.Caps caps;if(MidiInputTap.midiInGetDevCapsW((UIntPtr)id,out caps,(uint)Marshal.SizeOf(typeof(MidiInputTap.Caps)))==0)names.Add(caps.name);}return names.ToArray();}
 void OnMessage(IntPtr source,uint message,UIntPtr instance,UIntPtr data,UIntPtr timestamp){if(message==0x3c3)try{receive((uint)data.ToUInt64());}catch{} }
 public void Tick(long now,string name,bool enabled){
  if(requested!=name){Close();requested=name;nextScan=0;}if(!enabled){Close();return;}if(handle!=IntPtr.Zero){if(now<nextPresence)return;nextPresence=now+2000;bool found=false;foreach(string current in Names())if(current==opened){found=true;break;}if(found)return;Close();}if(now<nextScan)return;nextScan=now+2000;Error="Clock input unavailable";
  for(uint id=0;id<MidiInputTap.midiInGetNumDevs();id++){MidiInputTap.Caps caps;if(MidiInputTap.midiInGetDevCapsW((UIntPtr)id,out caps,(uint)Marshal.SizeOf(typeof(MidiInputTap.Caps)))!=0 || caps.name!=name)continue;
   uint result=midiInOpen(out handle,id,callback,UIntPtr.Zero,0x30000);if(result==0)result=midiInStart(handle);
   if(result!=0){Close();Error="Clock input error="+result;return;}opened=name;Error="";return;
  }
 }
 void Close(){if(handle==IntPtr.Zero)return;midiInStop(handle);midiInReset(handle);uint result=midiInClose(handle);if(result!=0)throw new InvalidOperationException("Clock input close error="+result);handle=IntPtr.Zero;opened="";}
 public void Dispose(){Close();GC.KeepAlive(callback);}
}
