using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;

// MIDI scheduling runs independently of UI, HID panel writes and Live's Python tick.
sealed class NoteRepeat : IDisposable {
 public static readonly string[] Labels={"1/1","1/2","1/2T","1/4","1/4T","1/8","1/8T","1/16","1/16T","1/32","1/32T","1/64","1/64T","1/128","1/128T"};
 static readonly double[] Rates={4,2,4.0/3,1,2.0/3,.5,1.0/3,.25,1.0/6,.125,1.0/12,.0625,1.0/24,.03125,1.0/48};
 readonly object gate=new object();readonly int[] held=new int[128];readonly bool[] sounding=new bool[128];
 readonly int[] physical=new int[128];bool latch;
 readonly int[] source=new int[128];readonly long[] pressOrder=new long[128];long nextOrder,arpCursor;
 static readonly string[] Types={"UP","DOWN","UP-DOWN","PLAYED","CHORD"};
 static readonly string[] Parameters={"RATE","TYPE","OCTAVES","GATE","SWING","RHYTHM"};
 // Bit zero is the first step. Authored figures plus even pulse distributions;
 // these are our own bank, not Launchkey presets.
 static readonly byte[] Rhythms={
  // 01-16: original bank; keep these numbers stable.
  255,85,17,1,51,119,221,239,EvenRhythm(3),RotateRhythm(EvenRhythm(5),2),RotateRhythm(EvenRhythm(5),1),RotateRhythm(EvenRhythm(5),3),RotateRhythm(EvenRhythm(3),3),83,39,69,
  // 17-24: sparse pairs, including offbeat starts.
  3,5,9,33,65,129,34,136,
  // 25-40: three-hit figures, from clusters to displaced accents.
  7,11,13,19,21,25,35,37,49,67,81,97,131,133,137,145,
  // 41-56: four-hit figures with varied gaps and clusters.
  15,23,27,29,43,45,53,57,71,75,77,89,99,105,113,153,
  // 57-64: denser figures with short breaks.
  31,47,55,87,95,127,191,253
 };
 public static int RhythmCount {get{return Rhythms.Length;}}
 static byte EvenRhythm(int hits){int mask=0;for(int step=0;step<8;step++)if(step*hits%8<hits)mask|=1<<step;return (byte)mask;}
 static byte RotateRhythm(byte mask,int offset){return (byte)((mask>>offset)|(mask<<(8-offset)));}
 bool arpeggiator;int arpRate=7,arpSwing=50,arpType,arpOctaves=1,arpGate=50,arpPage,arpRhythm;
 public event Action<int> Generated;
 readonly Action<int,int,bool> output;readonly Func<double> time;readonly Action<string> log;
 readonly AutoResetEvent wake=new AutoResetEvent(false);readonly Thread worker;
 bool enabled,playing,disposed,timerPeriod;int rate=7,swing=50;long index;
 double tempo=120,anchorBeat,anchorTime,lastSync,offAt=double.PositiveInfinity,lastPulse=double.NegativeInfinity;
 [DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint period);
 [DllImport("winmm.dll")]static extern uint timeEndPeriod(uint period);
 public NoteRepeat(Action<int,int,bool> send,Action<string> logger,Func<double> clock=null,bool threaded=true){
  output=send;log=logger;var stopwatch=Stopwatch.StartNew();time=clock ?? (()=>stopwatch.Elapsed.TotalSeconds);anchorTime=lastSync=time();
  if(threaded){worker=new Thread(Run);worker.IsBackground=true;worker.Name="Mikro note repeat";worker.Start();}
 }
 public bool Enabled {get{lock(gate)return enabled;}}
 public bool Arpeggiator {get{lock(gate)return enabled && arpeggiator;}}
 public bool Latch {get{lock(gate)return enabled && arpeggiator && latch;}}
 public void SetLatch(bool value){lock(gate){
  if(!enabled || !arpeggiator || latch==value)return;
  latch=value;
  if(!value)for(int note=0;note<128;note++)if(physical[note]==0){held[note]=0;pressOrder[note]=0;ReleaseSource(note);}
 }wake.Set();}
 public bool HasHeldNotes {get{lock(gate){for(int n=0;n<128;n++)if(held[n]>0)return true;return false;}}}
 public string ArpParameter {get{lock(gate)return Parameters[arpPage];}}
 public bool ArpLeft {get{lock(gate)return arpPage>0;}}
 public bool ArpRight {get{lock(gate)return arpPage<Parameters.Length-1;}}
 public int ArpRhythmNumber {get{lock(gate)return arpRhythm+1;}}
 public int ArpRhythmMask {get{lock(gate)return Rhythms[arpRhythm];}}
 public string ArpValue {get{lock(gate)return arpPage==0?Labels[arpRate]:arpPage==1?Types[arpType]:arpPage==2?arpOctaves.ToString():arpPage==3?arpGate+"%":arpPage==4?arpSwing+"%":(arpRhythm+1).ToString("00");}}
 public void TurnArp(int delta,bool page){lock(gate){if(page)arpPage=Math.Max(0,Math.Min(Parameters.Length-1,arpPage+delta));else{
  if(arpPage==0){arpRate=Math.Max(0,Math.Min(Rates.Length-1,arpRate+delta));Regrid();}
  else if(arpPage==1){arpType=Math.Max(0,Math.Min(Types.Length-1,arpType+delta));arpCursor=0;}
  else if(arpPage==2){arpOctaves=Math.Max(1,Math.Min(8,arpOctaves+delta));arpCursor=0;}
  else if(arpPage==3)arpGate=Math.Max(1,Math.Min(100,arpGate+delta));
  else if(arpPage==4){arpSwing=Math.Max(50,Math.Min(75,arpSwing+delta));Regrid();}
  else arpRhythm=Math.Max(0,Math.Min(Rhythms.Length-1,arpRhythm+delta));
 }}wake.Set();}
 int CurrentRate {get{return arpeggiator?arpRate:rate;}}
 int CurrentSwing {get{return arpeggiator?arpSwing:swing;}}
 public string Label {get{lock(gate)return "NOTE REPEAT\n"+Labels[rate]+"\nSWING "+swing+"%";}}
 public void Turn(int delta,bool editSwing){lock(gate){if(editSwing)swing=Math.Max(50,Math.Min(75,swing+delta));else rate=Math.Max(0,Math.Min(Rates.Length-1,rate+delta));Regrid();}wake.Set();}
 double Beat(double now){return anchorBeat+(now-anchorTime)*tempo/60;}
 public static double Grid(long step,double length,int percent,bool triplet){return step*length+(!triplet && (step&1)!=0?length*(percent-50)/50.0:0);}
 double At(long step){return Grid(step,Rates[CurrentRate],CurrentSwing,Labels[CurrentRate].EndsWith("T",StringComparison.Ordinal));}
 void Regrid(){double beat=Beat(time());index=(long)Math.Floor(beat/Rates[CurrentRate]);while(At(index)<beat-0.000001)index++;}
 public void Sync(double bpm,double beat,bool running,double age=0){
  if(bpm<20 || bpm>999 || double.IsNaN(bpm) || double.IsInfinity(bpm) || double.IsNaN(beat) || double.IsInfinity(beat) || double.IsNaN(age) || age<0 || age>.5)return;
  lock(gate){double now=time(),before=Beat(now);lastSync=now;bool transition=playing!=running;
   // The position belongs to the sender's sampling time, not the UI receive tick.
   double observed=beat+age*bpm/60;
   double error=observed-before;
   bool jump=running && Math.Abs(error)>.25;
   // Filter residual audio/UI sampling jitter without restarting the pulse grid.
   double correction=Math.Max(-.002,Math.Min(.002,error*.1));
   anchorBeat=running?(transition || jump || !enabled?observed:before+correction):playing?0:before;
   anchorTime=now;tempo=bpm;playing=running;
   // Manual notes also pass through this module, but belong to the physical
   // pad lifetime. Only generated repeat/Arp voices follow transport resets.
   if(transition || jump){if(enabled)StopVoices();arpCursor=0;lastPulse=double.NegativeInfinity;Regrid();}
  }wake.Set();
 }
 public void SetEnabled(bool value){lock(gate){if(enabled==value && !arpeggiator)return;StopVoices();arpeggiator=latch=false;enabled=value;lastPulse=double.NegativeInfinity;if(enabled)Regrid();}wake.Set();}
 public void SetArpeggiator(bool value){lock(gate){StopVoices();latch=false;if(!value){Array.Clear(held,0,128);Array.Clear(physical,0,128);Array.Clear(pressOrder,0,128);nextOrder=0;}arpCursor=0;arpeggiator=value;enabled=value;lastPulse=double.NegativeInfinity;if(value)Regrid();}wake.Set();}
 public void Note(int note,int velocity,bool down){lock(gate){
  if(disposed)return;
  if(down && latch && arpeggiator){bool released=true;for(int n=0;n<128;n++)if(physical[n]>0){released=false;break;}
   if(released){StopVoices();Array.Clear(held,0,128);Array.Clear(pressOrder,0,128);nextOrder=arpCursor=0;}
  }
  physical[note]=down?velocity:0;
  if(!enabled){if(down && held[note]==0)pressOrder[note]=++nextOrder;held[note]=down?velocity:0;if(down || sounding[note])output(note,velocity,down);sounding[note]=down;return;}
  bool empty=true;for(int n=0;n<128;n++)if(held[n]>0){empty=false;break;}
  if(down && held[note]==0)pressOrder[note]=++nextOrder;
  if(down || !latch)held[note]=down?velocity:0;
  if(!down && !latch)ReleaseSource(note);
  if(down && empty){arpCursor=0;if(!playing){anchorBeat=0;anchorTime=time();index=0;lastPulse=double.NegativeInfinity;}else Regrid();}
 }wake.Set();}
 public void Pressure(int note,int velocity){
  if(note<0 || note>=128 || velocity<1 || velocity>127)return;
  lock(gate){if(!disposed && enabled && !arpeggiator && physical[note]>0){physical[note]=held[note]=velocity;}}
 }
 void ReleaseSource(int note){for(int n=0;n<128;n++)if(sounding[n] && (arpeggiator?source[n]==note:n==note)){output(n,0,false);sounding[n]=false;}}
 void StopVoices(){for(int n=0;n<128;n++)if(sounding[n]){output(n,0,false);sounding[n]=false;}offAt=double.PositiveInfinity;}
 public void Reset(){lock(gate){StopVoices();Array.Clear(held,0,128);Array.Clear(physical,0,128);Array.Clear(pressOrder,0,128);enabled=arpeggiator=latch=false;arpCursor=nextOrder=0;lastPulse=double.NegativeInfinity;}wake.Set();}
 struct Voice {public int Pitch,Source;public Voice(int pitch,int origin){Pitch=pitch;Source=origin;}}
 List<Voice> ArpNotes(){
  var bases=new List<int>();for(int n=0;n<128;n++)if(held[n]>0)bases.Add(n);
  if(arpType==3)bases.Sort((a,b)=>pressOrder[a].CompareTo(pressOrder[b]));
  var notes=new List<Voice>();var seen=new bool[128];
  for(int octave=0;octave<arpOctaves;octave++)foreach(int n in bases){int pitch=n+octave*12;if(pitch<=127 && !seen[pitch]){notes.Add(new Voice(pitch,n));seen[pitch]=true;}}
  if(arpType!=3)notes.Sort((a,b)=>arpType==1?b.Pitch.CompareTo(a.Pitch):a.Pitch.CompareTo(b.Pitch));
  return notes;
 }
 void Emit(int pitch,int origin){output(pitch,held[origin],true);sounding[pitch]=true;source[pitch]=origin;if(arpeggiator && Generated!=null)Generated(origin);}
 public void Pump(){lock(gate){if(disposed || !enabled)return;double now=time(),beat=Beat(now);
  if(now-lastSync>2){Reset();return;}
  if(now>=offAt)StopVoices();
  // A delayed thread emits the latest step once, never a catch-up burst.
  if(At(index)<=beat+0.000001){while(At(index+1)<=beat+0.000001)index++;
   long step=index++;double pulse=At(step);
   if(pulse>lastPulse+0.000001){StopVoices();lastPulse=pulse;bool any=false;
    if(arpeggiator){var notes=ArpNotes();if(notes.Count>0 && (Rhythms[arpRhythm]&(1<<(int)((step%8+8)%8)))!=0){
     if(arpType==4){foreach(var note in notes)Emit(note.Pitch,note.Source);}else{
      long cycle=arpType==2 && notes.Count>1?notes.Count*2-2:notes.Count;int position=(int)(arpCursor%cycle);
      if(position>=notes.Count)position=(int)cycle-position;
      var note=notes[position];Emit(note.Pitch,note.Source);
     }arpCursor++;any=true;
    }}else for(int n=0;n<128;n++)if(held[n]>0){Emit(n,n);any=true;}
    if(any)offAt=now+Math.Max(.001,(At(index)-pulse)*60/tempo*(arpeggiator?arpGate/100.0:.5));
   }
  }
 }}
 void Run(){try{for(;;){bool on=false;lock(gate){if(disposed)return;if(enabled)for(int n=0;n<128;n++)if(held[n]>0 || sounding[n]){on=true;break;}}
   if(on && !timerPeriod){timerPeriod=timeBeginPeriod(1)==0;}else if(!on && timerPeriod){timeEndPeriod(1);timerPeriod=false;}
   if(on)Pump();wake.WaitOne(on?1:Timeout.Infinite);
  }}catch(Exception error){lock(gate){enabled=false;try{StopVoices();}catch{}}log("Note repeat stopped: "+error.Message);}
  finally{if(timerPeriod){timeEndPeriod(1);timerPeriod=false;}}
 }
 public void Dispose(){
  lock(gate){if(disposed)return;disposed=true;}
  try{Reset();}finally{wake.Set();if(worker!=null)worker.Join();wake.Dispose();}
 }
}
