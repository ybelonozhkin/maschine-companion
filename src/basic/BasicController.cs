using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;

// Local playing and pattern editing. The integration owns MIDI and HID sessions.
sealed class BasicController : IDisposable {
 readonly MikroInput input=new MikroInput();readonly NoteRepeat repeat;readonly BasicSequencer sequence;
 readonly PadAftertouch aftertouch;
 readonly BasicClock clock;readonly Action<int,int,bool> noteOutput;readonly Action<int> pitchOutput,modOutput;readonly Action<string> log;
 readonly Func<double> time;readonly object voicesGate=new object();readonly bool[,] voices=new bool[2,128];
 readonly long[] flashes=new long[128],padFlashes=new long[16];readonly ManualResetEvent stop=new ManualResetEvent(false);readonly AutoResetEvent stepWake=new AutoResetEvent(false);readonly Thread worker;
 [DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint period);
 [DllImport("winmm.dll")]static extern uint timeEndPeriod(uint period);
 volatile bool enabled,disposed;int keyboardPage,repeatPage,strip,stripPosition,modPosition;bool tempoView,arpView,pinned,gridView;
 long touched;double lastTap=-10;readonly Queue<double> taps=new Queue<double>();bool externalRunning,clockNotice;
 public BasicDeviceSettings Settings {get;private set;}
 public bool Dirty {get;set;}public bool TempoDirty {get;set;}public bool WaitingForLive,LiveReady;
 public event Action Activity;
 public BasicSequencer Sequence {get{return sequence;}}
 public bool Busy {get{return input.AnyHeld || repeat.HasHeldNotes || sequence.Playing;}}
 public bool Enabled {get{return enabled;}}
 public int HeldPadMask {get{return input.HeldPadMask;}}
 public BasicController(BasicClock source,Action<int,int,bool> notes,Action<int> pitch,Action<int> mod,Action<string> logger,int fixedVelocity=127,Func<double> now=null,bool threaded=true,Action<int,int> polyPressure=null,Action<int> channelPressure=null){
  clock=source;noteOutput=notes;pitchOutput=pitch;modOutput=mod;log=logger;time=now ?? (()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency);Settings=new BasicDeviceSettings{FixedVelocity=fixedVelocity};input.FixedValue=fixedVelocity;
  input.Activity+=()=>{if(Activity!=null)Activity();};
  sequence=new BasicSequencer(StepClock,(n,v,d)=>Voice(1,n,v,d));
  repeat=new NoteRepeat((n,v,d)=>{sequence.RecordNote(n,v,d);Voice(0,n,v,d);},logger,time,threaded);
  aftertouch=new PadAftertouch(polyPressure ?? ((n,v)=>{}),channelPressure ?? (v=>{}),()=>enabled&&!repeat.Enabled);
  input.PadHeld+=aftertouch.Held;input.RawPressure+=aftertouch.Pressure;
  repeat.Generated+=n=>flashes[n]=(long)(time()*1000)+50;
  input.Note+=(n,v,d)=>{if(enabled)repeat.Note(n,v,d);};input.Pressure+=(n,v)=>{if(enabled)repeat.Pressure(n,v);};
  input.Selected+=n=>Touch();input.Strike+=p=>padFlashes[p]=(long)(time()*1000)+50;
  input.Transport+=Control;input.Encoder+=d=>Turn(d,false);input.RootEncoder+=d=>Turn(d,true);input.Bank+=Navigate;
  input.StepPad+=p=>{bool empty=sequence.Snapshot().Length==0;sequence.Toggle(SelectedPitch,p,input.Fixed?input.FixedValue:100);if(empty&&sequence.Snapshot().Length>0&&clock.Read().Available)sequence.Start();Touch();};input.Edit+=Edit;
  input.Grid+=held=>{gridView=held;Touch();};input.Repeat+=Repeat;
  input.Strip+=position=>{if(!enabled||input.StepOnly)return;if(strip==1){stripPosition=position;pitchOutput(AbletonIntegration.StripPitch(position));}else if(strip==2&&position>0){modPosition=position;modOutput((position-1)*127/199);}};
  if(threaded){worker=new Thread(Run){IsBackground=true,Name="Basic Step"};worker.Start();}
 }
 void Voice(int source,int pitch,int velocity,bool down){lock(voicesGate){if(pitch<0||pitch>127||down&&!enabled)return;bool was=voices[0,pitch]||voices[1,pitch];voices[source,pitch]=down;bool sounding=voices[0,pitch]||voices[1,pitch];if(down){noteOutput(pitch,velocity,true);flashes[pitch]=(long)(time()*1000)+50;}else if(was&&!sounding)noteOutput(pitch,0,false);}}
 public void Configure(BasicDeviceSettings settings){settings.Validate();if(enabled)CancelPlaying();Settings=settings.Clone();input.FixedValue=Settings.FixedVelocity;input.BaseNote=Settings.PadBase;aftertouch.Mode=Settings.Aftertouch;Layout();Dirty=false;}
 public void Activate(int suppressPads=0){enabled=true;input.Reset();input.SuppressPads(suppressPads);Layout();Touch();}
 public void Deactivate(){enabled=false;CancelPlaying();input.Reset();tempoView=gridView=externalRunning=false;}
 public void SetChannel(int value){CancelPlaying();Settings.Channel=value;Settings.Validate();Dirty=true;}
 public int FixedVelocity {set{input.FixedValue=value;}}
 public AftertouchMode Aftertouch {get{return aftertouch.Mode;}set{aftertouch.Mode=value;Settings.Aftertouch=value;}}
 public void Feed(byte[] report){if(!enabled)return;input.Feed(report);stepWake.Set();}
 void Touch(){touched=(long)(time()*1000);}
 BasicClockState StepClock(){var state=clock.Read();if(state.External)state.Beat=state.SongBeat;return state;}
 void Layout(){input.BaseNote=Settings.PadBase;input.KeyboardNotes=Settings.Keyboard?BasicLayout.Notes(Settings):null;input.PreviewNotes=input.KeyboardNotes;}
 int SelectedPitch {get{return input.SelectedNote>=0?input.SelectedNote:Settings.Keyboard?FirstPitch:Settings.PadBase;}}
 int FirstPitch {get{foreach(int n in BasicLayout.Notes(Settings))if(n>=0)return n;return 127;}}
 void CancelNotes(){try{input.CancelHeldNotes();}finally{try{repeat.Reset();}finally{aftertouch.Clear();arpView=pinned=false;}}}
 public void CancelPlaying(){Exception failure=null;try{sequence.Stop();}catch(Exception ex){failure=ex;}try{CancelNotes();}catch(Exception ex){failure=ex;}lock(voicesGate){for(int n=0;n<128;n++){if(voices[0,n]||voices[1,n])try{noteOutput(n,0,false);}catch(Exception ex){failure=ex;}voices[0,n]=voices[1,n]=false;}}try{pitchOutput(8192);modOutput(0);}catch(Exception ex){failure=ex;}stripPosition=modPosition=0;if(failure!=null)throw failure;}
 public void Tick(){if(!enabled)return;var state=clock.Read();if(clock.Source==MidiClockSource.External){bool running=state.Available&&state.Running;if(running&&!externalRunning)sequence.Start(true);else if(!running&&externalRunning)sequence.Stop();externalRunning=running;stepWake.Set();}if(state.Available)repeat.Sync(state.Tempo,state.Beat,true);else if(repeat.Enabled){CancelNotes();Touch();}}
 public void Pump(){if(worker==null)repeat.Pump();sequence.Pump();}
 void Run(){bool highResolution=false;try{var signals=new WaitHandle[]{stop,stepWake};for(;;){bool playing=enabled&&sequence.Playing;if(playing&&!highResolution)highResolution=timeBeginPeriod(1)==0;else if(!playing&&highResolution){timeEndPeriod(1);highResolution=false;}try{if(enabled)Pump();}catch(Exception ex){try{sequence.Stop();}catch{}log("Basic Step stopped: "+ex.Message);}if(WaitHandle.WaitAny(signals,enabled&&sequence.Playing?1:Timeout.Infinite)==0)return;}}finally{if(highResolution)timeEndPeriod(1);}}
 void Repeat(bool held){if(!enabled)return;aftertouch.Clear();bool focused=arpView&&!tempoView;Touch();tempoView=false;if(held&&!clock.Read().Available){clockNotice=true;return;}
  if(input.Shift&&held){if(repeat.Arpeggiator)repeat.SetLatch(!repeat.Latch);else if(!Settings.Keyboard&&clock.Read().Available){pinned=!pinned;repeat.SetEnabled(pinned);}return;}
  if(repeat.Arpeggiator){if(held){if(focused)CancelNotes();else arpView=true;}return;}
  if(held&&Settings.Keyboard&&clock.Read().Available){repeat.SetArpeggiator(true);arpView=true;return;}
  if(held&&pinned){pinned=false;repeat.SetEnabled(false);return;}
  if(!pinned&&(!held||clock.Read().Available))repeat.SetEnabled(held);
 }
 void Turn(int delta,bool shift){if(!enabled||input.Group)return;Touch();
  if(input.StepOnly&&input.Select){Range(delta,shift);return;}
  if(gridView){double[] grids={1,.5,.25,.125,.0625};int at=Array.IndexOf(grids,sequence.Grid);sequence.SetGrid(grids[Math.Max(0,Math.Min(grids.Length-1,at+delta))]);return;}
  if(tempoView){if(clock.Source==MidiClockSource.Internal){clock.SetTempo(Math.Max(20,Math.Min(999,Math.Round(clock.Tempo)+delta)));TempoDirty=true;}return;}
  if(repeat.Arpeggiator&&arpView){repeat.TurnArp(delta,false);return;}
  if(repeat.Enabled&&!repeat.Arpeggiator){repeat.Turn(delta,repeatPage==1||input.SwingHeld);return;}
  if(input.StepOnly){sequence.Navigate(delta);return;}Range(delta,shift);
 }
 void Range(int delta,bool shift){CancelNotes();if(Settings.Keyboard){if(input.StepOnly&&input.Select||keyboardPage==0){Settings.KeyboardStart=Math.Max(0,Math.Min(127,Settings.KeyboardStart+delta*(shift?12:1)));Settings.Root=Settings.KeyboardStart%12;}else if(keyboardPage==1)Settings.ScaleIndex=Math.Max(0,Math.Min(BasicLayout.ScaleNames.Length-1,Settings.ScaleIndex+delta));else Settings.Scale=delta>0;}else Settings.PadBase=BasicLayout.Bank(Settings.PadBase,delta);Layout();Dirty=true;}
 void Navigate(int direction){if(!enabled||input.Group||input.Select||direction==0||tempoView||gridView)return;Touch();if(repeat.Arpeggiator&&arpView)repeat.TurnArp(direction,true);else if(repeat.Enabled&&!repeat.Arpeggiator)repeatPage=Math.Max(0,Math.Min(1,repeatPage+direction));else if(Settings.Keyboard&&!input.StepOnly)keyboardPage=Math.Max(0,Math.Min(2,keyboardPage+direction));}
 void Control(MikroTransport command){if(!enabled)return;Touch();
  switch(command){
   case MikroTransport.PadMode:case MikroTransport.KeyboardMode:
    if(command==MikroTransport.KeyboardMode&&Settings.Keyboard&&repeat.Arpeggiator){arpView=false;input.StepOnly=false;tempoView=false;break;}
    CancelNotes();input.StepOnly=false;Settings.Keyboard=command==MikroTransport.KeyboardMode;keyboardPage=0;tempoView=false;Layout();Dirty=true;break;
   case MikroTransport.Step:CancelNotes();input.StepOnly=!input.StepOnly;tempoView=false;break;
   case MikroTransport.Tempo:tempoView=!tempoView;break;
   case MikroTransport.TapTempo:
    if(clock.Source!=MidiClockSource.Internal)break;double now=time();double interval=now-lastTap;lastTap=now;if(interval<.06||interval>3){taps.Clear();break;}taps.Enqueue(interval);while(taps.Count>4)taps.Dequeue();double sum=0;foreach(double value in taps)sum+=value;clock.SetTempo(Math.Max(20,Math.Min(999,Math.Round(60*taps.Count/sum))));TempoDirty=true;tempoView=true;break;
   case MikroTransport.ScaleToggle:
    if(tempoView)break;if(input.StepOnly&&!input.Select)break;CancelNotes();if(Settings.Keyboard)Settings.KeyboardStart=BasicLayout.DefaultStart(Settings);else Settings.PadBase=36;Layout();Dirty=true;break;
   case MikroTransport.FixedVelocity:CancelNotes();input.Fixed=!input.Fixed;input.Sixteen=false;break;
   case MikroTransport.SixteenVelocity:if(input.SelectedNote>=0){CancelNotes();input.Sixteen=!input.Sixteen;input.Fixed=false;}break;
   case MikroTransport.Pitch:case MikroTransport.Mod:if(!input.StepOnly){pitchOutput(8192);modOutput(0);stripPosition=modPosition=0;int next=command==MikroTransport.Pitch?1:2;strip=strip==next?0:next;}break;
   case MikroTransport.Play:if(clock.Read().Available)sequence.Start();else clockNotice=true;break;
   case MikroTransport.Stop:CancelPlaying();break;
   case MikroTransport.Restart:if(clock.Read().Available)sequence.Start(true);else clockNotice=true;break;
   case MikroTransport.Record:if(clock.Read().Available)sequence.ToggleRecord();else clockNotice=true;break;
   case MikroTransport.Duplicate:if(input.StepOnly)sequence.Duplicate();break;
   case MikroTransport.Erase:if(input.StepOnly)sequence.ErasePitch(SelectedPitch);break;
  }
 }
 void Edit(int command){Touch();if(command==1)sequence.Undo(false);else if(command==2)sequence.Undo(true);else if(command==5)sequence.Quantize();else if(command==6)sequence.Quantize(true);else if(command==9)sequence.ErasePitch(SelectedPitch);}
 public string Screen {get{
  long now=(long)(time()*1000);var state=clock.Read();if(clockNotice&&!state.Available&&now-touched<3000)return "EXTERNAL CLOCK\nWaiting";if(state.Available)clockNotice=false;if(gridView)return "GRID\n"+BankScreen.Mark("1/"+(4/sequence.Grid).ToString("0"));
  if(tempoView)return (clock.Source==MidiClockSource.Internal?"INTERNAL":"EXTERNAL")+"\n"+(state.Available?BankScreen.Mark(state.Tempo.ToString("0")+" BPM"):"Waiting");
  if(LiveReady&&now-touched>1500)return "BASIC\nLive ready";if(WaitingForLive&&now-touched>1500)return "BASIC\nWaiting for Live";
  if(input.StepOnly&&input.Select)return Settings.Keyboard?"ROOT NOTE\n"+BankScreen.Mark(BankScreen.NoteName(FirstPitch)):"PAD BANK\n"+BankScreen.Mark(BankScreen.Range(Settings.PadBase));
  if(repeat.Arpeggiator&&arpView)return repeat.ArpParameter=="RHYTHM"?BankScreen.Rhythm(repeat.ArpRhythmNumber,repeat.ArpRhythmMask):"ARP "+repeat.ArpParameter+"\n"+BankScreen.Mark(repeat.ArpValue);
  if(repeat.Enabled&&!repeat.Arpeggiator){string[] values=repeat.Label.Split('\n');return repeatPage==0?"RATE\n"+BankScreen.Mark(values[1]):"SWING\n"+BankScreen.Mark(values[2].Substring(6));}
  if(input.StepOnly)return "STEP PAGE\n"+BankScreen.Mark((sequence.Page+1)+"/"+sequence.Pages);
  if(Settings.Keyboard)return keyboardPage==0?"ROOT NOTE\n"+BankScreen.Mark(BankScreen.NoteName(FirstPitch)):keyboardPage==1?"SCALE\n"+BankScreen.Mark(BasicLayout.ScaleNames[Settings.ScaleIndex]):"SCALE MODE\n"+BankScreen.Mark(Settings.Scale?"On":"Chromatic");
  return "PAD BANK\n"+BankScreen.Mark(BankScreen.Range(Settings.PadBase));
 }}
 public byte[] Frame(){var frame=new byte[81];frame[0]=128;long now=(long)(time()*1000);bool preview=input.StepOnly&&input.Select;var position=sequence.Position;int mask=sequence.Mask(SelectedPitch),cursor=position.PadCursor;
  for(int p=0;p<16;p++){int index=MikroInput.PadPosition(p);if(input.Shift&&!preview){bool allowed=index==0?sequence.CanUndo:index==1?sequence.CanRedo:index==4||index==5||index==8;if(allowed)frame[40+p]=68;continue;}
   if(input.StepOnly&&!preview){if((sequence.ValidMask&(1<<index))!=0)frame[40+p]=(byte)(((cursor==index?17:11)<<2)|(cursor==index?3:(mask&(1<<index))!=0?2:0));continue;}
   int n=input.Sixteen?input.SelectedNote:input.Pitch(index);if(n<0)continue;bool selected=n==input.SelectedNote,flash=flashes[n]>now;bool pressed=input.Sixteen?input.IsPadHeld(p)||padFlashes[p]>now:input.IsNoteHeld(n)||flash;int color=Settings.Keyboard&&n%12==Settings.Root?17:11;frame[40+p]=(byte)((color<<2)|(input.Sixteen?(pressed?3:index/4):pressed?(selected?3:2):selected?2:0));
  }
  frame[28]=(byte)(!Settings.Keyboard&&!input.StepOnly?127:124);frame[29]=(byte)(Settings.Keyboard&&!input.StepOnly?127:124);frame[31]=(byte)(input.StepOnly?127:124);frame[37]=(byte)(input.StepOnly?124:0);frame[36]=(byte)(input.StepOnly&&sequence.Length<256?124:0);frame[20]=(byte)(input.StepOnly?124:0);
  frame[27]=(byte)(input.Fixed||input.Sixteen?127:124);frame[26]=(byte)(input.Shift?127:124);frame[6]=(byte)(tempoView?127:124);frame[21]=(byte)(clock.Source==MidiClockSource.Internal&&!input.Shift?124:0);
  frame[18]=(byte)(clock.Read().Available?(repeat.Enabled?127:124):0);if(input.Shift&&clock.Read().Available)frame[18]=(byte)((repeat.Arpeggiator?repeat.Latch:pinned)?127:124);
  frame[23]=(byte)(sequence.Playing?127:124);frame[25]=(byte)(sequence.Playing?127:124);frame[24]=(byte)(sequence.Recording?127:124);
  bool pages=Settings.Keyboard&&!input.StepOnly&&!tempoView;frame[9]=(byte)((repeat.Arpeggiator&&arpView?repeat.ArpLeft:repeat.Enabled?repeatPage>0:pages&&keyboardPage>0)?124:0);frame[10]=(byte)((repeat.Arpeggiator&&arpView?repeat.ArpRight:repeat.Enabled?repeatPage<1:pages&&keyboardPage<2)?124:0);
  frame[22]=(byte)(input.Shift?(gridView?127:124):0);if(tempoView||gridView)frame[9]=frame[10]=0;
  if(input.StepOnly){for(int i=0;i<25;i++){bool page=i>=position.StripStart&&i<position.StripEnd;frame[56+i]=(byte)(((page?17:11)<<2)|(i==position.StripCursor?3:page?1:0));}}
  else{frame[11]=(byte)(strip==1?127:124);frame[12]=(byte)(strip==2?127:124);if(strip!=0){int end=strip==1?(stripPosition==0?12:(stripPosition-1)*24/199):(modPosition==0?0:(modPosition-1)*24/199);for(int i=0;i<25;i++)frame[56+i]=(byte)(i<=end?70:0);}}
  if(preview){for(int i=1;i<40;i++)frame[i]=0;frame[37]=127;frame[Settings.Keyboard?29:28]=127;}
  return frame;
 }
 public void Dispose(){
  if(disposed)return;disposed=true;enabled=false;stop.Set();var errors=new List<Exception>();
  bool joined=worker==null||worker.Join(1500);if(!joined)errors.Add(new TimeoutException("Basic Step did not finish"));
  try{CancelPlaying();}catch(Exception ex){errors.Add(ex);}
  try{repeat.Dispose();}catch(Exception ex){errors.Add(ex);}
  if(joined){stop.Dispose();stepWake.Dispose();}
  if(errors.Count>0)throw new AggregateException("Basic cleanup failed",errors);
 }
}
