using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Passive native-mode experiment: Raw Input only, no HID/MIDI output or mode commands.
class HidCapture : Form {
 [StructLayout(LayoutKind.Sequential)] struct Device {public IntPtr handle;public uint type;}
 [StructLayout(LayoutKind.Sequential)] struct Registration {public ushort page,usage;public uint flags;public IntPtr target;}
 [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputDeviceList([Out]Device[] list,ref uint count,uint size);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,IntPtr data,ref uint size);
 [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(Registration[] devices,uint count,uint size);
 [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint header);
 readonly HashSet<IntPtr> known=new HashSet<IntPtr>();readonly HashSet<uint> usages=new HashSet<uint>();
 readonly Stopwatch clock=Stopwatch.StartNew();readonly Timer timer=new Timer();readonly StreamWriter log;readonly int duration;
 long nextScan;int reports;bool failed;
 void Log(string value){if(failed)return;try{if(log.BaseStream.Length>8*1024*1024){failed=true;BeginInvoke(new Action(Close));return;}log.WriteLine(DateTimeOffset.Now.ToString("o")+" elapsed="+clock.ElapsedMilliseconds+" "+value);}catch(IOException){failed=true;BeginInvoke(new Action(Close));}}
 public HidCapture(int seconds){
  duration=seconds*1000;string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","logs");Directory.CreateDirectory(dir);
  log=new StreamWriter(Path.Combine(dir,"hid-native-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".log"),false,Encoding.UTF8){AutoFlush=true};
  ShowInTaskbar=false;timer.Interval=250;timer.Tick+=(s,e)=>{try{if(clock.ElapsedMilliseconds>=duration){Close();return;}if(clock.ElapsedMilliseconds>=nextScan){nextScan=clock.ElapsedMilliseconds+1000;Scan();}}catch(Exception ex){Log("ERROR "+ex.Message);Close();}};
  var unused=Handle;Scan();Log("READY passive Raw Input; duration seconds="+seconds+" collections="+known.Count);timer.Start();
 }
 protected override void SetVisibleCore(bool value){base.SetVisibleCore(false);}
 void Scan(){
  uint count=0,size=(uint)Marshal.SizeOf(typeof(Device));if(GetRawInputDeviceList(null,ref count,size)==uint.MaxValue)throw new Exception("Raw Input inventory failed");
  var list=new Device[count];if(GetRawInputDeviceList(list,ref count,size)==uint.MaxValue)return;
  var seen=new HashSet<IntPtr>();
  foreach(var d in list){
   if(d.type!=2)continue;uint chars=0;GetRawInputDeviceInfo(d.handle,0x20000007,IntPtr.Zero,ref chars);if(chars==0||chars>32768)continue;
   IntPtr p=Marshal.AllocHGlobal((int)(chars+1)*2);string path;
   try{if(GetRawInputDeviceInfo(d.handle,0x20000007,p,ref chars)==uint.MaxValue)continue;path=Marshal.PtrToStringUni(p);}finally{Marshal.FreeHGlobal(p);}
   if(path==null||path.IndexOf("vid_17cc&pid_1700",StringComparison.OrdinalIgnoreCase)<0)continue;
   seen.Add(d.handle);if(known.Contains(d.handle))continue;
   p=Marshal.AllocHGlobal(32);uint bytes=32;
   try{Marshal.WriteInt32(p,32);if(GetRawInputDeviceInfo(d.handle,0x2000000b,p,ref bytes)==uint.MaxValue)continue;
    ushort page=(ushort)Marshal.ReadInt16(p,20),usage=(ushort)Marshal.ReadInt16(p,22);uint key=((uint)page<<16)|usage;
    if(!usages.Contains(key)){
     if(!RegisterRawInputDevices(new[]{new Registration{page=page,usage=usage,flags=0x100|0x2000|(usage==0?0x20u:0u),target=Handle}},1,(uint)Marshal.SizeOf(typeof(Registration))))throw new Exception("Raw Input registration failed: "+Marshal.GetLastWin32Error());
     usages.Add(key);
    }
    known.Add(d.handle);Log("CONNECTED device="+d.handle+" page="+page.ToString("X4")+" usage="+usage.ToString("X4"));
   }finally{Marshal.FreeHGlobal(p);}
  }
  known.RemoveWhere(delegate(IntPtr handle){if(seen.Contains(handle))return false;Log("DISCONNECTED device="+handle);return true;});
 }
 protected override void WndProc(ref Message m){
  try{if(m.Msg==0xff){
   uint bytes=0,header=(uint)(8+2*IntPtr.Size);GetRawInputData(m.LParam,0x10000003,IntPtr.Zero,ref bytes,header);
   if(bytes>=header+8 && bytes<=1048576){IntPtr p=Marshal.AllocHGlobal((int)bytes);
    try{if(GetRawInputData(m.LParam,0x10000003,p,ref bytes,header)!=uint.MaxValue&&Marshal.ReadInt32(p)==2&&known.Contains(Marshal.ReadIntPtr(p,8))){
     int length=Marshal.ReadInt32(p,(int)header),count=Marshal.ReadInt32(p,(int)header+4);
     if(length>0 && count>0 && (long)length*count<=bytes-header-8)for(int i=0;i<count;i++){
      var data=new byte[length];Marshal.Copy(IntPtr.Add(p,(int)header+8+i*length),data,0,length);reports++;
      Log("REPORT device="+Marshal.ReadIntPtr(p,8)+" length="+length+" hex="+BitConverter.ToString(data));
     }
    }}finally{Marshal.FreeHGlobal(p);}
   }
  }}catch(Exception ex){Log("ERROR "+ex.Message);}
  base.WndProc(ref m);
 }
 protected override void OnFormClosed(FormClosedEventArgs e){timer.Stop();timer.Dispose();Log("FINISHED reports="+reports);log.Dispose();base.OnFormClosed(e);}
 [STAThread] static int Main(string[] args){
  int seconds=300;if(args.Length>1 || (args.Length==1 && !int.TryParse(args[0],out seconds)) || seconds<1 || seconds>1800)return 2;
  bool created;using(var mutex=new System.Threading.Mutex(true,"Local\\MaschineCompanionHidCapture",out created)){
   if(!created)return 3;
   try{Application.Run(new HidCapture(seconds));return 0;}finally{mutex.ReleaseMutex();}
  }
 }
}
