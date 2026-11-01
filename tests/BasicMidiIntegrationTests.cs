using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

partial class NativeMidiTests {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ClockOutputCaps {public ushort manufacturer,product;public uint version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;public ushort technology,voices,notes,channelMask;public uint support;}
 [DllImport("winmm.dll")]static extern uint midiOutGetNumDevs();
 [DllImport("winmm.dll",CharSet=CharSet.Unicode)]static extern uint midiOutGetDevCapsW(UIntPtr id,out ClockOutputCaps caps,uint size);
 [DllImport("winmm.dll")]static extern uint midiOutOpen(out IntPtr handle,uint id,IntPtr callback,UIntPtr instance,uint flags);
 [DllImport("winmm.dll")]static extern uint midiOutShortMsg(IntPtr handle,uint message);
 [DllImport("winmm.dll")]static extern uint midiOutClose(IntPtr handle);
 static byte[] BasicPad(int kind){var report=new byte[64];report[0]=2;report[1]=12;report[2]=(byte)(kind|12);report[3]=128;return report;}
 static void BasicProfileTests(){
  string folder=Path.Combine(Path.GetTempPath(),"MikroBasicIntegration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"companion.ini"),"ClockSource=Internal\n");
  var store=new BasicDeviceStore(folder);store.Save("TEST0123",new BasicDeviceSettings{Profile=ControllerProfile.ControlSurface,Channel=7,FixedVelocity=81});
  var screens=new List<byte[]>();
  try{var owner=new FakeNi{Granted=true};using(var integration=new AbletonIntegration(Console.WriteLine,(p,f)=>{if(f.Length==265)lock(screens)screens.Add((byte[])f.Clone());},39477,browserKeyWriter:k=>{},showStartupSplash:false,ownership:owner,basicDirectory:folder))using(var sender=new UdpClient()){
   Action<bool> tick=host=>{owner.Granted=true;integration.Tick("test-device",host,false);};
   Check(SpinWait.SpinUntil(()=>{tick(false);return integration.BasicActive;},8000),"Basic works with no Live process or script");Check(integration.DeviceSerial=="TEST0123"&&integration.MidiChannel==7,"serial configuration is loaded before input");Check(integration.FixedVelocity==81,"per-device Fixed Velocity reaches the Live and Basic engines");
   uint input=uint.MaxValue;for(uint i=0;i<midiInGetNumDevs();i++){Caps caps;if(midiInGetDevCapsW((UIntPtr)i,out caps,(uint)Marshal.SizeOf(typeof(Caps)))==0&&caps.name=="Mikro Companion")input=i;}Check(input!=uint.MaxValue,"Basic input port");IntPtr handle;Check(midiInOpen(out handle,input,callback,UIntPtr.Zero,0x30000)==0,"Basic capture open");IntPtr clockOutput=IntPtr.Zero;
   try{Check(midiInStart(handle)==0,"Basic capture start");lock(received)received.Clear();
    Check(integration.Detect()&&integration.IdentificationActive,"Detect is available on the owned controller");
    Check(SpinWait.SpinUntil(()=>{lock(screens)return screens.Count>=2&&Convert.ToBase64String(LatestTop(screens))==Convert.ToBase64String(BankScreen.TextReport(BankScreen.SerialLabel("TEST0123"),false))&&Convert.ToBase64String(LatestBottom(screens))==Convert.ToBase64String(BankScreen.TextReport(BankScreen.SerialLabel("TEST0123"),true));},1000),"Detect sends the correct serial to both OLED halves");
    tick(false);Check(integration.IdentificationActive,"ordinary state updates do not expire Detect");
    integration.Feed("test-device",BasicPad(0x10));Check(SpinWait.SpinUntil(()=>Count()==1,1000),"Basic note delivered");Check(!integration.IdentificationActive,"first strike dismisses identification and still sends its note");lock(received)Check(received[0]==(uint)(0x96|(36<<8)|(100<<16)),"Basic channel and velocity are real MIDI");
    integration.SelectDisplayDim(true);var deviceSettings=new BasicDeviceStore(folder).Load("TEST0123");Check(deviceSettings.DisplayDim&&Count()==1,"per-device Dim is saved without cutting the held Basic note");
    Action live=()=>{byte[] packet=Encoding.ASCII.GetBytes("MC8 65535 0 0 11 36 2 0 0 -1 0 0 1 0 0 0 ready");sender.Send(packet,packet.Length,new IPEndPoint(IPAddress.Loopback,39477));tick(true);};
    live();Thread.Sleep(20);Check(integration.BasicActive&&Count()==1,"Live readiness does not cut a held Basic note");integration.Feed("test-device",BasicPad(0x30));Check(SpinWait.SpinUntil(()=>Count()==2,1000),"held Basic release delivered");live();Check(!integration.BasicActive&&integration.OwnsController,"idle Basic transitions to Live");
    var detectDial=new byte[64];detectDial[0]=1;integration.Feed("test-device",detectDial);lock(received)received.Clear();Check(integration.Detect(),"Detect works in Control surface too");detectDial[7]=1;integration.Feed("test-device",detectDial);Check(!integration.IdentificationActive&&SpinWait.SpinUntil(()=>Count()==1,1000),"first Live encoder movement dismisses Detect and is not swallowed");lock(received)Check(received[0]==(uint)(0xb0|(65<<8)|(1<<16)),"Live encoder keeps its normal bank command");
    integration.SelectProfile(ControllerProfile.Basic);Check(SpinWait.SpinUntil(()=>{live();return integration.BasicActive;},1000),"explicit Basic remains Basic with Live connected");Check(new BasicDeviceStore(folder).Load("TEST0123").Profile==ControllerProfile.Basic,"profile saved by serial");
    lock(received)received.Clear();var plugin=new byte[64];plugin[0]=1;plugin[1]=64;integration.Feed("test-device",plugin);plugin[1]=0;integration.Feed("test-device",plugin);Thread.Sleep(20);Check(Count()==0,"Basic does not emit proprietary Live control CCs");
    var basicIdle=new byte[64];basicIdle[0]=1;integration.Feed("test-device",basicIdle);Check(integration.Detect(),"repeat Detect");integration.Feed("test-device",basicIdle);Check(integration.IdentificationActive,"duplicate idle HID does not dismiss Detect");basicIdle[10]=100;integration.Feed("test-device",basicIdle);Check(!integration.IdentificationActive,"strip contact dismisses Detect");
    integration.SelectChannel(9);integration.Feed("test-device",BasicPad(0x10));integration.Feed("test-device",BasicPad(0x30));Check(SpinWait.SpinUntil(()=>Count()==2,1000),"channel change delivery");lock(received)Check((received[0]&255)==0x98&&(received[1]&255)==0x88,"note on/off use selected channel");
    AftertouchWireTests(integration,tick,live,folder);
    integration.SelectClock(MidiClockSource.External,"Mikro Companion Send");deviceSettings=new BasicDeviceStore(folder).Load("TEST0123");Check(deviceSettings.ClockSource==MidiClockSource.External&&deviceSettings.ClockInput=="Mikro Companion Send"&&deviceSettings.DisplayDim,"clock input/source and Dim share the selected serial settings");tick(true);uint output=uint.MaxValue;for(uint i=0;i<midiOutGetNumDevs();i++){ClockOutputCaps caps;if(midiOutGetDevCapsW((UIntPtr)i,out caps,(uint)Marshal.SizeOf(typeof(ClockOutputCaps)))==0&&caps.name=="Mikro Companion")output=i;}Check(output!=uint.MaxValue&&midiOutOpen(out clockOutput,output,IntPtr.Zero,UIntPtr.Zero,0)==0,"clock return port open");
    Check(midiOutShortMsg(clockOutput,0xfa)==0,"external Start send");for(int i=0;i<30;i++){Check(midiOutShortMsg(clockOutput,0xf8)==0,"external Clock send");Thread.Sleep(21);tick(true);}
    var clock=(BasicClock)typeof(AbletonIntegration).GetField("basicClock",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(integration);Console.WriteLine("External clock loopback: available="+clock.Read().Available+", tempo="+clock.Tempo);Check(clock.Read().Available&&clock.Tempo>=20&&clock.Tempo<=999,"external clock received through real virtual MIDI return route");
    midiOutShortMsg(clockOutput,0xfc);tick(true);Thread.Sleep(1100);tick(true);Check(!clock.Read().Available,"clock loss detected");lock(received)received.Clear();integration.Feed("test-device",BasicPad(0x10));integration.Feed("test-device",BasicPad(0x30));Check(SpinWait.SpinUntil(()=>Count()==2,1000),"ordinary notes still play while external clock waits");
    Check(integration.Detect(),"Detect before Pause");integration.Pause(true);Check(!integration.OwnsController&&!integration.IdentificationActive&&!integration.Detect(),"Pause cancels Detect and cannot reacquire the controller through identification");
   }finally{if(clockOutput!=IntPtr.Zero)midiOutClose(clockOutput);midiInStop(handle);midiInClose(handle);}
  }}finally{foreach(string file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
  Console.WriteLine("PASS Basic real MIDI channel routing, serial profile, deferred Live transition, external clock return route and clock-loss playing");
 }
 static byte[] ExpressionReport(int pad,int kind,int raw=0){var report=new byte[64];report[0]=2;report[1]=(byte)pad;report[2]=(byte)(kind|(raw>>8));report[3]=(byte)raw;return report;}
 static void AftertouchWireTests(AbletonIntegration integration,Action<bool> tick,Action live,string folder){
  Func<uint,int> count=packed=>{lock(received)return received.FindAll(v=>v==packed).Count;};
  Action clear=()=>{Thread.Sleep(20);lock(received)received.Clear();};
  Action<int,int,int> feed=(pad,kind,raw)=>integration.Feed("test-device",ExpressionReport(pad,kind,raw));
  Func<int,int,int,uint> message=(status,note,value)=>(uint)(status|(note<<8)|(value<<16));
  clear();integration.SelectAftertouch(AftertouchMode.Polyphonic);
  Check(new BasicDeviceStore(folder).Load("TEST0123").Aftertouch==AftertouchMode.Polyphonic,"aftertouch mode persists by serial");
  feed(12,0x40,4095);Thread.Sleep(15);Check(Count()==0,"pressure before Note On is not forwarded");
  feed(12,0x10,3200);feed(12,0x40,4095);
  Check(SpinWait.SpinUntil(()=>count(message(0xa8,36,127))==1,1000),"Basic poly pressure carries the note, channel and full-scale value");
  feed(12,0x40,4095);feed(12,0x40,4080);Thread.Sleep(20);Check(count(message(0xa8,36,127))==1,"duplicate quantized pressure is suppressed on the wire");
  Thread.Sleep(80);tick(true);Check(count(message(0xa8,36,0))==0,"quiet pressure interval retains the held value");
  feed(13,0x10,3200);feed(13,0x40,2048);feed(12,0x30,0);
  Check(SpinWait.SpinUntil(()=>count(message(0xa8,36,0))==1&&count(message(0xa8,37,64))==1,1000),"releasing one pad clears only that note's poly pressure");
  Check(count(message(0xa8,37,0))==0,"other note's pressure remains held");
  integration.SelectAftertouch(AftertouchMode.Channel);
  Check(SpinWait.SpinUntil(()=>count(message(0xa8,37,0))==1&&count(message(0xd8,64,0))==1,1000),"Channel selection clears Poly and applies the retained held value");
  feed(12,0x10,3200);feed(12,0x40,4095);feed(13,0x40,1024);
  Check(SpinWait.SpinUntil(()=>count(message(0xd8,127,0))==1,1000),"channel pressure is maximum, not sum");
  feed(12,0x30,0);Check(SpinWait.SpinUntil(()=>count(message(0xd8,32,0))==1,1000),"channel pressure falls back to the remaining pad");
  feed(13,0x30,0);Check(SpinWait.SpinUntil(()=>count(message(0xd8,0,0))==1,1000),"last release resets channel pressure");
  clear();feed(12,0x10,3200);feed(12,0x40,4095);feed(12,0x40,0);
  Check(SpinWait.SpinUntil(()=>count(message(0xd8,0,0))==1,1000)&&count(message(0x98,36,100))==1&&count(message(0x88,36,0))==0,"zero pressure does not release or retrigger the held note");feed(12,0x30,0);
  integration.SelectAftertouch(AftertouchMode.Polyphonic);clear();feed(12,0x10,3200);feed(12,0x40,4095);
  Check(SpinWait.SpinUntil(()=>count(message(0xa8,36,127))==1,1000),"poly pressure restored");
  integration.SelectChannel(10);Check(SpinWait.SpinUntil(()=>count(message(0xa8,36,0))==1&&count(message(0x88,36,0))==1,1000),"channel change cleans up old-channel pressure and note");
  clear();feed(12,0x40,4095);feed(12,0x10,3200);Thread.Sleep(15);Check(Count()==0,"cancelled held pad cannot send pressure into a new channel");feed(12,0x30,0);
  integration.SelectChannel(9);integration.SelectAftertouch(AftertouchMode.Off);clear();feed(12,0x10,3200);feed(12,0x40,4095);feed(12,0x30,0);Check(SpinWait.SpinUntil(()=>Count()==2,1000),"Off preserves ordinary notes with no expression traffic");
  integration.SelectProfile(ControllerProfile.ControlSurface);Check(SpinWait.SpinUntil(()=>{live();return !integration.BasicActive&&integration.OwnsController;},1000),"aftertouch test enters Live");
  integration.SelectAftertouch(AftertouchMode.Polyphonic);clear();feed(12,0x10,3200);feed(12,0x40,4095);
  Check(SpinWait.SpinUntil(()=>count(message(0xa0,36,127))==1,1000),"Control surface sends ordinary poly pressure on Live's note channel");
  feed(12,0x30,0);Check(SpinWait.SpinUntil(()=>count(message(0xa0,36,0))==1,1000),"Live release resets pressure");
  integration.SelectProfile(ControllerProfile.Basic);Check(SpinWait.SpinUntil(()=>{live();return integration.BasicActive;},1000),"return to Basic");
  clear();feed(12,0x10,3200);feed(12,0x40,4095);Check(SpinWait.SpinUntil(()=>count(message(0xa8,36,127))==1,1000),"Basic pressure before pause");
  integration.Pause(true);Check(SpinWait.SpinUntil(()=>count(message(0xa8,36,0))==1&&count(message(0x88,36,0))==1,1000),"Pause releases pressure and note");
  integration.Resume();Check(SpinWait.SpinUntil(()=>{tick(true);return integration.BasicActive;},1000),"resume after pressure cleanup");
  feed(12,0x30,0);integration.SelectAftertouch(AftertouchMode.Off);clear();
  Console.WriteLine("PASS real MIDI aftertouch modes, normalization, deduplication, independent voices, held mode change, channel cleanup, Live and Pause");
 }
}
