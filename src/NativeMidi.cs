using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Windows MIDI Services creates the cable; WinMM sends short messages directly.
sealed class NativeMidi : IDisposable {
 [DllImport("CompanionMidi.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] static extern int MC_Create(StringBuilder error,int capacity);
 [DllImport("CompanionMidi.dll",CallingConvention=CallingConvention.Cdecl)] static extern int MC_Close();
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Caps {
  public ushort manufacturer,product;public uint version;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;
  public ushort technology,voices,notes,channelMask;public uint support;
 }
 [DllImport("winmm.dll")] static extern uint midiOutGetNumDevs();
 [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern uint midiOutGetDevCapsW(UIntPtr id,out Caps caps,uint size);
 [DllImport("winmm.dll")] static extern uint midiOutOpen(out IntPtr handle,uint id,IntPtr callback,UIntPtr instance,uint flags);
 [DllImport("winmm.dll")] static extern uint midiOutShortMsg(IntPtr handle,uint message);
 [DllImport("winmm.dll")] static extern uint midiOutClose(IntPtr handle);
 IntPtr output;bool created;readonly bool[] notes=new bool[128];int bend=8192,modulation;
 public bool Ready {get{return output!=IntPtr.Zero;}}
 public void Create(){if(created)return;var error=new StringBuilder(512);int result=MC_Create(error,error.Capacity);if(result!=0)throw new Exception("Windows MIDI port: "+error+" ("+result+")");created=true;}
 public bool TryOpen(){
  if(Ready)return true;if(!created)return false;
  for(uint i=0;i<midiOutGetNumDevs();i++) {Caps c;if(midiOutGetDevCapsW((UIntPtr)i,out c,(uint)Marshal.SizeOf(typeof(Caps)))==0 && c.name=="Mikro Companion Send"){
   uint result=midiOutOpen(out output,i,IntPtr.Zero,UIntPtr.Zero,0);if(result!=0)throw new Exception("MIDI output open error="+result);
   Send(0xb0,123,0);return true;
  }}return false;
 }
 readonly object outputGate=new object();
 void Send(int status,int a,int b){lock(outputGate){if(!Ready)throw new InvalidOperationException("MIDI output is not ready");uint r=midiOutShortMsg(output,(uint)(status|(a<<8)|(b<<16)));if(r!=0)throw new Exception("MIDI output send error="+r);}}
 public void Note(int note,int velocity,bool down){if(note<0||note>127||velocity<0||velocity>127)throw new ArgumentOutOfRangeException();Send(down?0x90:0x80,note,down?velocity:0);notes[note]=down;}
 public void PitchBend(int value){if(value<0||value>16383)throw new ArgumentOutOfRangeException("value");if(value!=bend){Send(0xe0,value&127,value>>7);bend=value;}}
 public void Modulation(int value){if(value<0||value>127)throw new ArgumentOutOfRangeException("value");if(value!=modulation){Send(0xb0,1,value);modulation=value;}}
 public void ResetExpression(){PitchBend(8192);Modulation(0);}
 public void BrowserClose(){Send(0xb0,67,127);Send(0xb0,67,0);}
 public void BrowserFavorite(){Send(0xb0,68,127);Send(0xb0,68,0);}
 public void CreateTrack(){Send(0xb0,69,127);Send(0xb0,69,0);}
 public void PatternPad(int value){Send(0xb0,71,value);}
 public void PatternToggle(){Send(0xb0,70,127);Send(0xb0,70,0);}
 public void SceneToggle(){Send(0xb0,74,127);Send(0xb0,74,0);}
 public void PatternTurn(int delta,bool horizontal=false){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,horizontal?73:72,delta>0?1:127);}
 public void Transport(MikroTransport command){if(command==MikroTransport.GridFeedback || command==MikroTransport.FixedVelocity || command==MikroTransport.SixteenVelocity)return;int cc=command==MikroTransport.Play?20:command==MikroTransport.Stop?21:command==MikroTransport.Record?22:command==MikroTransport.TapTempo?25:command==MikroTransport.Metronome?26:command==MikroTransport.Follow?27:command==MikroTransport.Mute?29:command==MikroTransport.Solo?30:command==MikroTransport.PadMode?31:command==MikroTransport.KeyboardMode?32:command==MikroTransport.Events?39:command==MikroTransport.Restart?45:command==MikroTransport.Loop?46:command==MikroTransport.Erase?47:command==MikroTransport.Volume?48:command==MikroTransport.Tempo?54:command==MikroTransport.Browse?57:command==MikroTransport.Step?61:command==MikroTransport.Duplicate?63:command==MikroTransport.Plugin?80:34;Send(0xb0,cc,127);Send(0xb0,cc,0);}
 public void DeviceTurn(int delta,bool fast){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,fast?84:83,delta>0?1:127);}
 public void DeviceReset(){Send(0xb0,93,127);Send(0xb0,93,0);}
 public void DeviceStrip(int position){if(position<0 || position>127)throw new ArgumentOutOfRangeException("position");Send(0xb0,94,position);}
 public void DeviceNavigate(int direction,bool device){Send(0xb0,device?82:81,direction>0?1:127);}
 public void DeviceClose(){Send(0xb0,85,127);Send(0xb0,85,0);}
 public void NavigationToggle(){Send(0xb0,86,127);Send(0xb0,86,0);}
 public void NavigationPage(int direction){Send(0xb0,87,direction>0?1:127);}
 public void NavigationTurn(int delta,bool fast){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,fast?89:88,delta>0?1:127);}
 public void ViewToggle(){Send(0xb0,90,127);Send(0xb0,90,0);}
 public void VolumeTurn(int delta,bool coarse=false){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,coarse?53:49,delta>0?1:127);}
 public void BrowserTurn(int delta){delta=Math.Max(-63,Math.Min(63,delta));if(delta!=0)Send(0xb0,58,delta>0?delta:128+delta);}
 public void BrowserPress(bool back){Send(0xb0,back?60:59,127);Send(0xb0,back?60:59,0);}
 public void GridHold(bool held){Send(0xb0,75,held?127:0);}
 public void GridTurn(int delta){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,76,delta>0?1:127);}
 public void StepTurn(int delta){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,64,delta>0?1:127);}
 public void StepSelectTurn(int delta){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,77,delta>0?1:127);}
 public void StepSelectReset(){Send(0xb0,79,127);Send(0xb0,79,0);}
 public void PadBankTurn(int delta){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,65,delta>0?1:127);}
 public void ScaleTypeTurn(int delta){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,66,delta>0?1:127);}
 public void ScaleMode(bool enabled){Send(0xb0,78,enabled?127:0);}
 public void StepPad(int position){Send(0xb0,62,position);}
 public void VolumeReset(){Send(0xb0,52,127);Send(0xb0,52,0);}
 public void TempoTurn(int delta,bool coarse=false){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,coarse?56:55,delta>0?1:127);}
 public void Track(int direction){int cc=direction<0?50:51;Send(0xb0,cc,127);Send(0xb0,cc,0);}
 public void SelectEvent(int pitch){Send(0xb0,40,pitch);}
 public void EventTurn(int delta,bool extend){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,extend?42:41,delta>0?1:127);}
 public void Edit(int command){Send(0xb0,37,command);}
 public void Modifier(bool shift){Send(0xb0,38,shift?127:0);}
 public void ScaleTurn(int delta){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,33,delta>0?1:127);}
 public void OctaveTurn(int delta){for(int i=0;i<Math.Abs(delta);i++)Send(0xb0,91,delta>0?1:127);}
 public void PatternEdit(bool duplicate){Send(0xb0,92,duplicate?1:2);}
 public void SelectNote(int note){Send(0xb0,28,note);}
 public void Bank(int direction){int cc=direction==0?36:direction<0?23:24;Send(0xb0,cc,127);Send(0xb0,cc,0);}
 public void ReleaseAll(){Exception error=null;for(int i=0;i<128;i++)if(notes[i])try{Note(i,0,false);}catch(Exception ex){error=ex;}try{ResetExpression();}catch(Exception ex){error=ex;}if(error!=null)throw error;}
 public void Dispose(){try{if(Ready)ReleaseAll();}finally{if(output!=IntPtr.Zero){midiOutClose(output);output=IntPtr.Zero;}if(created){int result=MC_Close();if(result!=0)throw new Exception("Windows MIDI port cleanup error="+result);created=false;}}}
}

// A separate input client observes strikes; it never sends or forwards MIDI.
sealed class MidiInputTap : IDisposable {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]public struct Caps{public ushort manufacturer,product;public uint version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;public uint support;}
 delegate void Callback(IntPtr handle,uint message,UIntPtr instance,UIntPtr data,UIntPtr timestamp);
 [DllImport("winmm.dll")]internal static extern uint midiInGetNumDevs();
 [DllImport("winmm.dll",CharSet=CharSet.Unicode)]internal static extern uint midiInGetDevCapsW(UIntPtr id,out Caps caps,uint size);
 [DllImport("winmm.dll")]static extern uint midiInOpen(out IntPtr handle,uint id,Callback callback,UIntPtr instance,uint flags);
 [DllImport("winmm.dll")]static extern uint midiInStart(IntPtr handle);
 [DllImport("winmm.dll")]static extern uint midiInStop(IntPtr handle);
 [DllImport("winmm.dll")]static extern uint midiInClose(IntPtr handle);
 IntPtr handle;readonly Callback callback;readonly Action<int> strike;
 public MidiInputTap(uint id,Action<int> onStrike){
  strike=onStrike;callback=Receive;uint result=midiInOpen(out handle,id,callback,UIntPtr.Zero,0x30000);
  if(result!=0)throw new Exception("MIDI activity input open error="+result);
  result=midiInStart(handle);if(result!=0){midiInClose(handle);handle=IntPtr.Zero;throw new Exception("MIDI activity input start error="+result);}
 }
 void Receive(IntPtr source,uint message,UIntPtr instance,UIntPtr data,UIntPtr timestamp){
  if(message!=0x3c3)return;uint packet=(uint)data.ToUInt64();
  if((packet&0xf0)==0x90 && ((packet>>16)&127)>0){try{strike((int)((packet>>8)&127));}catch{}}
 }
 public void Dispose(){if(handle!=IntPtr.Zero){midiInStop(handle);midiInClose(handle);handle=IntPtr.Zero;}GC.KeepAlive(callback);}
}

sealed class MidiActivityInputs : IDisposable {
 readonly string[] patterns;readonly Action<string> log;readonly Dictionary<string,MidiInputTap> taps=new Dictionary<string,MidiInputTap>();
 readonly Queue<int> strikes=new Queue<int>();readonly HashSet<string> failed=new HashSet<string>();long nextScan;
 public MidiActivityInputs(string names,Action<string> logger){patterns=names.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries);log=logger;}
 public static bool Matches(string name,string pattern){return Regex.IsMatch(name,"^"+Regex.Escape(pattern.Trim()).Replace("\\*",".*")+"$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);}
 void Strike(int note){lock(strikes){if(strikes.Count<256)strikes.Enqueue(note);}}
 public void Tick(long now){
  if(patterns.Length==0 || now<nextScan)return;nextScan=now+5000;var present=new HashSet<string>();
  for(uint id=0;id<MidiInputTap.midiInGetNumDevs();id++){
   MidiInputTap.Caps caps;if(MidiInputTap.midiInGetDevCapsW((UIntPtr)id,out caps,(uint)Marshal.SizeOf(typeof(MidiInputTap.Caps)))!=0)continue;
   if(caps.name=="Mikro Companion" || caps.name=="Mikro Companion Send")continue;
   bool match=false;foreach(string pattern in patterns)if(Matches(caps.name,pattern)){match=true;break;}if(!match)continue;
   string key=id+":"+caps.name;present.Add(key);if(taps.ContainsKey(key))continue;
   try{taps.Add(key,new MidiInputTap(id,Strike));failed.Remove(key);log("MIDI activity input: "+caps.name);}
   catch(Exception error){if(failed.Add(key))log(caps.name+": "+error.Message);}
  }
  var removed=new List<string>();foreach(string key in taps.Keys)if(!present.Contains(key))removed.Add(key);
  foreach(string key in removed){taps[key].Dispose();taps.Remove(key);}
 }
 public int[] Drain(){lock(strikes){var notes=strikes.ToArray();strikes.Clear();return notes;}}
 public void Pause(){foreach(var tap in taps.Values)tap.Dispose();taps.Clear();Drain();nextScan=0;}
 public void Dispose(){Pause();}
}
