using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class BasicModeTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Near(double a,double b,string message){Check(Math.Abs(a-b)<.00001,message+" ("+a+" vs "+b+")");}
 static byte[] Pad(int id,int kind,int velocity){var report=new byte[64];report[0]=2;report[1]=(byte)id;report[2]=(byte)(kind|(velocity>>8));report[3]=(byte)velocity;return report;}
 static byte[] Buttons(params int[] bits){var report=new byte[64];report[0]=1;foreach(int bit in bits)report[1+bit/8]|=(byte)(1<<(bit%8));return report;}
 static void Press(BasicController controller,params int[] bits){controller.Feed(Buttons(bits));controller.Feed(Buttons());}
 static void ClockTests(){double now=0;var clock=new BasicClock(()=>now);Check(clock.Read().Available,"internal clock is immediately available");now=1;Near(clock.Read().Beat,2,"internal beat");clock.SetTempo(60);now=2;Near(clock.Read().Beat,3,"tempo changes preserve phase");clock.Configure(MidiClockSource.External,120);Check(!clock.Read().Available,"external waits, no internal fallback");clock.Receive(0xfa);for(int i=0;i<25;i++){now+=1.0/48;clock.Receive(0xf8);}var state=clock.Read();Check(state.Available&&state.Running,"external clock acquired");Near(state.Tempo,120,"24 pulses per quarter");double beat=state.SongBeat;clock.Receive(0xfc);for(int i=0;i<12;i++){now+=1.0/48;clock.Receive(0xf8);}state=clock.Read();Check(state.Available&&!state.Running,"transport stop does not stop clock");Near(state.SongBeat,beat,"song position pauses while clock continues");Check(state.Beat>beat,"repeat phase continues");clock.Receive(0xfb);now+=1.0/48;clock.Receive(0xf8);Check(clock.Read().SongBeat>beat,"Continue resumes song position");clock.Receive(0xf2u|(16u<<8));Near(clock.Read().SongBeat,4,"song position uses sixteenth notes");clock.SetTempo(180);Near(clock.Tempo,120,"external tempo is read-only");now+=1.1;Check(!clock.Read().Available,"clock loss waits");clock.Receive(0xf8);now+=1.0/48;clock.Receive(0xf8);Check(!clock.Read().Running,"clock recovery does not silently restart transport");
  now+=1.1;clock.Receive(0xfa);clock.Receive(0xf8);now+=1.0/48;clock.Receive(0xf8);Check(clock.Read().Available&&clock.Read().Running,"Start after a long clock gap restarts transport");
  now+=1.1;clock.Receive(0xfb);clock.Receive(0xf8);now+=1.0/48;clock.Receive(0xf8);Check(clock.Read().Available&&clock.Read().Running,"Continue after a long clock gap resumes transport");}
 static void LayoutTests(){var settings=new BasicDeviceSettings{Keyboard=true,KeyboardStart=60};Check(BasicLayout.Notes(settings).Take(8).SequenceEqual(new[]{60,62,64,65,67,69,71,72}),"compact major layout");settings.Scale=false;Check(BasicLayout.Notes(settings)[15]==75,"chromatic layout");settings.KeyboardStart=127;var notes=BasicLayout.Notes(settings);Check(notes[0]==127&&notes.Skip(1).All(n=>n==-1),"upper note boundary");int bank=36;for(int i=0;i<12;i++)bank=BasicLayout.Bank(bank,-1);Check(bank==0,"lower bank");bank=BasicLayout.Bank(bank,1);Check(bank==4&&BasicLayout.Bank(bank,2)==36,"edge banks do not shift alignment");Check(BasicLayout.Bank(112,4)==112,"upper bank clamp");}
 static void StoreTests(string root){var store=new BasicDeviceStore(root);var settings=new BasicDeviceSettings{Profile=ControllerProfile.Basic,Channel=7,PadBase=52,KeyboardStart=73,Root=1,Keyboard=true};store.Save("TEST123",settings);var loaded=new BasicDeviceStore(root).Load("TEST123");Check(loaded.Channel==7&&loaded.Profile==ControllerProfile.Basic&&loaded.KeyboardStart==73,"serial settings round trip");Check(new BasicDeviceStore(root).Load("OTHER123").Channel==1,"unknown serial cannot inherit another config");bool rejected=false;try{store.Load("../wrong");}catch(ArgumentException){rejected=true;}Check(rejected,"invalid serial rejected");string config=Path.Combine(root,"companion.ini");File.WriteAllText(config,"# keep comment\nEnableAbleton=false\nClockSource=Internal\n");LocalIni.Update(config,new Dictionary<string,string>{{"ClockSource","External"}});Check(File.ReadAllText(config).Contains("# keep comment")&&File.ReadAllText(config).Contains("EnableAbleton=false")&&File.ReadAllText(config).Contains("ClockSource=External"),"shared config edit preserves unrelated settings");}
 static void DeviceSettingsTests(string root){
  string path=Path.Combine(root,"devices.ini");File.WriteAllText(path,"[Mikro.LEGACY123]\nProfile=Basic\nMidiChannel=4\n");
  var defaults=new BasicDeviceSettings{ClockSource=MidiClockSource.External,ClockInput="External clock",BasicTempo=93,DisplayDim=true,FixedVelocity=81};
  var store=new BasicDeviceStore(root);var migrated=store.Load("LEGACY123",defaults);
  Check(migrated.Channel==4&&migrated.ClockSource==MidiClockSource.External&&migrated.ClockInput=="External clock"&&migrated.BasicTempo==93&&migrated.DisplayDim&&migrated.FixedVelocity==81,"legacy device settings inherit existing clock and display defaults");
  migrated.Aftertouch=AftertouchMode.Polyphonic;store.Save("LEGACY123",migrated);store.Save("OTHER123",new BasicDeviceSettings{BasicTempo=147,DisplayDim=false});
  var reloaded=new BasicDeviceStore(root).Load("LEGACY123",new BasicDeviceSettings());
  Check(reloaded.BasicTempo==93&&reloaded.ClockSource==MidiClockSource.External&&reloaded.DisplayDim&&reloaded.FixedVelocity==81,"saved per-device clock and display survive different defaults and another serial");Check(reloaded.Aftertouch==AftertouchMode.Polyphonic&&store.Load("OTHER123").Aftertouch==AftertouchMode.Off,"aftertouch setting is per-device and defaults to Off");
  Check(new BasicDeviceStore(root).Load("OTHER123").BasicTempo==147&&!new BasicDeviceStore(root).Load("OTHER123").DisplayDim,"device settings remain independent");
  var unknown=store.Load("UNKNOWN123",defaults);unknown.BasicTempo=111;Check(defaults.BasicTempo==93,"inherited settings do not modify the shared defaults object");
  reloaded.ClockInput="wrong\nProfile=Basic";bool rejected=false;try{store.Save("LEGACY123",reloaded);}catch(FormatException){rejected=true;}Check(rejected,"clock input cannot inject INI entries");
  reloaded.ClockInput="External clock";reloaded.BasicTempo=double.NaN;rejected=false;try{store.Save("LEGACY123",reloaded);}catch(FormatException){rejected=true;}Check(rejected,"invalid per-device tempo rejected");
 }
 static void SequencerTests(string root){double now=0;var clock=new BasicClock(()=>now);var sent=new List<string>();var sequence=new BasicSequencer(clock.Read,(n,v,d)=>sent.Add(n+":"+v+":"+d));sequence.Toggle(36,0,99);sequence.Toggle(38,4,80);sequence.Start();sequence.Pump();Check(sent.Contains("36:99:True"),"step zero plays");now=.12;sequence.Pump();Check(sent.Contains("36:0:False"),"step note is released");now=.5;sequence.Pump();Check(sent.Contains("38:80:True"),"next step plays");sequence.Stop();int count=sent.Count;now=8;sequence.Pump();Check(sent.Count==count,"stop emits no further notes");sequence.Duplicate();Check(sequence.Length==8&&sequence.Snapshot().Length==4,"duplicate all parts");sequence.Undo(false);Check(sequence.Length==4&&sequence.Snapshot().Length==2,"undo restores loop length");sequence.Undo(true);Check(sequence.Length==8,"redo restores duplicate");sequence.Reset();Check(sequence.Snapshot().Length==0&&sequence.Length==4,"reset pattern");sequence.Undo(false);Check(sequence.Snapshot().Length==4&&sequence.Length==8,"reset is undoable");
  sequence.Reset();now=10;sequence.ToggleRecord();now+=.065;sequence.RecordNote(42,110,true);now+=.1;Near(sequence.Snapshot()[0].Length,.2,"snapshot includes the current held duration");sequence.RecordNote(42,0,false);sequence.ToggleRecord();var recorded=sequence.Snapshot();Check(recorded.Length==1,"one recorded note");Near(recorded[0].Start,.13,"live timing retained");Near(recorded[0].Length,.2,"held duration retained");sequence.Toggle(42,1,100);Check(sequence.Snapshot().Length==0,"off-grid note is absorbed by its nearest step");sequence.Undo(false);sequence.SaveMidi(Path.Combine(root,"pattern.mid"),120,7);MidiFileTests(Path.Combine(root,"pattern.mid"),42,110,7);
 }
 static void ExternalSequenceTests(){
  var state=new BasicClockState{Available=true,Running=true,External=true,Tempo=120,Beat=8,Revision=1};
  var sent=new List<int>();var sequence=new BasicSequencer(()=>state,(n,v,d)=>{if(d)sent.Add(n);});
  sequence.Toggle(36,0,100);sequence.Toggle(38,4,100);sequence.Start();sequence.Pump();Check(sent.SequenceEqual(new[]{36}),"external playback follows song position");
  state.Beat=9;state.Revision++;sequence.Pump();Check(sent.SequenceEqual(new[]{36,38}),"external seek retains pattern alignment");sequence.Stop();
 }
 static long Variable(BinaryReader reader){long value=0;int b;do{b=reader.ReadByte();value=(value<<7)|(uint)(b&127);}while((b&128)!=0);return value;}
 static int Number(BinaryReader reader,int bytes){int value=0;while(bytes-->0)value=(value<<8)|reader.ReadByte();return value;}
 static void MidiFileTests(string path,int pitch,int velocity,int channel){using(var reader=new BinaryReader(File.OpenRead(path))){Check(new string(reader.ReadChars(4))=="MThd"&&Number(reader,4)==6,"SMF header");Check(Number(reader,2)==0&&Number(reader,2)==1&&Number(reader,2)==960,"format 0, one track, 960 PPQN");Check(new string(reader.ReadChars(4))=="MTrk","SMF track");int size=Number(reader,4);long end=reader.BaseStream.Position+size,tick=0;bool on=false,off=false,tempo=false,signature=false,finish=false;while(reader.BaseStream.Position<end){tick+=Variable(reader);int status=reader.ReadByte();if(status==255){int type=reader.ReadByte(),length=(int)Variable(reader);byte[] data=reader.ReadBytes(length);if(type==81)tempo=data.SequenceEqual(new byte[]{7,161,32});if(type==88)signature=data.SequenceEqual(new byte[]{4,2,24,8});if(type==47)finish=true;}else{int a=reader.ReadByte(),b=reader.ReadByte();if(status==0x90+channel-1)on=a==pitch&&b==velocity;if(status==0x80+channel-1)off=a==pitch;}}Check(on&&off&&tempo&&signature&&finish&&tick==3840,"export contains notes, duration, channel, tempo, signature and full loop");}}
 static void ControllerTests(){double now=0;var clock=new BasicClock(()=>now);var output=new List<string>();using(var controller=new BasicController(clock,(n,v,d)=>output.Add(n+":"+v+":"+d),v=>{},v=>{},s=>{},now:()=>now,threaded:false)){
  controller.Activate();controller.Tick();controller.Feed(Pad(12,0x10,3200));Check(controller.Busy&&output.Last()=="36:100:True","Basic pad plays with velocity without script");controller.Feed(Pad(12,0x30,0));Check(!controller.Busy&&output.Last()=="36:0:False","release lifetime");controller.WaitingForLive=true;now=2;Check(controller.Screen.Contains("Waiting for Live"),"waiting status keeps Basic available");Press(controller,28);controller.Feed(Pad(12,0x10,3200));Check(output.Last()=="36:100:True","local keyboard layout");controller.Feed(Pad(12,0x30,0));Press(controller,27);Press(controller,30);controller.Feed(Pad(12,0x10,3200));controller.Feed(Pad(12,0x30,0));Check(controller.Sequence.Snapshot().Length==1&&controller.Sequence.Playing,"first Step creates local notes and starts playback");controller.Pump();Press(controller,24);Check(!controller.Sequence.Playing&&!controller.Busy,"Stop releases generated voices");
  Press(controller,27);controller.Feed(Buttons(17));controller.Feed(Pad(12,0x10,3200));controller.Tick();controller.Pump();Check(output.Last()=="36:100:True","repeat works at internal tempo while Step stopped");controller.Feed(Pad(12,0x40,4000));now+=.13;controller.Tick();controller.Pump();Check(output.Last()=="36:125:True","repeat pressure changes velocity");controller.Deactivate();Check(output.Last().EndsWith(":False"),"pause clears held/generated notes");int count=output.Count;controller.Feed(Pad(12,0x10,3200));controller.Pump();Check(output.Count==count,"inactive input produces no notes");
  controller.Activate(1<<12);controller.Feed(Pad(12,0x10,3200));Check(output.Count==count,"handover suppresses a pad held in the previous mode");controller.Feed(Pad(12,0x30,0));controller.Feed(Pad(12,0x10,3200));Check(output.Count==count+1,"physical release restores playing after handover");controller.Feed(Pad(12,0x30,0));
 }}
 static void StripTests(){
  double now=0;var clock=new BasicClock(()=>now);
  using(var controller=new BasicController(clock,(n,v,d)=>{},v=>{},v=>{},s=>{},now:()=>now,threaded:false)){
   controller.Activate();Press(controller,30);controller.Sequence.Toggle(36,0,100);controller.Sequence.Start();
   byte[] frame=controller.Frame();Check(frame[56]==71&&frame.Skip(57).Take(24).All(v=>v==69),"one-page Step strip is white with a bright playback marker");
   now=.5;frame=controller.Frame();Check(frame[62]==71&&frame[56]==69,"strip marker advances across the whole pattern");
   controller.Sequence.Duplicate();frame=controller.Frame();Check(frame.Skip(56).Take(13).All(v=>v>>2==17)&&frame.Skip(69).Take(12).All(v=>v==44),"duplicated pattern splits the strip into edited and remaining pages");
   controller.Sequence.Navigate(1);frame=controller.Frame();Check(frame[59]==47&&frame.Skip(68).Take(13).All(v=>v==69),"page navigation moves the white region without moving playback");
   now=2.75;frame=controller.Frame();Check(frame[73]==71,"playback marker is bright white inside the edited page");
   controller.Feed(Buttons(36));Check(controller.Frame()[73]==71,"Select audition retains the Step strip");controller.Feed(Buttons());
   now=4;frame=controller.Frame();Check(frame[56]==47,"strip playback wraps to the start of the pattern");
   controller.Sequence.Stop();frame=controller.Frame();Check(frame.Skip(56).Take(12).All(v=>v==44)&&frame.Skip(68).Take(13).All(v=>v==69),"Stop removes the marker but retains the edited-page region");
   controller.Sequence.SetGrid(.125);controller.Sequence.Navigate(1);frame=controller.Frame();Check(frame[68]==69&&frame[74]==69&&frame[67]==44&&frame[75]==44,"grid changes resize the edited-page region proportionally");
   Press(controller,27);Check(controller.Frame().Skip(56).Take(25).All(v=>v==0),"leaving Step clears the pattern strip");
   Press(controller,10);Check(controller.Frame().Skip(56).Take(25).Any(v=>v!=0),"Pitch strip still works outside Step");
   Press(controller,30);clock.Configure(MidiClockSource.External,120);frame=controller.Frame();Check(!frame.Skip(56).Take(25).Any(v=>(v&3)==3),"missing external clock has no playback marker");
  }
 }
 static void ControllerAftertouchTests(){
  foreach(bool keyboard in new[]{false,true}){
   double now=0;var clock=new BasicClock(()=>now);var notes=new List<string>();var pressure=new List<string>();
   using(var controller=new BasicController(clock,(n,v,d)=>notes.Add(n+":"+v+":"+d),v=>{},v=>{},s=>{},now:()=>now,threaded:false,polyPressure:(n,v)=>pressure.Add(n+":"+v))){
    controller.Configure(new BasicDeviceSettings{Keyboard=keyboard,Aftertouch=AftertouchMode.Polyphonic});controller.Activate();controller.Tick();
    controller.Feed(Pad(12,0x10,3200));controller.Feed(Pad(12,0x40,4095));Check(pressure[pressure.Count-1]=="36:127","Basic controller forwards played-note pressure");
    controller.Feed(Buttons(17));Check(pressure[pressure.Count-1]=="36:0","Repeat/Arp activation clears ordinary expression");pressure.Clear();
    controller.Feed(Pad(12,0x40,4000));for(int i=0;i<5;i++){now+=.13;controller.Tick();controller.Pump();}
    Check(pressure.Count==0,"Repeat/Arp does not emit aftertouch from pad pressure");
    if(keyboard)Check(notes.FindAll(n=>n.EndsWith(":True")).TrueForAll(n=>n=="36:100:True"),"Arp retains strike velocity despite pressure");
    else Check(notes.Contains("36:125:True"),"Pad Repeat retains expressive repeat velocity");
   }
  }
 }
 static void SerialScreenTests(){
  string label=BankScreen.SerialLabel("0001B369");using(var bitmap=BankScreen.Render(label)){
   int top=32,bottom=-1,left=128,right=-1;
   for(int y=0;y<32;y++)for(int x=0;x<128;x++)if(bitmap.GetPixel(x,y).R>127){top=Math.Min(top,y);bottom=Math.Max(bottom,y);left=Math.Min(left,x);right=Math.Max(right,x);}
   Check(top<16&&bottom>=16&&bottom-top>=15&&right-left>70,"serial is rendered as a large full-screen identifier");
  }
  var reports=BankScreen.TextReports(label);Check(reports.Length==2&&reports[0][3]==0&&reports[1][3]==2,"serial uses both normal OLED reports");
 }
 public static int Main(){string root=Path.Combine(Path.GetTempPath(),"MikroBasicTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{ClockTests();LayoutTests();StoreTests(root);DeviceSettingsTests(root);SequencerTests(root);ExternalSequenceTests();ControllerTests();StripTests();ControllerAftertouchTests();SerialScreenTests();Console.WriteLine("PASS Basic layouts, serial settings, internal/external clock, transport, local Step, Smart Strip, recording, Undo, MIDI export and input lifecycle");return 0;}finally{foreach(string file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);}}
}
