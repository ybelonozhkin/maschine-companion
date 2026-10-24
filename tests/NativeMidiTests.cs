using System;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;

class NativeMidiTests {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Caps{public ushort a,b;public uint c;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;public uint support;}
 delegate void Callback(IntPtr h,uint message,UIntPtr instance,UIntPtr data,UIntPtr time);
 [DllImport("winmm.dll")]static extern uint midiInGetNumDevs();
 [DllImport("winmm.dll",CharSet=CharSet.Unicode)]static extern uint midiInGetDevCapsW(UIntPtr id,out Caps caps,uint size);
 [DllImport("winmm.dll")]static extern uint midiInOpen(out IntPtr handle,uint id,Callback callback,UIntPtr instance,uint flags);
 [DllImport("winmm.dll")]static extern uint midiInStart(IntPtr handle);
 [DllImport("winmm.dll")]static extern uint midiInStop(IntPtr handle);
 [DllImport("winmm.dll")]static extern uint midiInClose(IntPtr handle);
 static readonly List<uint> received=new List<uint>();static Callback callback=Receive;
 static void Receive(IntPtr h,uint message,UIntPtr instance,UIntPtr data,UIntPtr time){if(message==0x3c3)lock(received)received.Add((uint)data.ToUInt64()&0xffffff);}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
 static int Count(){lock(received)return received.Count;}
 static byte[] LatestTop(List<byte[]> screens){for(int i=screens.Count-1;i>=0;i--)if(screens[i][3]==0)return screens[i];throw new Exception("No top OLED report");}
 static byte[] LatestBottom(List<byte[]> screens){for(int i=screens.Count-1;i>=0;i--)if(screens[i][3]==2)return screens[i];throw new Exception("No bottom OLED report");}
 static void PanelOutputTests(){
  using(var rhythm=BankScreen.Render(BankScreen.Rhythm(9,73))){
   for(int step=0;step<8;step++){
    Check(rhythm.GetPixel(5+step*15,20).R==0,"all rhythm cells have a visible border");
    Check(rhythm.GetPixel(10+step*15,24).R==((73&(1<<step))!=0?0:255),"rhythm cells show hits filled and rests hollow in time order");
   }
  }
  Check(Convert.ToBase64String(BankScreen.TextReport(BankScreen.Rhythm(9,73),false))==Convert.ToBase64String(BankScreen.TextReport(BankScreen.Rhythm(9,109),false)),"rhythm graphics stay entirely in the lower OLED report");
  Check(BankScreen.Rhythm(64,253).StartsWith("RHYTHM 64\n"),"OLED supports the expanded bank's final rhythm");
  using(var glyphs=BankScreen.Render("I\nI")){
   int[] firstPixel={16,16},lastPixel={-1,-1},stroke={0,0};
   for(int y=0;y<32;y++){int pixels=0;for(int x=0;x<128;x++)if(glyphs.GetPixel(x,y).R>127)pixels++;
    if(pixels>0){int row=y/16;firstPixel[row]=Math.Min(firstPixel[row],y%16);lastPixel[row]=Math.Max(lastPixel[row],y%16);stroke[row]=Math.Max(stroke[row],pixels);}
   }
   Check(firstPixel[0]==firstPixel[1] && lastPixel[0]==lastPixel[1] && lastPixel[0]-firstPixel[0]>=10,"OLED rows have equal glyph height and alignment");
   Check(stroke[0]>1 && stroke[1]==1,"OLED heading remains bold and lower stems are one pixel wide");
  }
  var output=new PanelOutput();var sent=new List<byte[]>();Action<string,byte[]> write=(path,report)=>sent.Add((byte[])report.Clone());
  byte[] frame=new byte[81];frame[0]=0x80;frame[23]=0x7c;
  string first="1 Analog\n"+BankScreen.Cell("C1",true)+" Major",second="1 Analog\n"+BankScreen.Cell("C2",true)+" Major";
  output.Send("a",frame,first,write,()=>true);Check(sent.Count==3,"first output sends LED and both OLED halves");
  sent.Clear();output.Send("a",frame,first,write,()=>true);Check(sent.Count==0,"unchanged output sends nothing");
  output.Send("a",frame,second,write,()=>true);Check(sent.Count==1 && sent[0][3]==2,"note change sends only dirty bottom OLED half");
  sent.Clear();string wide="1 Analog\n"+BankScreen.Cell("F#4",false)+" "+BankScreen.Mark("Super Long Scale 2");
  output.Send("a",frame,wide,write,()=>true);Check(sent.Count==1 && sent[0][3]==2,"scale inversion and width changes stay in lower report");
  sent.Clear();output.Send("a",frame,second,write,()=>true);Check(sent.Count==1 && sent[0][3]==2,"root inversion change stays in lower report");
  sent.Clear();frame[40]=4;output.Send("a",frame,second,write,()=>true);Check(sent.Count==1 && sent[0][0]==0x80,"pad activity skips OLED");
  sent.Clear();output.Send("b",frame,second,write,()=>true);Check(sent.Count==3,"device change invalidates reports");
  sent.Clear();output.Reset();bool allowed=true;
  Check(!output.Send("b",frame,first,(path,report)=>{write(path,report);allowed=false;},()=>allowed),"pause cancels remaining reports");
  sent.Clear();output.Send("b",frame,first,write,()=>true);Check(sent.Count==3,"resume refreshes whole panel");
  try{output.Send("b",frame,second,(path,report)=>{throw new Exception("test write failure");},()=>true);throw new Exception("write did not fail");}catch(Exception ex){Check(ex.Message=="test write failure","write failure propagated");}
  sent.Clear();output.Send("b",frame,second,write,()=>true);Check(sent.Count==3,"write failure invalidates cached panel");
 }
 [STAThread]static int Main(){
  PanelOutputTests();
  Check(MidiActivityInputs.Matches("MIDIIN2 (Launchkey MK4 37 MIDI)","MIDIIN2 (Launchkey*")&&!MidiActivityInputs.Matches("Other MIDI","Launchkey*"),"MIDI activity name patterns");
  using(var midi=new NativeMidi()){
   midi.Create();Check(SpinWait.SpinUntil(()=>midi.TryOpen(),8000),"output enumeration");
   uint input=uint.MaxValue;
   for(uint i=0;i<midiInGetNumDevs();i++){Caps c;if(midiInGetDevCapsW((UIntPtr)i,out c,(uint)Marshal.SizeOf(typeof(Caps)))==0&&c.name=="Mikro Companion")input=i;}
   Check(input!=uint.MaxValue,"input enumeration");IntPtr handle;Check(midiInOpen(out handle,input,callback,UIntPtr.Zero,0x30000)==0,"input open");
   var tapped=new List<int>();var tap=new MidiInputTap(input,note=>{lock(tapped)tapped.Add(note);});
   try{
    Check(midiInStart(handle)==0,"input start");
    for(int i=0;i<16;i++){midi.Note(36+i,50+i,true);midi.Note(36+i,0,false);}
    midi.Transport(MikroTransport.Play);midi.Transport(MikroTransport.Stop);midi.Transport(MikroTransport.Record);
    midi.Note(36,100,true);midi.Note(37,110,true);midi.ReleaseAll();
    Check(SpinWait.SpinUntil(()=>Count()>=42,3000),"loopback receipt");
    lock(received){Check(received.Count==42,"exact message count");for(int i=0;i<16;i++){
     Check(received[i*2]==(uint)(0x90|((36+i)<<8)|((50+i)<<16)),"note and velocity delivery");
     Check(received[i*2+1]==(uint)(0x80|((36+i)<<8)),"release delivery");
    }for(int i=0;i<3;i++){Check(received[32+i*2]==(uint)(0xb0|((20+i)<<8)|(127<<16)),"transport press");Check(received[33+i*2]==(uint)(0xb0|((20+i)<<8)),"transport release");}
    Check(received[40]==(uint)(0x80|(36<<8))&&received[41]==(uint)(0x80|(37<<8)),"pause releases held notes");}
    midi.Bank(-1);midi.Bank(1);Check(SpinWait.SpinUntil(()=>Count()==46,1000),"bank commands delivered");
    lock(received){Check(received[42]==(uint)(0xb0|(23<<8)|(127<<16))&&received[43]==(uint)(0xb0|(23<<8))&&received[44]==(uint)(0xb0|(24<<8)|(127<<16))&&received[45]==(uint)(0xb0|(24<<8)),"bank press and release CCs");}
    Check(SpinWait.SpinUntil(()=>{lock(tapped)return tapped.Count==18;},1000),"parallel MIDI input sees only strikes");
    lock(tapped){for(int i=0;i<16;i++)Check(tapped[i]==36+i,"parallel MIDI input preserves pitches");}
    Check(Count()==46,"passive MIDI input does not echo notes");
    midi.Transport(MikroTransport.TapTempo);midi.Transport(MikroTransport.Metronome);
    Check(SpinWait.SpinUntil(()=>Count()==50,1000),"tap and metronome delivered");
    lock(received){Check(received[46]==(uint)(0xb0|(25<<8)|(127<<16))&&received[47]==(uint)(0xb0|(25<<8))&&received[48]==(uint)(0xb0|(26<<8)|(127<<16))&&received[49]==(uint)(0xb0|(26<<8)),"tap and metronome press/release CCs");}

    midi.Transport(MikroTransport.Follow);midi.Transport(MikroTransport.GridFeedback);
    Check(SpinWait.SpinUntil(()=>Count()==52,1000),"Follow delivered without Grid MIDI output");
    lock(received){Check(received[50]==(uint)(0xb0|(27<<8)|(127<<16))&&received[51]==(uint)(0xb0|(27<<8)),"Follow press and release CC");}
    midi.RootTurn(1);midi.RootTurn(-1);midi.Bank(0);
    Check(SpinWait.SpinUntil(()=>Count()==56,1000),"root turns and range reset delivered");
    lock(received){Check(received[52]==(uint)(0xb0|(35<<8)|(1<<16))&&received[53]==(uint)(0xb0|(35<<8)|(127<<16))&&received[54]==(uint)(0xb0|(36<<8)|(127<<16))&&received[55]==(uint)(0xb0|(36<<8)),"root relative and reset CC mapping");}
    midi.PitchBend(16383);midi.Modulation(127);midi.ReleaseAll();
    Check(SpinWait.SpinUntil(()=>Count()==60,1000),"pitch and modulation including pause reset delivered");
    lock(received)Check(received[56]==(uint)(0xe0|(127<<8)|(127<<16))&&received[57]==(uint)(0xb0|(1<<8)|(127<<16))&&received[58]==(uint)(0xe0|(64<<16))&&received[59]==(uint)(0xb0|(1<<8)),"expression uses 14-bit pitch bend and CC1");
   }finally{tap.Dispose();midiInStop(handle);midiInClose(handle);}
  }
  bool leftover=false;for(uint i=0;i<midiInGetNumDevs();i++){Caps c;if(midiInGetDevCapsW((UIntPtr)i,out c,(uint)Marshal.SizeOf(typeof(Caps)))==0&&c.name=="Mikro Companion")leftover=true;}
  Check(!leftover,"transient endpoint cleanup");
  lock(received)received.Clear();
  var browserKeys=new List<int>();
  Check(Marshal.SizeOf(typeof(LiveBrowserKeys.Input))==40,"Windows keyboard input ABI");
  Check(LiveBrowserKeys.TargetAllowed("Ableton Live 12 Standard",(IntPtr)10,(IntPtr)10)
    && !LiveBrowserKeys.TargetAllowed("Other app",(IntPtr)10,(IntPtr)10)
    && !LiveBrowserKeys.TargetAllowed("Ableton Live 12 Standard",(IntPtr)10,(IntPtr)11),"browser keys target Live main window and reject other apps and secondary windows");
  Check(LiveBrowserKeys.FocusTarget(LiveBrowserKeys.BrowserFocus.Outside,40)==LiveBrowserKeys.BrowserFocus.None
   && LiveBrowserKeys.FocusTarget(LiveBrowserKeys.BrowserFocus.Search,256)==LiveBrowserKeys.BrowserFocus.Contents
   && LiveBrowserKeys.FocusTarget(LiveBrowserKeys.BrowserFocus.Sidebar,13)==LiveBrowserKeys.BrowserFocus.Contents
   && LiveBrowserKeys.FocusTarget(LiveBrowserKeys.BrowserFocus.Contents,13)==LiveBrowserKeys.BrowserFocus.None
   && LiveBrowserKeys.FocusTarget(LiveBrowserKeys.BrowserFocus.Contents,37)==LiveBrowserKeys.BrowserFocus.Sidebar
   && LiveBrowserKeys.FocusTarget(LiveBrowserKeys.BrowserFocus.Sidebar,40)==LiveBrowserKeys.BrowserFocus.None,"browser leaves outside focus alone and enters manually selected categories");
  Check(AbletonIntegration.StripPitch(0)==8192&&AbletonIntegration.StripPitch(1)==0&&AbletonIntegration.StripPitch(100)==8192&&AbletonIntegration.StripPitch(101)==8192&&AbletonIntegration.StripPitch(200)==16383,"pitch strip endpoints center and release");
  var xmp=new System.Xml.XmlDocument();xmp.LoadXml("<root><item><filePath>Drums/Kit.adg</filePath><colors><Bag><li>1</li></Bag></colors></item><item><filePath>Orange.adg</filePath><colors><Bag><li>2</li></Bag></colors></item></root>");
  var favorites=new List<string>(LiveFavorites.RedNames(xmp));Check(favorites.Count==1&&favorites[0]=="Kit.adg","Favorites reads red collection metadata");
  int panels=0;var frames=new List<byte[]>();
  var screens=new List<byte[]>();Action<byte[]> onPanel=null;
  using(var integration=new AbletonIntegration(Console.WriteLine,(path,frame)=>{lock(frames){if(frame[0]==0x80)frames.Add((byte[])frame.Clone());else screens.Add((byte[])frame.Clone());}Interlocked.Increment(ref panels);if(onPanel!=null)onPanel(frame);},39474,browserKeyWriter:browserKeys.Add,showStartupSplash:false))using(var sender=new UdpClient()){
   var queue=typeof(AbletonIntegration).GetMethod("QueuePanel",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
   bool queued=false;var queueLeds=new byte[81];queueLeds[0]=0x80;
   onPanel=report=>{if(report[0]==0xe0 && report[3]==0 && !queued){queued=true;queue.Invoke(integration,new object[]{"test-device",queueLeds,52});queue.Invoke(integration,new object[]{"test-device",queueLeds,68});}};
   queue.Invoke(integration,new object[]{"test-device",queueLeds,36});
   Check(SpinWait.SpinUntil(()=>!integration.IsWriting,2000),"queued OLED frames complete");onPanel=null;
   lock(frames){Check(screens.Count==3,"rapid turns coalesce queued frames and reuse the parameter heading: "+screens.Count);
    Check(Convert.ToBase64String(screens[1])==Convert.ToBase64String(BankScreen.Report(36,true)),"new state does not interrupt first OLED frame");
    Check(screens[2][3]==2 && Convert.ToBase64String(screens[2])==Convert.ToBase64String(BankScreen.Report(68,true)),"latest queued OLED value follows intact");frames.Clear();screens.Clear();}
   integration.Pause(false);
   Action heartbeat=()=>{var data=Encoding.ASCII.GetBytes("MC1 1 0 0 ready");sender.Send(data,data.Length,new IPEndPoint(IPAddress.Loopback,39474));};
   Check(SpinWait.SpinUntil(()=>{heartbeat();integration.Tick("test-device",true,false);return integration.Status.StartsWith("Ableton:");},8000),"integration activation");
   Check(SpinWait.SpinUntil(()=>!integration.IsWriting,2000),"initial panel write");
   lock(frames){var f=frames[frames.Count-1];Check(f[23]==0x7c&&f[24]==0x7c&&f[25]==0x7c,"dim available transport brightness");Check(f[52]!=0&&f[40]==0,"one assigned bottom-left pad");}
   var allPads=Encoding.ASCII.GetBytes("MC1 65535 1 1 ready");sender.Send(allPads,allPads.Length,new IPEndPoint(IPAddress.Loopback,39474));
   Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){if(frames.Count<2)return false;var f=frames[frames.Count-1];return f[23]==0x7f&&f[24]==0x7f&&f[25]==0x7f;}},1000),"active transport brightness");
   lock(frames){var f=frames[frames.Count-1];for(int i=40;i<56;i++)Check(f[i]!=0,"all sixteen assigned pads light");}
   var colored=Encoding.ASCII.GetBytes("MC2 65535 1 1 1 ready");sender.Send(colored,colored.Length,new IPEndPoint(IPAddress.Loopback,39474));
   Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][40]==4;},1000),"track color reaches HID palette");
   colored=Encoding.ASCII.GetBytes("MC2 65535 1 1 7 ready");sender.Send(colored,colored.Length,new IPEndPoint(IPAddress.Loopback,39474));
   Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][40]==28;},1000),"color updates with unchanged pad assignment");
   foreach(int rec in new[]{0,1}){
    var stopped=Encoding.ASCII.GetBytes("MC2 65535 0 "+rec+" 7 ready");sender.Send(stopped,stopped.Length,new IPEndPoint(IPAddress.Loopback,39474));
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){var f=frames[frames.Count-1];return f[23]==0x7c&&f[25]==0x7c&&f[24]==(rec==1?0x7f:0x7c);}},1000),"stopped play and stop dim independently of record toggle");
   }
   var playingOnly=Encoding.ASCII.GetBytes("MC2 65535 1 0 7 ready");sender.Send(playingOnly,playingOnly.Length,new IPEndPoint(IPAddress.Loopback,39474));
   Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){var f=frames[frames.Count-1];return f[23]==0x7f&&f[25]==0x7f&&f[24]==0x7c;}},1000),"play and stop bright without recording");
   foreach(int metro in new[]{1,0}){
    var packet=Encoding.ASCII.GetBytes("MC5 65535 1 0 7 36 1 "+metro+" ready");sender.Send(packet,packet.Length,new IPEndPoint(IPAddress.Loopback,39474));
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][21]==(metro==1?0x7f:0x7c);},1000),"metronome state controls Tap brightness");
   }
   foreach(int follow in new[]{0,1}){
    var packet=Encoding.ASCII.GetBytes("MC6 65535 1 0 7 36 1 0 "+follow+" ready");sender.Send(packet,packet.Length,new IPEndPoint(IPAddress.Loopback,39474));
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][22]==(follow==1?0x7f:0x7c);},1000),"Follow state brightness");
    var button=new byte[64];button[0]=1;button[3]=32;button[4]=2;integration.Feed("test-device",button);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][22]==0;},100),"shift Follow dark feedback");
    button[3]=0;button[4]=0;integration.Feed("test-device",button);
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][22]==(follow==1?0x7f:0x7c);},1000),"Follow restores current state after feedback");
   }
   foreach(int baseNote in new[]{0,112,36}){
    var bank=Encoding.ASCII.GetBytes("MC3 65535 1 0 7 "+baseNote+" ready");sender.Send(bank,bank.Length,new IPEndPoint(IPAddress.Loopback,39474));
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){var f=frames[frames.Count-1];return f[9]==0&&f[10]==0;}},1000),"track arrows unavailable without mixer state");
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,2000),"screen update completes");
    lock(frames){var expected=BankScreen.Report(baseNote,false);Check(screens.Count>=2,"OLED range reports");var actual=LatestTop(screens);Check(Convert.ToBase64String(actual)==Convert.ToBase64String(expected),"OLED range follows bank");}
   }
   Check(BankScreen.Range(36)=="C1 - D#2"&&BankScreen.Range(0)=="C-2 - D#-1"&&BankScreen.Range(112)=="E7 - G8","Live note names at bank edges");
   uint input=uint.MaxValue;for(uint i=0;i<midiInGetNumDevs();i++){Caps c;if(midiInGetDevCapsW((UIntPtr)i,out c,(uint)Marshal.SizeOf(typeof(Caps)))==0&&c.name=="Mikro Companion")input=i;}
   IntPtr handle;Check(midiInOpen(out handle,input,callback,UIntPtr.Zero,0x30000)==0,"integration input open");
   try{midiInStart(handle);var r=new byte[64];r[0]=2;r[1]=12;r[2]=0x1c;r[3]=0x80;
    Action<int,int> state=(play,epoch)=>{var packet=Encoding.ASCII.GetBytes("MC4 0 "+play+" 0 7 52 "+epoch+" ready");sender.Send(packet,packet.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);};
    Action<int,int> pulse=(note,epoch)=>{var packet=Encoding.ASCII.GetBytes("MP1 "+epoch+" "+note);sender.Send(packet,packet.Length,new IPEndPoint(IPAddress.Loopback,39474));};
    state(1,1);pulse(52,1);
    Check(SpinWait.SpinUntil(()=>{pulse(52,1);integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][52]==30;},1000),"sampler activity burst lights pad");
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][52]==0;},1000),"sampler flash expires");
    pulse(52,1);pulse(53,1);
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){var f=frames[frames.Count-1];return f[52]==30&&f[53]==30;}},1000),"polyphonic sampler feedback");
    state(1,2);
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){var f=frames[frames.Count-1];return f[52]==0&&f[53]==0;}},1000),"track epoch clears old activity");
    pulse(52,1);Thread.Sleep(20);integration.Tick("test-device",true,false);
    lock(frames)Check(frames[frames.Count-1][52]==0,"stale source activity ignored");
    pulse(52,2);
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][52]==30;},1000),"activity after target change");
    state(0,2);
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][52]==0;},1000),"stop clears activity");
    pulse(52,2);
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][52]==30;},1000),"live instrument activity works with transport stopped");
    Check(Count()==0,"activity never generates MIDI notes");
    var nextBank=Encoding.ASCII.GetBytes("MC3 0 1 0 7 52 ready");sender.Send(nextBank,nextBank.Length,new IPEndPoint(IPAddress.Loopback,39474));
    string screen52=Convert.ToBase64String(BankScreen.Report(52,false));
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return screens.Count>=2&&Convert.ToBase64String(LatestTop(screens))==screen52;},1000),"bank acknowledged before strike");
    integration.Feed("test-device",r);Check(SpinWait.SpinUntil(()=>Count()==2,1000),"HID to MIDI delivery");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][52]==31;},1000),"strike lights an empty pad brightly");
    var release=(byte[])r.Clone();release[2]=0x30;release[3]=0;integration.Feed("test-device",release);
    Check(SpinWait.SpinUntil(()=>Count()==3,1000),"release after bright strike");
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][52]==30;},500),"selected empty pad returns medium after flash");
    integration.Feed("test-device",r);Check(SpinWait.SpinUntil(()=>Count()==5,1000),"second strike delivered");
    var previousBank=Encoding.ASCII.GetBytes("MC3 65535 1 0 7 36 ready");sender.Send(previousBank,previousBank.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    integration.Pause(true);Check(SpinWait.SpinUntil(()=>Count()==6,1000),"integration pause note release");
    integration.Feed("test-device",r);integration.Tick("test-device",true,true);Thread.Sleep(30);Check(Count()==6,"paused input suppressed");
    lock(received){Check(received[0]==(uint)(0xb0|(28<<8)|(52<<16)),"selection precedes note");Check(received[1]==(uint)(0x90|(52<<8)|(100<<16)),"banked hardware pad and velocity mapping");Check(received[2]==(uint)(0x80|(52<<8)),"release exact note");Check(received[5]==(uint)(0x80|(52<<8)),"pause releases original bank note");}
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,2000)&&panels>0,"asynchronous panel output");
    lock(frames){var blank=BankScreen.Report(-1,false);Check(Convert.ToBase64String(LatestTop(screens))==Convert.ToBase64String(blank),"pause clears owned screen");}
    Action<int,int,int,int> fullState=(chosen,mute,solo,available)=>{
     var packet=Encoding.ASCII.GetBytes("MC7 65535 1 0 7 36 20 0 0 "+chosen+" "+mute+" "+solo+" "+available+" ready");
     sender.Send(packet,packet.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    };
    Action<int,bool> control=(bit,shift)=>{
     var press=new byte[64];press[0]=1;press[1+bit/8]=(byte)(1<<(bit%8));if(shift)press[4]|=2;
     integration.Feed("test-device",press);var up=new byte[64];up[0]=1;integration.Feed("test-device",up);
    };
    Action<int,bool> groupControl=(bit,shift)=>{
     var press=new byte[64];press[0]=1;press[2]=64;press[1+bit/8]|=(byte)(1<<(bit%8));if(shift)press[4]|=2;
     integration.Feed("test-device",press);var up=new byte[64];up[0]=1;integration.Feed("test-device",up);
    };
    fullState(36,1,1,1);
    lock(received)received.Clear();groupControl(9,true);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(69<<8)|(127<<16)));},1000),"Group + Shift right creates a MIDI track");
    lock(received)Check(!received.Contains((uint)(0xb0|(51<<8)|(127<<16))),"Group + Shift right does not also navigate");
    Action<string> feedback=packet=>{var data=Encoding.ASCII.GetBytes(packet);sender.Send(data,data.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);};
    lock(received)received.Clear();control(9,false);control(9,true);
    lock(received){foreach(uint message in received)Check(message!=(uint)(0xb0|(51<<8)|(127<<16)) && message!=(uint)(0xb0|(69<<8)|(127<<16)),"arrows and Shift arrows alone neither navigate tracks nor create them");}
    feedback("MG1 20 1 0 1/16 ready");
    lock(received)received.Clear();var gridDial=new byte[64];gridDial[0]=1;gridDial[3]=32;gridDial[4]=2;integration.Feed("test-device",gridDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(75<<8)|(127<<16)));},1000),"Shift Follow opens Grid");
    feedback("MG1 20 1 1 1/16 ready");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][22]==127;},1000),"Grid lights Follow brightly");
    gridDial[7]=1;integration.Feed("test-device",gridDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(76<<8)|(1<<16)));},1000),"Grid owns encoder instead of note banks");
    feedback("MG1 20 1 1 1/32T ready");
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"Grid OLED completes");
    lock(frames)Check(Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport("GRID\n"+BankScreen.Mark("1/32T"),true)),"Grid OLED displays inverse resolution");
    gridDial[4]=0;integration.Feed("test-device",gridDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(75<<8)));},1000),"releasing Shift sends Grid release immediately");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][22]==124;},1000),"Grid LED exits without waiting for script feedback");
    lock(received)received.Clear();gridDial[7]=2;integration.Feed("test-device",gridDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(65<<8)|(1<<16)));},1000),"encoder returns to pad banks despite stale enabled Grid feedback");
    gridDial[3]=0;integration.Feed("test-device",gridDial);
    feedback("MG1 20 1 0 1/32T ready");
    feedback("MDEV1 20 1 1 0 1 1 0 RmlsdGVy Q3V0b2Zm Mi40MCBrSHo= ready");
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[7]==127 && f[9]==0 && f[10]==124;}},1000),"Plug-in overlay lights parameter navigation boundaries");
    lock(received)received.Clear();control(9,false);control(8,true);control(6,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(81<<8)|(1<<16))) && received.Contains((uint)(0xb0|(82<<8)|(127<<16))) && received.Contains((uint)(0xb0|(80<<8)|(127<<16)));},1000),"Plugin button and plain/Shift arrows send distinct device commands");
    var deviceDial=new byte[64];deviceDial[0]=1;deviceDial[7]=2;integration.Feed("test-device",deviceDial);
    lock(received)received.Clear();deviceDial[7]=3;integration.Feed("test-device",deviceDial);deviceDial[4]=2;deviceDial[7]=4;integration.Feed("test-device",deviceDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(83<<8)|(1<<16))) && received.Contains((uint)(0xb0|(84<<8)|(1<<16)));},1000),"device encoder distinguishes normal and fine adjustment");
    lock(received)foreach(uint message in received)Check((message&0xffff)!=(uint)(0xb0|(65<<8)) && (message&0xffff)!=(uint)(0xb0|(33<<8)),"device encoder does not also change note banks or root");
    deviceDial[4]=0;integration.Feed("test-device",deviceDial);
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"device parameter OLED completes");
    lock(frames)Check(Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport("Cutoff\n"+BankScreen.Mark("2.40 kHz"),true)),"device OLED uses the native formatted value");
    deviceDial[7]=0;integration.Feed("test-device",deviceDial);
    feedback("MDEV1 20 1 0 0 1 1 0 RmlsdGVy Q3V0b2Zm Mi40MCBrSHo= ready");
    lock(received)received.Clear();control(31,true);control(34,true);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(90<<8)|(127<<16))) && received.Contains((uint)(0xb0|(86<<8)|(127<<16)));},1000),"Shift Scene toggles document view and Shift Variation toggles Navigate");
    lock(received)Check(!received.Contains((uint)(0xb0|(74<<8)|(127<<16))),"Shift Scene does not also enter Scene mode");
    feedback("MNAV1 20 1 0 MTcuMS4x ready");
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[35]==127 && f[9]==0 && f[10]==124;}},1000),"Navigate lights active button and first page boundary");
    lock(received)received.Clear();control(9,false);
    var navigationDial=new byte[64];navigationDial[0]=1;navigationDial[7]=1;integration.Feed("test-device",navigationDial);navigationDial[4]=2;navigationDial[7]=2;integration.Feed("test-device",navigationDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(87<<8)|(1<<16))) && received.Contains((uint)(0xb0|(88<<8)|(1<<16))) && received.Contains((uint)(0xb0|(89<<8)|(1<<16)));},1000),"Navigate arrows choose pages and encoder sends normal or fast movement");
    lock(received)foreach(uint message in received)Check((message&0xffff)!=(uint)(0xb0|(65<<8)) && (message&0xffff)!=(uint)(0xb0|(33<<8)),"Navigate encoder does not change pad bank or root");
    navigationDial[4]=0;navigationDial[7]=0;integration.Feed("test-device",navigationDial);
    feedback("MNAV1 20 1 1 VmVyc2U= ready");
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"Navigate OLED completes");
    lock(frames)Check(Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport("MARKER\n"+BankScreen.Mark("Verse"),true)),"Navigate shows locator name as encoder target");
    feedback("MNAV1 20 0 1 - ready");
    fullState(36,1,1,1);
    string clockSample=(System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    var repeatClock=Encoding.ASCII.GetBytes("MR2 20 120 0 0 "+clockSample+" ready");sender.Send(repeatClock,repeatClock.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    lock(received)received.Clear();control(17,false);
    // Holding uses separate down/up reports, unlike ordinary transport buttons.
    var repeatHold=new byte[64];repeatHold[0]=1;repeatHold[3]=2;integration.Feed("test-device",repeatHold);
    var repeatPad=new byte[64];repeatPad[0]=2;repeatPad[1]=12;repeatPad[2]=0x1c;repeatPad[3]=0x80;integration.Feed("test-device",repeatPad);
    Check(SpinWait.SpinUntil(()=>{lock(received){int ons=0;foreach(uint value in received)if((value&0xf0)==0x90)ons++;return ons>=3;}},700),"held pad repeats through real MIDI output");
    var pressurePad=new byte[64];pressurePad[0]=2;pressurePad[1]=12;pressurePad[2]=0x45;pressurePad[3]=0;integration.Feed("test-device",pressurePad);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0x90|(36<<8)|(40<<16)));},400),"native pressure lowers velocity of repeated MIDI NoteOns");
    pressurePad[2]=0x4d;pressurePad[3]=0xc0;integration.Feed("test-device",pressurePad);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0x90|(36<<8)|(110<<16)));},400),"stronger native pressure raises the following repeat velocity");
    lock(received)foreach(uint message in received)Check((message&0xf0)!=0xa0&&(message&0xf0)!=0xd0,"repeat pressure is captured as velocity rather than a separate aftertouch lane");
    repeatPad[2]=0x30;repeatPad[3]=0;integration.Feed("test-device",repeatPad);repeatHold[3]=0;integration.Feed("test-device",repeatHold);
    int notesAfterRelease;lock(received){notesAfterRelease=0;foreach(uint value in received)if((value&0xf0)==0x90)notesAfterRelease++;}
    Thread.Sleep(160);lock(received){int after=0;foreach(uint value in received)if((value&0xf0)==0x90)after++;Check(after==notesAfterRelease,"pad and button release stop all repeats");}
    clockSample=(System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    feedback("MR2 20 120 0 0 "+clockSample+" ready");
    control(17,true);integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][18]==127;},1000),"Pad Note Repeat stays bright after Shift shortcut release");
    lock(received)received.Clear();repeatPad[2]=0x1c;repeatPad[3]=0x80;integration.Feed("test-device",repeatPad);
    Check(SpinWait.SpinUntil(()=>{lock(received){int ons=0;foreach(uint value in received)if((value&0xf0)==0x90)ons++;return ons>=2;}},400),"pinned Pad Note Repeat generates notes without holding its button");
    pressurePad[2]=0x45;pressurePad[3]=0;integration.Feed("test-device",pressurePad);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0x90|(36<<8)|(40<<16)));},400),"pinned repeat still follows pad pressure");
    repeatPad[2]=0x30;repeatPad[3]=0;integration.Feed("test-device",repeatPad);
    lock(received){notesAfterRelease=0;foreach(uint value in received)if((value&0xf0)==0x90)notesAfterRelease++;}
    Thread.Sleep(160);lock(received){int after=0;foreach(uint value in received)if((value&0xf0)==0x90)after++;Check(after==notesAfterRelease,"pinning the mode does not latch released pad notes");}
    control(17,false);integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][18]==124;},1000),"ordinary Note Repeat press clears pinning without re-enabling on release");
    control(17,true);control(17,true);integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][18]==124;},1000),"Shift shortcut can also unpin Pad Note Repeat");
    lock(received)received.Clear();control(10,false);var strip=new byte[64];strip[0]=1;strip[10]=200;integration.Feed("test-device",strip);strip[10]=0;integration.Feed("test-device",strip);
    Check(SpinWait.SpinUntil(()=>Count()==2,1000),"Pitch button routes strip and release");
    lock(received)Check(received[0]==(uint)(0xe0|(127<<8)|(127<<16))&&received[1]==(uint)(0xe0|(64<<16)),"Pitch returns to center on release");
    lock(received)received.Clear();control(11,false);strip[10]=200;integration.Feed("test-device",strip);strip[10]=0;integration.Feed("test-device",strip);
    Check(SpinWait.SpinUntil(()=>Count()==1,1000),"Mod sends CC1 without reset on release");
    control(11,false);Check(SpinWait.SpinUntil(()=>Count()==2,1000),"leaving Mod clears expression");
    fullState(36,1,1,1);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[38]==127&&f[39]==127&&f[52]==30;}},1000),"selected pad and mute/solo state");
    lock(received)received.Clear();
    control(26,false);integration.Feed("test-device",r);integration.Feed("test-device",release);
    Check(SpinWait.SpinUntil(()=>Count()==3,1000),"fixed velocity note delivery");
    lock(received)Check(received[1]==(uint)(0x90|(36<<8)|(127<<16)),"configured fixed value reaches MIDI");
    fullState(36,1,1,1);control(26,true);
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"velocity screen completed");
    lock(frames)Check(Convert.ToBase64String(LatestTop(screens))==Convert.ToBase64String(BankScreen.TextReport("16 VEL BANK\n"+BankScreen.Mark(BankScreen.Range(36)),false)),"16 velocity screen identifies its editable bank");
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){var f=frames[frames.Count-1];for(int pad=0;pad<16;pad++)if((f[40+pad]&3)!=MikroInput.PadPosition(pad)/4)return false;return true;}},1000),"16 velocity uses all four brightness levels, rising one level per physical row");
    integration.Feed("test-device",r);var high=(byte[])r.Clone();high[1]=3;integration.Feed("test-device",high);
    Check(SpinWait.SpinUntil(()=>Count()==8,1000),"16 velocity repeats one pitch");
    lock(received){Check(received[5]==(uint)(0x90|(36<<8)|(1<<16))&&received[6]==(uint)(0x80|(36<<8))&&received[7]==(uint)(0x90|(36<<8)|(127<<16)),"16 velocity endpoint notes and retrigger");}
    integration.Feed("test-device",release);Check(Count()==8,"overlap stays sounding");
    high[2]=0x30;high[3]=0;integration.Feed("test-device",high);Check(SpinWait.SpinUntil(()=>Count()==9,1000),"last overlap releases MIDI");
    control(26,true);control(37,false);control(38,false);
    Check(SpinWait.SpinUntil(()=>Count()==15,1000),"solo and mute commands delivered");
    lock(received){Check(received[11]==(uint)(0xb0|(30<<8)|(127<<16))&&received[13]==(uint)(0xb0|(29<<8)|(127<<16)),"solo and mute CC mapping");}
    fullState(-1,0,0,0);control(37,false);control(38,false);Thread.Sleep(20);Check(Count()==15,"master ignores mix buttons");
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[38]==0&&f[39]==0;}},1000),"master mix buttons off");
    integration.Feed("test-device",r);Check(SpinWait.SpinUntil(()=>Count()==17,1000),"held note before target change");
    var changed=Encoding.ASCII.GetBytes("MC7 65535 1 0 7 36 21 0 0 -1 0 0 1 ready");sender.Send(changed,changed.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>Count()==18,1000),"target change releases held pitch");
    Action<bool,string> keyboardState=(enabled,notes)=>{
     var packet=Encoding.ASCII.GetBytes("MK1 21 "+(enabled?1:0)+" 1 0 36 "+notes+" TWFqb3I= ready");sender.Send(packet,packet.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    };
    string major="36,38,40,41,43,45,47,48,50,52,53,55,57,59,60,62";
    keyboardState(true,major);
    lock(received)received.Clear();control(8,false);control(8,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][9]==0 && frames[frames.Count-1][10]==124;},1000),"Keyboard parameter selection stops at Root Note");
    control(9,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][9]==124 && frames[frames.Count-1][10]==124;},1000),"Scale sits between Root Note and Scale Mode");
    var pageEncoder=new byte[64];pageEncoder[0]=1;pageEncoder[7]=1;integration.Feed("test-device",pageEncoder);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(66<<8)|(1<<16)));},1000),"Scale page routes ordinary encoder turns to scale selection");
    control(9,false);control(9,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][9]==124 && frames[frames.Count-1][10]==0;},1000),"Keyboard parameter selection stops at Scale Mode");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport("SCALE MODE\n"+BankScreen.Mark("ON"),true));},1000),"third Keyboard page displays Live Scale Mode");
    lock(received)received.Clear();pageEncoder[7]=15;integration.Feed("test-device",pageEncoder);pageEncoder[7]=14;integration.Feed("test-device",pageEncoder);pageEncoder[7]=15;integration.Feed("test-device",pageEncoder);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(78<<8))) && received.Contains((uint)(0xb0|(78<<8)|(127<<16)));},1000),"Scale Mode page sends explicit Off and On even before feedback arrives");
    lock(received){foreach(uint message in received)Check(message!=(uint)(0xb0|(34<<8)|(127<<16)),"Scale Mode encoder never toggles repeatedly");}
    control(8,false);control(8,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[28]==124&&f[29]==127&&f[52]==68&&f[53]==28;}},1000),"keyboard mode and tonic color");
    string arpStamp=(System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    feedback("MR2 21 120 0 0 "+arpStamp+" ready");control(17,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][18]==127&&frames[frames.Count-1][29]==124;},1000),"Keyboard Arp stays enabled after Note Repeat release");
    lock(received)received.Clear();var arpThird=(byte[])r.Clone();arpThird[1]=14;integration.Feed("test-device",r);integration.Feed("test-device",arpThird);
    Check(SpinWait.SpinUntil(()=>{lock(received){int ons=0;bool low=false,highNote=false;foreach(uint msg in received)if((msg&0xf0)==0x90){ons++;low|=((msg>>8)&127)==36;highNote|=((msg>>8)&127)==40;}return ons>=3&&low&&highNote;}},600),"Arp alternates held scale notes through real MIDI");
    var arpDial=new byte[64];arpDial[0]=1;arpDial[7]=1;integration.Feed("test-device",arpDial);arpDial[4]=2;integration.Feed("test-device",arpDial);control(9,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport("ARP TYPE\n"+BankScreen.Mark("UP"),true));},1000),"right arrow selects Arp parameter without changing the scale");
    arpDial[4]=0;arpDial[7]=0;integration.Feed("test-device",arpDial);
    feedback("MX1 21 1 1 0 0 LTYuMCBkQg== ready");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][4]==127&&frames[frames.Count-1][18]==127;},1000),"Volume temporarily owns encoder while Arp stays on");
    feedback("MX1 21 1 0 0 0 LTYuMCBkQg== ready");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport("ARP TYPE\n"+BankScreen.Mark("UP"),true));},1000),"closing Volume restores the selected Arp page");
    control(28,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][29]==127&&frames[frames.Count-1][18]==127;},1000),"Keyboard restores normal controls with Arp in the background");
    lock(received)received.Clear();arpDial[7]=3;integration.Feed("test-device",arpDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(33<<8)|(1<<16)));},1000),"background Arp does not steal the Keyboard root encoder");
    control(17,true);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][29]==127;},1000),"Latch shortcut preserves the Keyboard screen while Arp runs in the background");
    integration.Feed("test-device",release);var arpRelease=(byte[])arpThird.Clone();arpRelease[2]=0x30;arpRelease[3]=0;integration.Feed("test-device",arpRelease);
    lock(received)received.Clear();
    Check(SpinWait.SpinUntil(()=>{lock(received){int notes=0;foreach(uint message in received)if((message&0xf0)==0x90)notes++;return notes>=2;}},400),"Latch keeps sending Arp notes after both physical pads are released");
    var latchShift=new byte[64];latchShift[0]=1;latchShift[4]=2;integration.Feed("test-device",latchShift);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][18]==127;},1000),"Shift shows enabled Latch as Bright");
    latchShift[3]=2;integration.Feed("test-device",latchShift);integration.Feed("test-device",latchShift);latchShift[3]=0;integration.Feed("test-device",latchShift);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][18]==124 && frames[frames.Count-1][29]==127;},1000),"Latch toggle is edge-triggered and Shift shows Off as Dim without leaving Keyboard");
    lock(received)received.Clear();Thread.Sleep(140);
    lock(received)foreach(uint message in received)Check((message&0xf0)!=0x90,"Latch off stops released notes immediately");
    latchShift[4]=0;integration.Feed("test-device",latchShift);integration.Feed("test-device",r);integration.Feed("test-device",arpThird);
    control(17,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][29]==124&&frames[frames.Count-1][18]==127;},1000),"Note Repeat reopens active background Arp settings");
    for(int page=0;page<4;page++)control(9,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][9]==124 && frames[frames.Count-1][10]==0 && Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport(BankScreen.Rhythm(1,255),true));},1000),"last Arp page shows the continuous rhythm as eight filled cells");
    var rhythmDial=new byte[64];rhythmDial[0]=1;rhythmDial[7]=1;integration.Feed("test-device",rhythmDial);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return Convert.ToBase64String(LatestTop(screens))==Convert.ToBase64String(BankScreen.TextReport(BankScreen.Rhythm(2,85),false)) && Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport(BankScreen.Rhythm(2,85),true));},1000),"Arp encoder selects a rhythm and its OLED diagram");
    rhythmDial[7]=0;integration.Feed("test-device",rhythmDial);for(int page=0;page<4;page++)control(8,false);
    control(17,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][18]==124;},1000),"pressing Note Repeat in its view disables Arp");
    int arpNotesAfterOff=0;lock(received)foreach(uint msg in received)if((msg&0xf0)==0x90)arpNotesAfterOff++;
    integration.Feed("test-device",r);integration.Feed("test-device",arpThird);Thread.Sleep(140);
    lock(received){int count=0;foreach(uint msg in received)if((msg&0xf0)==0x90)count++;Check(count==arpNotesAfterOff,"Arp disable consumes held pads without sounding an ordinary chord");}
    integration.Feed("test-device",release);arpThird[2]=0x30;arpThird[3]=0;integration.Feed("test-device",arpThird);
    lock(received)received.Clear();var key=(byte[])r.Clone();key[1]=14;integration.Feed("test-device",key);
    Check(SpinWait.SpinUntil(()=>Count()==2,1000),"scale note delivery");
    lock(received)Check(received[1]==(uint)(0x90|(40<<8)|(100<<16)),"third keyboard pad plays major third");
    keyboardState(true,"36,38,39,41,43,44,46,48,50,51,53,55,56,58,60,62");key[2]=0x30;key[3]=0;integration.Feed("test-device",key);
    Check(SpinWait.SpinUntil(()=>Count()==3,1000),"keyboard note release after scale change");
    lock(received)Check(received[2]==(uint)(0x80|(40<<8)),"keyboard release retains original pitch");
    var dial=new byte[64];dial[0]=1;integration.Feed("test-device",dial);dial[7]=1;integration.Feed("test-device",dial);
    Check(SpinWait.SpinUntil(()=>Count()==4,1000),"encoder turn forwarded");
    lock(received)Check(received[3]==(uint)(0xb0|(33<<8)|(1<<16)),"encoder relative CC");
    control(39,false);control(27,false);control(28,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(31<<8)|(127<<16)))&&received.Contains((uint)(0xb0|(32<<8)|(127<<16)));},1000),"pad and keyboard mode commands delivered without scale toggle");
    // Resetting synthetic button reports may also move the synthetic encoder back.
    lock(received){Check(!received.Contains((uint)(0xb0|(34<<8)|(127<<16)))&&received.Contains((uint)(0xb0|(31<<8)|(127<<16)))&&received.Contains((uint)(0xb0|(32<<8)|(127<<16))),"encoder press leaves Scale Mode alone while mode buttons still work");}
    lock(received)received.Clear();control(39,true);
    lock(received)Check(!received.Contains((uint)(0xb0|(34<<8)|(127<<16))) && !received.Contains((uint)(0xb0|(78<<8)|(127<<16))),"Shift encoder press no longer changes Scale Mode in Keyboard");
    lock(received)Check(!received.Contains((uint)(0xb0|(36<<8)|(127<<16))),"Shift encoder press does not reset the Keyboard range");
    keyboardState(false,major);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[28]==127&&f[29]==124;}},1000),"Pad Mode restores layout lighting");
    KeyboardState invalid;Check(!KeyboardState.TryParse("MK1 21 1 1 0 36 36,35 TWFqb3I= ready".Split(' '),out invalid),"malformed keyboard packet rejected");
    Check(BankScreen.TextReport(new string('A',200)+"\nMAJOR\nSCALE ON",false).Length==265,"OLED clips long scale names safely");
    var bottomText=BankScreen.TextReport("C MAJOR\nC1 SCALE ON",true);bool bottomPixels=false;for(int i=9;i<265;i++)if(bottomText[i]!=255)bottomPixels=true;Check(bottomPixels,"multiline keyboard text uses lower OLED half");
    pulse(40,21);integration.Tick("test-device",true,false);
    Check(integration.FastRefreshActive,"short flash starts temporary refresh timer");
    Check(SpinWait.SpinUntil(()=>{System.Windows.Forms.Application.DoEvents();lock(frames)return frames[frames.Count-1][48]==30&&!integration.FastRefreshActive;},400),"flash expires without process/state polling and timer stops");
    var editState=Encoding.ASCII.GetBytes("ME1 21 17 ready");sender.Send(editState,editState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    lock(received)received.Clear();var shifted=new byte[64];shifted[0]=1;shifted[4]=2;integration.Feed("test-device",shifted);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[52]==68&&f[48]==68&&f[53]==0&&f[40]==0;}},1000),"Shift pads show available edit commands in Dim white");
    integration.Feed("test-device",r);integration.Feed("test-device",r);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][52]==71;},1000),"edit command flashes Bright white");
    var redoAvailable=Encoding.ASCII.GetBytes("ME1 21 3 ready");sender.Send(redoAvailable,redoAvailable.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][53]==68;},1000),"Redo lights white as soon as Live permits repeat");
    shifted[4]=0;integration.Feed("test-device",shifted);integration.Feed("test-device",release);
    Check(SpinWait.SpinUntil(()=>Count()==3,1000),"modifier and command delivered without musical notes");
    lock(received){Check(received[0]==(uint)(0xb0|(38<<8)|(127<<16))&&received[1]==(uint)(0xb0|(37<<8)|(1<<16))&&received[2]==(uint)(0xb0|(38<<8)),"Shift and edit CC mapping");}
    var selectionState=Encoding.ASCII.GetBytes("MV1 21 1 1 3 1 ready");sender.Send(selectionState,selectionState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[34]==127&&f[52]==30&&f[53]==28&&f[54]==0;}},1000),"Events button and pitch selection masks");
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"Events screen completes");
    lock(frames)Check(Convert.ToBase64String(LatestTop(screens))==Convert.ToBase64String(BankScreen.TextReport("EVENTS\n"+BankScreen.Mark("NOTE SELECT"),false)),"Events OLED highlights encoder note selection");
    lock(received)received.Clear();integration.Feed("test-device",r);integration.Feed("test-device",r);integration.Feed("test-device",release);
    var eventDial=new byte[64];eventDial[0]=1;eventDial[7]=1;integration.Feed("test-device",eventDial);
    eventDial[4]=2;integration.Feed("test-device",eventDial);eventDial[7]=2;integration.Feed("test-device",eventDial);
    Check(SpinWait.SpinUntil(()=>Count()==4,1000),"Events pad and encoder routes without notes");
    lock(received){Check(received[0]==(uint)(0xb0|(40<<8)|(36<<16))&&received[1]==(uint)(0xb0|(41<<8)|(1<<16))&&received[3]==(uint)(0xb0|(42<<8)|(1<<16)),"Events pitch, event navigation and range MIDI commands");}
    eventDial[4]=0;integration.Feed("test-device",eventDial);control(33,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(39<<8)|(127<<16)));},1000),"Events button transport CC");
    selectionState=Encoding.ASCII.GetBytes("MV1 21 0 0 0 0 ready");sender.Send(selectionState,selectionState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][34]==0;},1000),"Events turns off when no editor target exists");
    var loopState=Encoding.ASCII.GetBytes("MT1 21 1 ready");sender.Send(loopState,loopState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][19]==127;},1000),"Restart LED follows native Loop");
    lock(received)received.Clear();control(18,false);control(18,true);control(19,false);
    Check(SpinWait.SpinUntil(()=>Count()==6,1000),"Restart Loop and modifier delivered; Erase ignored outside Events");
    lock(received)Check(received.Contains((uint)(0xb0|(45<<8)|(127<<16)))&&received.Contains((uint)(0xb0|(46<<8)|(127<<16))),"Restart and Loop CC routing");
    selectionState=Encoding.ASCII.GetBytes("MV2 21 1 1 3 1 1 ready");sender.Send(selectionState,selectionState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][20]==124;},1000),"Erase available only with Events selection");control(19,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(47<<8)|(127<<16)));},1000),"Events Erase CC");
    selectionState=Encoding.ASCII.GetBytes("MV2 21 1 0 0 0 0 ready");sender.Send(selectionState,selectionState.Length,new IPEndPoint(IPAddress.Loopback,39474));
    var menuState=Encoding.ASCII.GetBytes("MK2 21 1 1 0 36 "+major+" TWFqb3I= 0 ready");sender.Send(menuState,menuState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"keyboard menu screen completes");
    lock(frames)Check(Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport("ROOT NOTE\n"+BankScreen.Mark("C1"),true)),"Keyboard Root Note page shows its value on the lower row");
    var scaleFocusReport=new byte[64];scaleFocusReport[0]=1;control(9,false);
    string focusedScale=Convert.ToBase64String(BankScreen.TextReport("SCALE\n"+BankScreen.Mark("Major"),true));
    Check(SpinWait.SpinUntil(()=>{lock(frames)return screens.Count>=2&&Convert.ToBase64String(LatestBottom(screens))==focusedScale;},1000),"right arrow opens Scale with its value on the lower row");
    control(8,false);integration.Feed("test-device",scaleFocusReport);
    feedback("MX1 21 1 0 1 1 LTYuMCBkQg== ready");groupControl(8,false);Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(50<<8)|(127<<16)));},1000),"Group arrow selects previous track");
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[9]==0&&f[10]==124;}},1000),"Keyboard root page offers only the next parameter");
    var mixState=Encoding.ASCII.GetBytes("MX1 21 1 1 0 1 "+Convert.ToBase64String(Encoding.UTF8.GetBytes("-6.0 dB"))+" ready");sender.Send(mixState,mixState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[4]==127&&f[29]==124;}},1000),"Volume focus lights Volume and dims Keyboard");
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"Volume screen completes");
    lock(frames)Check(Convert.ToBase64String(LatestTop(screens))==Convert.ToBase64String(BankScreen.TextReport("VOLUME\n"+BankScreen.Mark("-6.0 dB"),false)),"OLED highlights Live formatted dB value");
    feedback("MG1 21 1 0 1/16 ready");var volumeGrid=new byte[64];volumeGrid[0]=1;volumeGrid[3]=32;volumeGrid[4]=2;integration.Feed("test-device",volumeGrid);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][4]==124&&frames[frames.Count-1][22]==127;},1000),"held Grid temporarily dims Volume encoder focus");
    volumeGrid[3]=0;integration.Feed("test-device",volumeGrid);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][4]==127;},1000),"releasing Follow restores previous Volume focus");
    volumeGrid[4]=0;integration.Feed("test-device",volumeGrid);
    lock(received)received.Clear();var volumeDial=new byte[64];volumeDial[0]=1;volumeDial[7]=1;integration.Feed("test-device",volumeDial);
    Check(SpinWait.SpinUntil(()=>Count()==1,1000),"Volume turn delivered");lock(received)Check(received[0]==(uint)(0xb0|(49<<8)|(1<<16)),"Volume overrides keyboard encoder route");
    volumeDial[2]=64;volumeDial[4]=2;integration.Feed("test-device",volumeDial);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[9]==0&&f[10]==127;}},1000),"Group + Shift highlights MIDI track creation");
    groupControl(9,false);control(3,false);control(39,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(48<<8)|(127<<16)))&&received.Contains((uint)(0xb0|(51<<8)|(127<<16)));},1000),"Volume button and next-track mapping");
    lock(received)Check(!received.Contains((uint)(0xb0|(34<<8)|(127<<16))),"encoder press does not toggle scale while adjusting volume");
    mixState=Encoding.ASCII.GetBytes("MX1 21 1 0 0 1 LTYuMCBkQg== ready");sender.Send(mixState,mixState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[4]==124&&f[29]==127;}},1000),"leaving Volume restores Keyboard focus");
    control(39,false);
    var ui=Encoding.ASCII.GetBytes("MCU1 21 1 123.4 ready");sender.Send(ui,ui.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][6]==127;},1000),"Tempo focus LED");
    lock(received)received.Clear();volumeDial[2]=0;volumeDial[4]=0;volumeDial[7]=2;integration.Feed("test-device",volumeDial);control(39,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(55<<8)|(1<<16)));},1000),"Tempo encoder route");
    lock(received)Check(!received.Contains((uint)(0xb0|(34<<8)|(127<<16)))&&!received.Contains((uint)(0xb0|(52<<8)|(127<<16))),"Tempo press leaves scale and volume unchanged");
    ui=Encoding.ASCII.GetBytes("MCU1 21 0 123.4 ready");sender.Send(ui,ui.Length,new IPEndPoint(IPAddress.Loopback,39474));
    ui=Encoding.ASCII.GetBytes("MB1 21 1 ready");sender.Send(ui,ui.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][3]==127;},1000),"Browse LED");
    foreach(string request in new[]{"MBK1 21 13","MBK1 20 13","MBK1 21 65"}){var keyPacket=Encoding.ASCII.GetBytes(request);sender.Send(keyPacket,keyPacket.Length,new IPEndPoint(IPAddress.Loopback,39474));}
    integration.Tick("test-device",true,false);
    Check(browserKeys.Count==1&&browserKeys[0]==13,"browser keyboard bridge accepts current epoch and allowed keys only");
    lock(received)received.Clear();var fastBrowser=new byte[64];fastBrowser[0]=1;fastBrowser[4]=2;fastBrowser[7]=0;integration.Feed("test-device",fastBrowser);fastBrowser[7]=1;integration.Feed("test-device",fastBrowser);
    Check(SpinWait.SpinUntil(()=>{lock(received){int turns=0;foreach(uint value in received)if(value==(uint)(0xb0|(58<<8)|(6<<16)))turns++;return turns==1;}},1000),"Shift browser encoder sends one six-row displacement per detent");
    control(1,false);Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(68<<8)|(127<<16)));},1000),"Favorites hardware button routes only in Browse");
    control(39,false);control(39,true);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(59<<8)|(127<<16)))&&received.Contains((uint)(0xb0|(60<<8)|(127<<16)));},1000),"Browse Enter and Back routing");
    control(2,false);control(5,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(57<<8)|(127<<16)))&&received.Contains((uint)(0xb0|(54<<8)|(127<<16)));},1000),"Browse and Tempo hardware buttons");
    ui=Encoding.ASCII.GetBytes("MB1 21 0 ready");sender.Send(ui,ui.Length,new IPEndPoint(IPAddress.Loopback,39474));
    ui=Encoding.ASCII.GetBytes("MS1 21 1 1 36 1 2 65535 1 0 7 12 25 13 1 ready");sender.Send(ui,ui.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[31]==127&&f[36]==124&&f[52]==71&&f[68]==69&&f[69]==71&&f[56]==28;}},1000),"Step grid and proportional colored strip with white page and bright playhead");
    lock(received)received.Clear();var stepPad=new byte[64];stepPad[0]=2;stepPad[1]=12;stepPad[2]=0x1c;stepPad[3]=0x80;integration.Feed("test-device",stepPad);stepPad[2]=0x30;stepPad[3]=0;integration.Feed("test-device",stepPad);
    Check(SpinWait.SpinUntil(()=>Count()>=1,1000),"step pad delivered");
    lock(received){Check(received.Contains((uint)(0xb0|(62<<8))),"step zero is a command");foreach(uint message in received)Check((message&0xf0)!=0x90&&(message&0xf0)!=0x80,"step pads never send musical notes");}
    control(30,false);control(35,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(61<<8)|(127<<16)))&&received.Contains((uint)(0xb0|(63<<8)|(127<<16)));},1000),"Step and Duplicate hardware buttons");
    StepState invalidStep;Check(!StepState.TryParse("MS1 21 1 1 36 2 2 65535 1 0 7 12 25 13 1 ready".Split(' '),out invalidStep),"invalid step page rejected");
    keyboardState(false,major);
    var previewSelect=new byte[64];previewSelect[0]=1;previewSelect[5]=16;integration.Feed("test-device",previewSelect);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[28]==127&&f[37]==127&&f[31]==0&&f[9]==0&&f[10]==0&&f[36]==0;}},1000),"Step preview lights Pad Mode and disables other controls");
    lock(received)received.Clear();stepPad[2]=0x1c;stepPad[3]=0x80;integration.Feed("test-device",stepPad);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0x90|(36<<8)|(100<<16)))&&received.Contains((uint)(0xb0|(28<<8)|(36<<16)));},1000),"Step Select auditions and selects Pad Mode pitch despite Keyboard layout");
    lock(received)Check(!received.Contains((uint)(0xb0|(62<<8))),"audition never toggles a step");
    foreach(int blockedBit in new[]{9,30,35,3}){var blockedPress=new byte[64];blockedPress[0]=1;blockedPress[5]=16;blockedPress[1+blockedBit/8]|=(byte)(1<<(blockedBit%8));integration.Feed("test-device",blockedPress);integration.Feed("test-device",previewSelect);}
    lock(received){foreach(uint message in received)if((message&0xf0)==0xb0)Check(((message>>8)&127)==28||((message>>8)&127)==38,"preview blocks navigation mode changes duplicate and volume");}
    var soundDial=(byte[])previewSelect.Clone();soundDial[7]=1;integration.Feed("test-device",soundDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(77<<8)|(1<<16)));},1000),"Step Select encoder changes the audition range rather than pattern pages");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport("PAD BANK\n"+BankScreen.Mark(BankScreen.Range(36)),true));},1000),"drum audition screen highlights its note bank");
    var resetSound=(byte[])previewSelect.Clone();resetSound[5]|=128;integration.Feed("test-device",resetSound);integration.Feed("test-device",resetSound);integration.Feed("test-device",previewSelect);
    Check(SpinWait.SpinUntil(()=>{lock(received){int resets=0;foreach(uint message in received)if(message==(uint)(0xb0|(79<<8)|(127<<16)))resets++;return resets==1;}},1000),"Step Select encoder press resets the audition range once per click");
    keyboardState(true,major);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][29]==127&&frames[frames.Count-1][28]==0;},1000),"melodic Step Select shows Keyboard rather than Pad Mode");
    var melodyPad=new byte[64];melodyPad[0]=2;melodyPad[1]=14;melodyPad[2]=0x1c;melodyPad[3]=0x80;integration.Feed("test-device",melodyPad);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0x90|(40<<8)|(100<<16)));},1000),"melodic Step Select plays the scale's third note rather than a chromatic pitch");
    melodyPad[2]=0x30;melodyPad[3]=0;integration.Feed("test-device",melodyPad);
    feedback("MK2 21 1 1 1 37 37,39,41,42,44,46,48,49,51,53,54,56,58,60,61,63 TWFqb3I= 0 ready");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport("ROOT NOTE\n"+BankScreen.Mark("C#1"),true));},1000),"Keyboard audition display follows changes to the root note");
    keyboardState(false,major);
    Check(SpinWait.SpinUntil(()=>{pulse(37,21);integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][53]==30;},1000),"playback flashes continue on normal pad layout during preview");
    previewSelect[5]=0;integration.Feed("test-device",previewSelect);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0x80|(36<<8)));},1000),"Select release sends audition note off");
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[31]==127&&f[52]==71;}},1000),"Select release restores step grid");
    int afterPreview=Count();stepPad[2]=0x30;stepPad[3]=0;integration.Feed("test-device",stepPad);Check(Count()==afterPreview,"preview pad release cannot edit Step");
    keyboardState(true,major);
    lock(received)received.Clear();var pageDial=new byte[64];pageDial[0]=1;pageDial[7]=1;integration.Feed("test-device",pageDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(64<<8)|(1<<16)));},1000),"Step encoder scrolls pattern pages");
    feedback("MX1 21 1 0 1 1 LTYuMCBkQg== ready");groupControl(8,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(50<<8)|(127<<16)));},1000),"Group + Step arrow selects track");
    ui=Encoding.ASCII.GetBytes("MS1 21 1 0 36 1 2 65535 1 -1 7 12 25 -1 1 ready");sender.Send(ui,ui.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    lock(received)received.Clear();control(9,false);pageDial[4]=2;pageDial[7]=1;integration.Feed("test-device",pageDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(66<<8)|(1<<16)));},1000),"Scale page encoder selects scale type");
    ui=Encoding.ASCII.GetBytes("MK2 21 0 1 0 36 "+major+" TWFqb3I= 0 ready");sender.Send(ui,ui.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    pageDial[4]=0;integration.Feed("test-device",pageDial);lock(received)received.Clear();pageDial[7]=2;integration.Feed("test-device",pageDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(65<<8)|(1<<16)));},1000),"Pad Mode encoder selects bank");
    mixState=Encoding.ASCII.GetBytes("MX1 21 1 0 1 0 LTYuMCBkQg== ready");sender.Send(mixState,mixState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];return f[9]==0&&f[10]==0;}},1000),"Pad Mode arrows stay dark despite track neighbors");
    var createHint=new byte[64];createHint[0]=1;createHint[2]=64;createHint[4]=2;integration.Feed("test-device",createHint);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][10]==127;},1000),"Group + Shift right stays Bright at the end of the track list");
    createHint[2]=0;createHint[4]=0;integration.Feed("test-device",createHint);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][10]==0;},1000),"releasing Group restores dark Pad Mode arrows");
    feedback("MX1 21 1 0 1 1 LTYuMCBkQg== ready");groupControl(9,false);Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(51<<8)|(127<<16)));},1000),"Group Pad Mode arrow selects track");
    control(32,false);Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(70<<8)|(127<<16)));},1000),"Pattern button sends toggle");
    string patternColors="1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16";
    var patternState=Encoding.ASCII.GetBytes("MPT1 21 1 0 0 0 1 4096 0 0 6 "+patternColors+" ready");sender.Send(patternState,patternState.Length,new IPEndPoint(IPAddress.Loopback,39474));
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){var f=frames[frames.Count-1];return f[33]==127&&f[40]==55&&f[52]==4&&f[9]==0&&f[10]==0;}},1000),"Pattern colors, top-row playing state and grid arrow bounds");
    lock(received)received.Clear();integration.Feed("test-device",r);integration.Feed("test-device",release);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(71<<8)))&&received.Contains((uint)(0xb0|(71<<8)|(16<<16)));},1000),"Pattern pad press and release reach Live");
    lock(received)foreach(uint message in received)Check((message&0xf0)!=0x90,"Pattern never sends instrument notes");
    patternState=Encoding.ASCII.GetBytes("MPT4 21 1 0 0 0 1 4096 0 0 6 17 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 5 0 ready");sender.Send(patternState,patternState.Length,new IPEndPoint(IPAddress.Loopback,39474));
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return frames[frames.Count-1][52]==70&&frames[frames.Count-1][37]==124;},1000),"selected empty slot is white and Select is available");
    var clipSelectReport=new byte[64];clipSelectReport[0]=1;clipSelectReport[5]=16;integration.Feed("test-device",clipSelectReport);lock(received)received.Clear();integration.Feed("test-device",r);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(71<<8)|(48<<16)));},1000),"Select pad sends selection instead of clip launch");
    lock(received)foreach(uint message in received)Check(message!=(uint)(0xb0|(71<<8)),"selection never sends a launch command");
    var selectableState=Encoding.ASCII.GetBytes("MPT5 21 1 0 0 0 0 4096 8192 0 2 0 0 0 0 0 0 0 0 0 0 0 0 1 0 0 0 1 13 12288 ready");sender.Send(selectableState,selectableState.Length,new IPEndPoint(IPAddress.Loopback,39474));
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){var f=frames[frames.Count-1];for(int i=1;i<40;i++)if(f[i]!=(i==33||i==37?127:i==15?124:0))return false;for(int i=40;i<56;i++)if(f[i]!=(i==40?4:i==41?71:0))return false;return true;}},1000),"Pattern Select colors override playback; selection and Group remain available");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return Convert.ToBase64String(LatestTop(screens))==Convert.ToBase64String(BankScreen.TextReport("SCENES\n"+BankScreen.Mark("1-4"),false));},1000),"selection screen marks scene scrolling as the encoder target");
    lock(received)received.Clear();foreach(int blockedBit in new[]{2,22,23,24,30,31,32,33,35,3}){var blockedPress=(byte[])clipSelectReport.Clone();blockedPress[1+blockedBit/8]|=(byte)(1<<(blockedBit%8));integration.Feed("test-device",blockedPress);integration.Feed("test-device",clipSelectReport);}
    lock(received)Check(received.Count==0,"Pattern Select dark command buttons send no MIDI");
    feedback("MPT5 21 1 0 0 0 1 4096 0 0 6 0 0 0 0 0 0 0 0 0 0 0 0 1 0 0 0 1 13 65535 ready");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][9]==0&&frames[frames.Count-1][10]==0;},1000),"Pattern Select arrows stay dark outside Group");
    var selectNavigate=(byte[])clipSelectReport.Clone();selectNavigate[2]=2;integration.Feed("test-device",selectNavigate);integration.Feed("test-device",clipSelectReport);selectNavigate[2]=0;selectNavigate[4]=2;selectNavigate[7]=1;integration.Feed("test-device",selectNavigate);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(73<<8)|(1<<16)));},1000),"Pattern Select Shift encoder scrolls the frame horizontally");
    selectNavigate[2]=1;integration.Feed("test-device",selectNavigate);selectNavigate[2]=0;selectNavigate[4]=0;selectNavigate[7]=2;integration.Feed("test-device",selectNavigate);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(72<<8)|(1<<16)));},1000),"Pattern Select encoder scrolls scenes");
    integration.Feed("test-device",r);
    var selectNavigateHeld=(byte[])clipSelectReport.Clone();selectNavigateHeld[2]=2;integration.Feed("test-device",selectNavigateHeld);integration.Feed("test-device",clipSelectReport);
    lock(received)received.Clear();clipSelectReport[5]=0;integration.Feed("test-device",clipSelectReport);integration.Feed("test-device",r);integration.Feed("test-device",release);
    lock(received)Check(!received.Contains((uint)(0xb0|(71<<8))),"a selection pad held through navigation and Select release cannot launch a clip");
    clipSelectReport[5]=16;integration.Feed("test-device",clipSelectReport);
    var selectWithShift=(byte[])clipSelectReport.Clone();selectWithShift[4]=2;integration.Feed("test-device",selectWithShift);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][41]==71&&frames[frames.Count-1][33]==127;},1000),"Shift cannot expose the Stop layer during Select");
    clipSelectReport[5]=0;integration.Feed("test-device",clipSelectReport);integration.Feed("test-device",release);
    feedback("MV1 21 1 0 0 0 0 ready");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][34]==124;},1000),"Events lights in Pattern when MIDI note editing is available");
    patternState=Encoding.ASCII.GetBytes("MPT2 21 1 0 0 0 1 4096 0 0 6 "+patternColors+" 5 ready");sender.Send(patternState,patternState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    var patternShift=new byte[64];patternShift[0]=1;patternShift[4]=2;integration.Feed("test-device",patternShift);
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];for(int i=40;i<52;i++)if(f[i]!=0)return false;return f[52]==7&&f[53]==4&&f[54]==7&&f[55]==4;}},1000),"Shift Pattern lights only four red track Stop pads");
    lock(received)received.Clear();var blockedPatternPad=new byte[64];blockedPatternPad[0]=2;blockedPatternPad[1]=0;blockedPatternPad[2]=0x16;blockedPatternPad[3]=64;
    integration.Feed("test-device",blockedPatternPad);blockedPatternPad[2]=0x30;blockedPatternPad[3]=0;integration.Feed("test-device",blockedPatternPad);
    lock(received)Check(received.Count==0,"Stop layer upper pads send neither clip launches nor musical notes");
    lock(received)received.Clear();integration.Feed("test-device",r);integration.Feed("test-device",r);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(71<<8)|(32<<16)));},1000),"Shift Pattern pad sends track stop");
    lock(received)foreach(uint message in received){Check(message!=(uint)(0xb0|(21<<8)|(127<<16)),"track stop never sends transport stop");Check(message!=(uint)(0xb0|(71<<8)),"track stop never launches underlying clip");}
    patternShift[4]=0;integration.Feed("test-device",patternShift);integration.Feed("test-device",release);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][52]==4&&frames[frames.Count-1][53]==8;},1000),"releasing Shift restores bottom clip colors");
    lock(received)received.Clear();control(24,false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(21<<8)|(127<<16)));},1000),"transport Stop remains immediate in Pattern");
    control(9,false);var horizontalTurn=new byte[64];horizontalTurn[0]=1;horizontalTurn[4]=2;horizontalTurn[7]=1;integration.Feed("test-device",horizontalTurn);Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(73<<8)|(1<<16)));},1000),"Pattern Shift encoder scrolls tracks without changing selected track");
    control(8,false);var patternDial=new byte[64];patternDial[0]=1;patternDial[7]=1;integration.Feed("test-device",patternDial);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(72<<8)|(1<<16)));},1000),"Pattern encoder scrolls scenes");
    integration.Feed("test-device",r);lock(received)received.Clear();integration.Pause(false);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(71<<8)|(16<<16)));},1000),"pause releases a held clip launch button");
    integration.Tick("test-device",true,false);control(31,false);Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(74<<8)|(127<<16)));},1000),"Scene button sends distinct toggle");
    patternState=Encoding.ASCII.GetBytes("MPT3 21 2 0 0 0 1 32768 0 0 6 0 0 0 4 0 0 0 3 0 0 0 2 0 0 0 1 0 ready");sender.Send(patternState,patternState.Length,new IPEndPoint(IPAddress.Loopback,39474));
    Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames){var f=frames[frames.Count-1];if(f[32]!=127||f[33]!=124||f[9]!=0||f[10]!=0)return false;for(int i=0;i<16;i++)if(i%4!=3&&f[40+i]!=0)return false;return f[43]==7&&f[47]==8&&f[51]==12&&f[55]==16;}},1000),"Scene lights only right column and its own mode button");
    Check(SpinWait.SpinUntil(()=>{lock(frames)return frames[frames.Count-1][34]==124;},1000),"Events remains available in Scene for the open MIDI clip");
    lock(received)received.Clear();var sceneHit=new byte[64];sceneHit[0]=2;sceneHit[1]=3;sceneHit[2]=0x16;sceneHit[3]=64;integration.Feed("test-device",sceneHit);sceneHit[2]=0x30;sceneHit[3]=0;integration.Feed("test-device",sceneHit);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0xb0|(71<<8)|(40<<16)));},1000),"Scene top right pad launches first visible scene");
    patternState=Encoding.ASCII.GetBytes("MPT1 21 0 0 0 0 1 0 0 0 6 "+patternColors+" ready");sender.Send(patternState,patternState.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    PatternState rejected;Check(!PatternState.TryParse("MPT1 21 1 ready".Split(' '),out rejected),"malformed Pattern state rejected");
    Check(!PatternState.TryParse("MPT5 21 1 0 0 0 0 0 0 0 2 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 -1 65536 ready".Split(' '),out rejected),"out-of-range selectable mask rejected");
    var modeShortcut=new byte[64];modeShortcut[0]=1;modeShortcut[1]=1;modeShortcut[4]=2;integration.Feed("test-device",modeShortcut);
    Check(integration.PausedForMidiMode,"MIDI shortcut exposes mode pause to tray");
    integration.Tick(null,true,false);integration.Tick("test-device",true,false);
    Check(integration.PausedForMidiMode,"USB reconnect and Live heartbeat do not silently clear mode pause");
    integration.Resume();Check(!integration.PausedForMidiMode,"Resume explicitly clears MIDI mode pause");
    var recovered=Encoding.ASCII.GetBytes("MC7 65535 0 0 7 36 21 0 0 -1 0 0 1 ready");sender.Send(recovered,recovered.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);
    lock(received)received.Clear();integration.Feed("test-device",r);integration.Feed("test-device",release);
    Check(SpinWait.SpinUntil(()=>{lock(received)return received.Contains((uint)(0x90|(36<<8)|(100<<16)))&&received.Contains((uint)(0x80|(36<<8)));},1000),"native notes recover after MIDI mode pause and Resume");
    var groupHeld=new byte[64];groupHeld[0]=1;groupHeld[2]=64;integration.Feed("test-device",groupHeld);
    foreach(string title in new[]{"Analog","Bass"}){
     var display=Encoding.ASCII.GetBytes("MD1 21 "+Convert.ToBase64String(Encoding.UTF8.GetBytes(title))+" ready");sender.Send(display,display.Length,new IPEndPoint(IPAddress.Loopback,39474));
     string expectedScreen=Convert.ToBase64String(BankScreen.TextReport(title+"\n ",false));
     Check(SpinWait.SpinUntil(()=>{integration.Tick("test-device",true,false);lock(frames)return screens.Count>=2&&Convert.ToBase64String(LatestTop(screens))==expectedScreen;},1000),"track title and rename reach OLED independently of pad color");
    }
    groupHeld[2]=0;integration.Feed("test-device",groupHeld);
    Check(SpinWait.SpinUntil(()=>{lock(frames)return Convert.ToBase64String(LatestTop(screens))==Convert.ToBase64String(BankScreen.TextReport("PAD BANK\n"+BankScreen.Mark(BankScreen.Range(36)),false));},1000),"releasing Group restores the parameter heading instead of the track name");
    feedback("MN1 21 0 0 ready");
    Check(SpinWait.SpinUntil(()=>{lock(frames){var f=frames[frames.Count-1];for(int i=40;i<56;i++)if(f[i]!=0)return false;return f[39]==0 && f[4]!=0;}},1000),"Return/Main pads and Mute dark while Volume remains available");
    lock(received)received.Clear();integration.Feed("test-device",r);integration.Feed("test-device",release);control(38,false);
    Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"disabled note target completes output");
    lock(received){foreach(uint message in received)Check((message&0xf0)!=0x90 && message!=(uint)(0xb0|(29<<8)|(127<<16)),"disabled target suppresses notes and Mute");}
    feedback("MN1 21 1 1 ready");
    integration.Pause(true);
   }finally{midiInStop(handle);midiInClose(handle);}
  }
  var splashReports=new List<byte[]>();
  using(var integration=new AbletonIntegration(Console.WriteLine,(path,report)=>{if(report[0]==0xe0)lock(splashReports)splashReports.Add((byte[])report.Clone());},39474,browserKeyWriter:key=>{}))using(var sender=new UdpClient()){
   Action tick=()=>{var data=Encoding.ASCII.GetBytes("MC1 1 0 0 ready");sender.Send(data,data.Length,new IPEndPoint(IPAddress.Loopback,39474));integration.Tick("test-device",true,false);};
   Func<string,bool> screen=text=>{lock(splashReports)return splashReports.Count>=2 && Convert.ToBase64String(LatestTop(splashReports))==Convert.ToBase64String(BankScreen.TextReport(text,false)) && Convert.ToBase64String(LatestBottom(splashReports))==Convert.ToBase64String(BankScreen.TextReport(text,true));};
   Check(SpinWait.SpinUntil(()=>{tick();return screen(BankScreen.StartupSplash);},8000),"Live connection displays startup screen");
   var visibleTime=System.Diagnostics.Stopwatch.StartNew();
   Check(SpinWait.SpinUntil(()=>{tick();return visibleTime.ElapsedMilliseconds>=1200;},1800) && screen(BankScreen.StartupSplash),"startup screen remains visible past one second");
   Check(SpinWait.SpinUntil(()=>{tick();return screen("PAD BANK\n"+BankScreen.Mark(BankScreen.Range(36)));},2000),"startup screen expires into current mode");
   integration.Pause(true);Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"pause clears startup screen");
   Check(SpinWait.SpinUntil(()=>{tick();return !integration.IsWriting && screen("PAD BANK\n"+BankScreen.Mark(BankScreen.Range(36)));},1000),"resume skips startup screen");
   integration.Tick(null,true,false);Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"USB disconnect completes pending output");
   Check(SpinWait.SpinUntil(()=>{tick();return screen(BankScreen.StartupSplash);},1000),"USB reconnect with Live running repeats startup screen");
   integration.Pause(true);Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"pause interrupts reconnect screen");
   Check(SpinWait.SpinUntil(()=>{tick();return !integration.IsWriting && screen("PAD BANK\n"+BankScreen.Mark(BankScreen.Range(36)));},1000),"resume after reconnect skips startup screen");
   integration.Tick("test-device",false,false);Check(SpinWait.SpinUntil(()=>!integration.IsWriting,1000),"Live exit clears screen");
   Check(SpinWait.SpinUntil(()=>{tick();return screen(BankScreen.StartupSplash);},1000),"new Live session shows startup screen again");
  }
  Console.WriteLine("PASS Windows MIDI loopback, native HID mapping, pause releases and suppresses notes, async panel output, endpoint cleanup");return 0;
 }
}
