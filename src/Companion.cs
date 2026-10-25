using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

static class LaunchScreen {
 public static byte[] Report(bool visible,bool bottom) {
  byte[] report=new byte[265];byte[] header={0xe0,0,0,0,0,0x80,0,2,0};if(bottom)header[3]=2;Array.Copy(header,report,9);
  for(int i=9;i<265;i++)report[i]=0xff;
  if(visible && !bottom)for(int x=0;x<8;x++) {
   const int y=13;int index=9+(y/8)*128+x;
   report[index]=(byte)(report[index] & ~(1<<(y%8)));
  } // 8-pixel underscore at the left edge of the first text row
  return report;
 }
}
class TimedPause {
 public DateTimeOffset? Until;public bool Indefinite;
 public bool Active(DateTimeOffset now){return Indefinite || (Until.HasValue && now<Until.Value);}
 public void Start(DateTimeOffset now){Indefinite=false;Until=now.AddHours(1);}
 public void StartIndefinite(){Until=null;Indefinite=true;}
 public void Resume(){Until=null;Indefinite=false;}
}
class HoldGate {
 bool down, fired; long started; public bool IsDown {get{return down;}} public bool IsFired {get{return fired;}}
 public void Input(bool pressed,long now) { if(pressed && !down){started=now;fired=false;} if(!pressed)fired=false;down=pressed; }
 public bool Tick(long now,int threshold,bool blocked) { if(!down || fired || now-started<threshold)return false;fired=true;return !blocked; }
 public void Reset(){down=false;fired=false;}
 public void CancelUntilRelease(){fired=true;}
}
sealed class StandbyVegas {
 readonly Func<int,int,int> choose;readonly bool[] pads=new bool[16];
 bool ready;ulong buttons;int encoder=-1,strip,slot,color,lastLevel=-1;long started,next;
 public StandbyVegas(Func<int,int,int> random=null){var rng=new Random(Guid.NewGuid().GetHashCode());choose=random ?? new Func<int,int,int>(rng.Next);}
 public bool Animating {get{return slot!=0;}}
 public bool InputHeld {get{if(buttons!=0 || strip!=0)return true;foreach(bool pad in pads)if(pad)return true;return false;}}
 public void Ready(bool value,long now){if(!value || !ready){slot=0;next=0;lastLevel=-1;}ready=value;}
 public bool Input(byte[] report,long now){
  if(report==null || (report.Length!=64 && report.Length!=128))return false;
  bool changed=false;
  for(int offset=0;offset<report.Length;offset+=64){
   if(report[offset]==1){
    ulong current=0;for(int i=0;i<5;i++)current|=(ulong)report[offset+1+i]<<(8*i);
    int turn=report[offset+7]&15,touch=report[offset+10]|(report[offset+11]<<8);if(touch>200)touch=0;
    changed|=current!=buttons || encoder>=0 && turn!=encoder || touch!=strip;
    buttons=current;encoder=turn;strip=touch;
   }else if(report[offset]==2){
    for(int i=offset+1;i+2<offset+64;i+=3){int pad=report[i],kind=report[i+1]&0xf0;
     if(pad>=16)continue;
     if(kind==0x10){changed|=!pads[pad];pads[pad]=true;}
     else if(kind==0x30){changed|=pads[pad];pads[pad]=false;}
     else if(kind==0x40 && pads[pad])changed=true;
    }
   }
  }
  if(changed){slot=0;next=0;lastLevel=-1;}
  return changed;
 }
 public byte[] Frame(long now,byte standby,bool enabled=false){
  if(!enabled || !ready || InputHeld)return null;
  if(slot==0){if(now<next)return null;slot=choose(2,56);color=slot>=40?choose(1,18):0;started=now;lastLevel=-1;}
  int level=(int)((now-started)/500),levels=slot>=40?4:3;
  if(level==lastLevel)return null;
  lastLevel=level;var frame=new byte[81];frame[0]=0x80;frame[1]=standby;
  if(level>=levels){slot=0;next=now+choose(2000,7001);}
  else frame[slot]=(byte)(slot>=40?(color<<2)|(3-level):new[]{127,126,124}[level]);
  return frame;
 }
}
class Settings {
 public bool EnableAbleton=false;
 public bool EnableNativeHandoff=false;
 public string MidiActivityInputs="";public int FixedVelocity=127;public bool DisplayDim;
 public string Executable,Arguments="";public int HoldMs=1200;public bool StandbyLed=true,EnableClose=true,PauseWhenDawRunning=true;public int CloseHoldMs=2000;public string StandbyBrightness="Dim";public const string DefaultDaws="Ableton Live*;FL;FL64;FL (scaled);FL64 (scaled);FL (compatible memory);reaper;Cubase*;Nuendo*;Studio One;Bitwig Studio;ProTools;Cakewalk;Sonar";public string[] DawProcesses=ParseDaws(DefaultDaws);
 public byte StandbyBrightnessValue {get{return BrightnessValue(StandbyBrightness);}}
 public static byte BrightnessValue(string value) {
  if(string.Equals(value,"Dim",StringComparison.OrdinalIgnoreCase))return 0x7c;
  if(string.Equals(value,"Normal",StringComparison.OrdinalIgnoreCase))return 0x7e;
  if(string.Equals(value,"Bright",StringComparison.OrdinalIgnoreCase))return 0x7f;
  throw new Exception("StandbyBrightness must be Dim, Normal, or Bright");
 }

 public static string[] ParseDaws(string text) {
  var rules=new List<string>();
  foreach(string part in text.Split(';')) {
   string rule=part.Trim();if(rule.Length==0)continue;
   if(rule.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))rule=rule.Substring(0,rule.Length-4);
   if(rule.Length==0 || rule=="*" || rule.IndexOfAny(new[]{'\\','/',':','?'})>=0 || (rule.Contains("*") && (rule.IndexOf('*')!=rule.Length-1 || rule.LastIndexOf('*')!=rule.Length-1)))throw new Exception("DawProcesses: use process names separated by ;, with optional trailing *");
   rules.Add(rule);
  }
  return rules.ToArray();
 }
 public static string RestartRequired(Settings current,Settings next) {
  if(current.EnableAbleton!=next.EnableAbleton)return "EnableAbleton";
  if(current.EnableNativeHandoff!=next.EnableNativeHandoff)return "EnableNativeHandoff";
  if(current.MidiActivityInputs!=next.MidiActivityInputs)return "MidiActivityInputs";
  return null;
 }
 public static Settings Load(string path) {
  var s=new Settings();var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(string raw in File.ReadAllLines(path)) {string line=raw.Trim();if(line.Length==0 || line.StartsWith("#"))continue;int eq=line.IndexOf('=');if(eq<=0)throw new Exception("Invalid config line: "+line);values[line.Substring(0,eq).Trim()]=line.Substring(eq+1).Trim();}
  foreach(var pair in values) {
   switch(pair.Key.ToLowerInvariant()) {
    case "enableableton":s.EnableAbleton=bool.Parse(pair.Value);break;
    case "enablenativehandoff":s.EnableNativeHandoff=bool.Parse(pair.Value);break;
    case "displaydim":s.DisplayDim=bool.Parse(pair.Value);break;
    case "fixedvelocity":s.FixedVelocity=int.Parse(pair.Value);if(s.FixedVelocity<1 || s.FixedVelocity>127)throw new Exception("FixedVelocity must be 1..127");break;
    case "midiactivityinputs":s.MidiActivityInputs=pair.Value;break;
    case "executable":s.Executable=Environment.ExpandEnvironmentVariables(pair.Value.Trim('"'));break;
    case "arguments":s.Arguments=pair.Value;break;
    case "holdmilliseconds":s.HoldMs=int.Parse(pair.Value);break;
    case "enableclose":s.EnableClose=bool.Parse(pair.Value);break;
    case "closeholdmilliseconds":s.CloseHoldMs=int.Parse(pair.Value);break;
    case "standbybrightness":s.StandbyBrightness=pair.Value;BrightnessValue(pair.Value);break;
    case "pausewhendawrunning":s.PauseWhenDawRunning=bool.Parse(pair.Value);break;
    case "dawprocesses":s.DawProcesses=ParseDaws(pair.Value);break;
    case "standbyled":s.StandbyLed=bool.Parse(pair.Value);break;
    default:throw new Exception("Unknown config setting: "+pair.Key);
   }
  }
  if(s.Executable==null || !Path.IsPathRooted(s.Executable) || !File.Exists(s.Executable) || !s.Executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new Exception("Executable must be an existing absolute .exe path");
  s.Executable=Path.GetFullPath(s.Executable);if(s.HoldMs<500 || s.HoldMs>10000)throw new Exception("HoldMilliseconds must be 500..10000");if(s.CloseHoldMs<1000 || s.CloseHoldMs>10000)throw new Exception("CloseHoldMilliseconds must be 1000..10000");return s;
 }
}
class ProcessSnapshot {public bool Running,LedBlocked,AbletonRunning,NativeOwner;public string RunningDetails="",DawDetails="",NativeDetails="";}
static class Processes {
 public static ProcessSnapshot Observe(Settings settings) {
  var snapshot=new ProcessSnapshot();var running=new List<string>();var daws=new List<string>();var native=new List<string>();
  var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.GetFileNameWithoutExtension(settings.Executable),"Maschine 3","Maschine3","Maschine 2","Maschine2"};
  foreach(var process in Process.GetProcesses())using(process) {
   try {
    string name=process.ProcessName;
    if(name.StartsWith("Ableton Live",StringComparison.OrdinalIgnoreCase))snapshot.AbletonRunning=true;
    if(names.Contains(name)){snapshot.Running=true;running.Add(name+" PID "+process.Id);}
    if(name.Equals("Controller Editor",StringComparison.OrdinalIgnoreCase) || name.Equals("Komplete Kontrol",StringComparison.OrdinalIgnoreCase)){snapshot.LedBlocked=true;native.Add(name+" PID "+process.Id);}
    if(settings.PauseWhenDawRunning)foreach(string rule in settings.DawProcesses)if(MatchesDaw(name,rule)){daws.Add(name+" PID "+process.Id);break;}
   }catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
  }
  snapshot.NativeOwner=snapshot.Running || native.Count>0;snapshot.NativeDetails=string.Join(", ",native);snapshot.RunningDetails=string.Join(", ",running);snapshot.DawDetails=string.Join(", ",daws);snapshot.LedBlocked=snapshot.LedBlocked || snapshot.Running || daws.Count>0;return snapshot;
 }

 public static bool Running(string executable) {
  var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.GetFileNameWithoutExtension(executable),"Maschine 3","Maschine3","Maschine 2","Maschine2"};
  foreach(string name in names) {var found=Process.GetProcessesByName(name);bool any=found.Length>0;foreach(var p in found)p.Dispose();if(any)return true;}return false;
 }
 public static string Describe(string executable) {
  var descriptions=new List<string>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.GetFileNameWithoutExtension(executable),"Maschine 3","Maschine3","Maschine 2","Maschine2"};
  foreach(string name in names)foreach(var p in Process.GetProcessesByName(name))using(p)descriptions.Add(name+" PID "+p.Id);
  return string.Join(", ",descriptions);
 }
 public static bool MatchesDaw(string name,string rule) {
  return rule.EndsWith("*",StringComparison.Ordinal)?name.StartsWith(rule.Substring(0,rule.Length-1),StringComparison.OrdinalIgnoreCase):name.Equals(rule,StringComparison.OrdinalIgnoreCase);
 }
 public static string DawBlockers(Settings settings){return settings.PauseWhenDawRunning?DawBlockers(settings.DawProcesses):"";}
 public static string DawBlockers(string[] rules) {
  if(rules.Length==0)return "";
  var result=new List<string>();
  foreach(var process in Process.GetProcesses())using(process) {
   try {foreach(string rule in rules)if(MatchesDaw(process.ProcessName,rule)){result.Add(process.ProcessName+" PID "+process.Id);break;}}
   catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
  }
  return string.Join(", ",result);
 }
 public static bool DawHandoffAllowed(string[] rules) {
  bool found=false;
  foreach(var process in Process.GetProcesses())using(process) {
   try {
    bool matches=false;foreach(string rule in rules)if(MatchesDaw(process.ProcessName,rule)){matches=true;break;}
    if(!matches)continue;found=true;
    // Only the newly observed startup transition, never an existing host session.
    if((DateTime.Now-process.StartTime).TotalSeconds>3)return false;
    foreach(ProcessModule module in process.Modules)
     if(module.ModuleName.StartsWith("Maschine",StringComparison.OrdinalIgnoreCase))return false;
   }catch {return false;}
  }
  return found;
 }
 public static bool LedBlocked(string executable) {
  if(Running(executable))return true;
  foreach(string name in new[]{"Controller Editor","Komplete Kontrol"}){var found=Process.GetProcessesByName(name);bool any=found.Length>0;foreach(var p in found)p.Dispose();if(any)return true;}return false;
 }
}
static class DisplayContrast {
 // Observed through the licensed Hardware Preferences UI on Mikro MK3.
 public static byte[] Report(bool dim,int length) {
  if(length!=33)throw new Exception("Unsupported HID feature length="+length);
  var report=new byte[length];byte[] prefix={0xf8,0x80,0,0x20,0,1,0,0,0,(byte)(dim?0:100),0};Array.Copy(prefix,report,prefix.Length);return report;
 }
 public static void Save(string path,bool dim) {
  var lines=new List<string>(File.ReadAllLines(path));bool found=false;
  for(int i=0;i<lines.Count;i++){string line=lines[i].Trim();int eq=line.IndexOf('=');if(eq>0 && string.Equals(line.Substring(0,eq).Trim(),"DisplayDim",StringComparison.OrdinalIgnoreCase)){lines[i]="DisplayDim="+(dim?"true":"false");found=true;}}
  if(!found)lines.Add("DisplayDim="+(dim?"true":"false"));
  string temporary=path+".tmp";try{File.WriteAllLines(temporary,lines.ToArray());File.Replace(temporary,path,null);}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
}
static class StandbyLight {
 static int busy;
 public static bool IsBusy {get{return Interlocked.CompareExchange(ref busy,0,0)!=0;}}
 public static void Set(string path,byte brightness){RunWrite(delegate{SetCore(path,brightness);});}
 public static void Frame(string path,byte[] report){RunWrite(delegate{SetCore(path,0,report);});}
 public static void Contrast(string path,bool dim,Func<bool> allowed){RunWrite(delegate {
  if(!allowed())throw new Exception("Contrast skipped: controller unavailable");
  using(var file=CreateFile(path,0x40000000,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
   if(file.IsInvalid)throw new Exception("HID contrast open error="+Marshal.GetLastWin32Error());
   IntPtr data;if(!HidD_GetPreparsedData(file,out data))throw new Exception("HID descriptor unavailable");
   IntPtr caps=Marshal.AllocHGlobal(64);int length;
   try{if(HidP_GetCaps(data,caps)!=0x110000)throw new Exception("HID caps unavailable");length=(ushort)Marshal.ReadInt16(caps,8);}finally{Marshal.FreeHGlobal(caps);HidD_FreePreparsedData(data);}
   byte[] report=DisplayContrast.Report(dim,length);
   if(!allowed())throw new Exception("Contrast skipped: controller unavailable");
   if(!HidD_SetFeature(file,report,(uint)report.Length))throw new Exception("HID contrast error="+Marshal.GetLastWin32Error());
  }
 });}
 public static void Marker(string path,bool visible,Func<bool> allowed){RunWrite(delegate {
  foreach(bool bottom in new[]{false,true}) {
   if(!allowed())throw new Exception("Screen write skipped: native owner detected");
   SetCore(path,0,LaunchScreen.Report(visible,bottom));
  }
 });}
 internal static void RunWrite(Action write) {
  if(Interlocked.CompareExchange(ref busy,1,0)!=0)throw new Exception("A previous HID write is still pending");
  Task<Exception> operation;
  try {operation=Task.Factory.StartNew(delegate {
   try{write();return (Exception)null;}
   catch(Exception ex){return ex;}
   finally{Interlocked.Exchange(ref busy,0);}
  },CancellationToken.None,TaskCreationOptions.None,TaskScheduler.Default);}catch{Interlocked.Exchange(ref busy,0);throw;}
  // The worker retains its handle and buffers until native I/O has finished.
  if(!operation.Wait(600))throw new Exception("HID write did not finish; launch is blocked until it completes");
  if(operation.Result!=null)throw operation.Result;
 }

 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle file,out IntPtr data);
 [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
 [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,IntPtr caps);
 [DllImport("hid.dll",SetLastError=true)] static extern bool HidD_SetFeature(SafeFileHandle file,byte[] report,uint length);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteFile(SafeFileHandle file,IntPtr data,uint size,IntPtr written,IntPtr overlapped);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetOverlappedResult(SafeFileHandle file,IntPtr overlapped,out uint written,bool wait);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool CancelIoEx(SafeFileHandle file,IntPtr overlapped);
 // Shared write-only handle; never reads input or sends mode/feature reports.
 static void SetCore(string path,byte brightness,byte[] screen=null) {
  using(var file=CreateFile(path,0x40000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)) {
   if(file.IsInvalid)throw new Exception("HID shared write open error="+Marshal.GetLastWin32Error());
   IntPtr preparsed;if(!HidD_GetPreparsedData(file,out preparsed))throw new Exception("HID descriptor unavailable");
   IntPtr caps=Marshal.AllocHGlobal(64);int length;
   try {if(HidP_GetCaps(preparsed,caps)!=0x110000)throw new Exception("HID caps unavailable");length=(ushort)Marshal.ReadInt16(caps,6);}
   finally{Marshal.FreeHGlobal(caps);HidD_FreePreparsedData(preparsed);}
   if(length<81 || length>1024)throw new Exception("Unsupported HID output length="+length);
   // Community Mikro MK3 unified LED report: 0x80, first slot is Maschine.
   // Other LEDs are off in standby; never send while Maschine owns the workflow.
   byte[] report=new byte[length];if(screen!=null){if(screen.Length>length)throw new Exception("OLED report does not fit descriptor");Array.Copy(screen,report,screen.Length);}else{report[0]=0x80;report[1]=brightness;}
   IntPtr buffer=Marshal.AllocHGlobal(length);IntPtr ov=Marshal.AllocHGlobal(IntPtr.Size==8?32:20);
   using(var completion=new EventWaitHandle(false,EventResetMode.ManualReset)) {
    try {
     Marshal.Copy(report,0,buffer,length);for(int i=0;i<(IntPtr.Size==8?32:20);i++)Marshal.WriteByte(ov,i,0);
     Marshal.WriteIntPtr(ov,IntPtr.Size==8?24:16,completion.SafeWaitHandle.DangerousGetHandle());
     bool ok=WriteFile(file,buffer,(uint)length,IntPtr.Zero,ov);
     if(!ok && Marshal.GetLastWin32Error()!=997)throw new Exception("HID write error="+Marshal.GetLastWin32Error());
     if(!completion.WaitOne(250)) {CancelIoEx(file,ov);uint cancelled;GetOverlappedResult(file,ov,out cancelled,true);throw new Exception("HID LED write timeout");}
     uint written;if(!GetOverlappedResult(file,ov,out written,false) || written!=length)throw new Exception("HID LED report not completed");
    }finally{Marshal.FreeHGlobal(ov);Marshal.FreeHGlobal(buffer);}
   }
  }
 }
}
class Companion : Form {
 [DllImport("kernel32.dll")] static extern bool FreeConsole();
 [StructLayout(LayoutKind.Sequential)] struct Device {public IntPtr handle;public uint type;}
 [StructLayout(LayoutKind.Sequential)] struct Registration {public ushort page,usage;public uint flags;public IntPtr target;}
 [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputDeviceList([Out]Device[] list,ref uint count,uint size);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,IntPtr data,ref uint size);
 [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(Registration[] devices,uint count,uint size);
 [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint header);
 class Controller {public string Path;public HoldGate Hold=new HoldGate();public readonly StandbyVegas Vegas=new StandbyVegas();public bool LedAttempted,LedSet,MarkerSet;public bool? ContrastDim;public long ContrastRetryAt;public int Action;public long PressedAt;public IntPtr CloseWindow;public uint ClosePid;}
 readonly Dictionary<IntPtr,Controller> controllers=new Dictionary<IntPtr,Controller>();readonly HashSet<uint> registered=new HashSet<uint>();
 readonly Stopwatch clock=Stopwatch.StartNew();readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();NotifyIcon tray;StreamWriter log;Settings settings;string root;long lastScan=-1000,launchUntil;bool wasRunning;bool stopping;long lastProcessCheck=-1000;bool runningNow,ledBlockedNow,wasLedBlocked,dawBlockedNow;string dawDetails="";string status="",blockDetails="";ToolStripMenuItem statusItem,resumeItem,pauseItem,startupItem,vegasItem,dimItem;bool suspended,sessionLocked,sessionNotifications,sessionReportedLocked,sessionDisconnected;long lastDesktopCheck=-1000;long lastWake=-10000;string startupExecutable;readonly TimedPause pause=new TimedPause();Process launchedProcess;long focusDeadline,lastFocusCheck;IntPtr focusWindow;int focusAttempts;
 readonly EventWaitHandle exitSignal=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\MaschineCompanionExit");
 AbletonIntegration ableton;ToolStripMenuItem manualPauseItem;readonly object logGate=new object();
 bool abletonHost,nativeBlockedNow;bool logUnavailable;
 void Log(string text){lock(logGate)try{if(logUnavailable)return;if(log.BaseStream.Length>=2*1024*1024){log.Flush();log.BaseStream.SetLength(0);log.BaseStream.Position=0;log.WriteLine("Log truncated at 2 MiB");}log.WriteLine(DateTimeOffset.Now.ToString("o")+" "+text);}catch(IOException){logUnavailable=true;}catch(UnauthorizedAccessException){logUnavailable=true;}catch(ObjectDisposedException){logUnavailable=true;}}
 public Companion(string directory) {
  exitSignal.Reset();root=directory;settings=Settings.Load(Path.Combine(root,"companion.ini"));Directory.CreateDirectory(Path.Combine(root,"logs"));
  log=new StreamWriter(Path.Combine(root,"logs","companion-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".log"));log.AutoFlush=true;
  ShowInTaskbar=false;WindowState=FormWindowState.Minimized;
  var menu=new ContextMenuStrip();statusItem=new ToolStripMenuItem("Starting companion..."){Enabled=false};menu.Items.Add(statusItem);manualPauseItem=new ToolStripMenuItem("Pause",null,(s,e)=>PauseIndefinitely());menu.Items.Add(manualPauseItem);pauseItem=new ToolStripMenuItem("Pause for 1 hour",null,(s,e)=>PauseOneHour());menu.Items.Add(pauseItem);resumeItem=new ToolStripMenuItem("Resume",null,(s,e)=>ResumeNow()){Enabled=false};menu.Items.Add(resumeItem);vegasItem=new ToolStripMenuItem("Vegas mode",null,(s,e)=>ToggleVegas());menu.Items.Add(vegasItem);dimItem=new ToolStripMenuItem("Dim",null,(s,e)=>ToggleDim()){Checked=settings.DisplayDim};menu.Items.Add(dimItem);menu.Items.Add("Show status",null,(s,e)=>ShowStatus());menu.Items.Add("Reload config",null,(s,e)=>Reload());startupExecutable=System.Reflection.Assembly.GetExecutingAssembly().Location;startupItem=new ToolStripMenuItem("Start with Windows",null,(s,e)=>ToggleStartup());startupItem.Checked=StartupRegistration.Enabled(startupExecutable);menu.Items.Add(startupItem);menu.Items.Add("Open log folder",null,(s,e)=>Process.Start(Path.Combine(root,"logs")));menu.Items.Add("Exit",null,(s,e)=>Close());
  tray=new NotifyIcon{Icon=System.Drawing.SystemIcons.Application,Text="Maschine Companion",ContextMenuStrip=menu,Visible=true};
  timer.Interval=50;timer.Tick+=(s,e)=>SafeTick();timer.Start();Log("started hold="+settings.HoldMs+" executable="+settings.Executable);sessionLocked=!SessionState.InputAvailable();if(settings.EnableAbleton)ableton=new AbletonIntegration(Log,activityInputs:settings.MidiActivityInputs,fixedVelocity:settings.FixedVelocity,nativeHandoff:settings.EnableNativeHandoff);Scan();sessionNotifications=SessionState.WTSRegisterSessionNotification(Handle,0);if(!sessionNotifications)Log("session notification registration unavailable");SafeTick();
 }
 void PauseIndefinitely(){PauseOneHour();pause.StartIndefinite();Log("paused until resumed");UpdateStatus(clock.ElapsedMilliseconds);}
 void PauseOneHour(){
  if(ableton!=null)ableton.Pause(true);manualPauseItem.Visible=false;
  FinishFocus();foreach(var c in controllers.Values){c.Vegas.Ready(false,clock.ElapsedMilliseconds);c.Action=0;c.Hold.CancelUntilRelease();try{if(c.LedSet && !NativeLightBlocked())StandbyLight.Set(c.Path,0);}catch(Exception ex){Log("pause LED cleanup: "+ex.Message);}c.LedSet=false;}
  pause.Start(DateTimeOffset.UtcNow);resumeItem.Enabled=true;pauseItem.Visible=false;Log("paused until "+pause.Until.Value.ToString("o"));UpdateStatus(clock.ElapsedMilliseconds);
 }
 void ResumeNow(){pause.Resume();manualPauseItem.Visible=true;if(ableton!=null)ableton.Resume();resumeItem.Enabled=false;pauseItem.Visible=true;lastProcessCheck=-1000;foreach(var c in controllers.Values){c.Action=0;c.Hold.CancelUntilRelease();c.LedAttempted=false;}Log("manual pause ended");SafeTick();}
 void ToggleStartup(){try{StartupRegistration.Set(!startupItem.Checked,startupExecutable);startupItem.Checked=StartupRegistration.Enabled(startupExecutable);Log("Windows startup enabled="+startupItem.Checked);}catch(Exception ex){Log("startup registration error: "+ex.Message);tray.ShowBalloonTip(5000,"Startup error",ex.Message,ToolTipIcon.Error);}}
 void ToggleDim(){
  try{bool next=!settings.DisplayDim;DisplayContrast.Save(Path.Combine(root,"companion.ini"),next);settings.DisplayDim=next;dimItem.Checked=next;lastProcessCheck=-1000;Log("display Dim="+next);SafeTick();}
  catch(Exception ex){Log("display setting error: "+ex.Message);tray.ShowBalloonTip(5000,"Display setting error",ex.Message,ToolTipIcon.Error);}
 }
 void UpdateContrast(long now,bool allowed){
  foreach(var c in controllers.Values){
   if(!allowed){c.ContrastDim=null;continue;}
   if(c.ContrastDim==settings.DisplayDim || now<c.ContrastRetryAt || StandbyLight.IsBusy || ableton!=null && ableton.IsWriting)continue;
   if(c.Path.IndexOf("mi_00",StringComparison.OrdinalIgnoreCase)<0)continue;
   try{StandbyLight.Contrast(c.Path,settings.DisplayDim,delegate{return !stopping && !suspended && !sessionLocked && !pause.Active(DateTimeOffset.UtcNow) && !AbletonIntegration.NativeBlocked() && (abletonHost?ableton!=null && ableton.OwnsController:!Processes.LedBlocked(settings.Executable) && Processes.DawBlockers(settings).Length==0);});c.ContrastDim=settings.DisplayDim;Log("display contrast "+(settings.DisplayDim?"Dim":"Normal")+" applied");}
   catch(Exception ex){c.ContrastRetryAt=now+5000;Log("display contrast unavailable: "+ex.Message);}
  }
 }
 void ToggleVegas(){
  // Refresh ownership before accepting a menu action; the process cache may be stale.
  lastProcessCheck=-1000;SafeTick();
  if(!vegasItem.Checked && !vegasItem.Enabled)return;
  vegasItem.Checked=!vegasItem.Checked;
  foreach(var c in controllers.Values){c.Vegas.Ready(false,clock.ElapsedMilliseconds);c.LedAttempted=false;}
  Log("manual Vegas mode: "+(vegasItem.Checked?"on":"off"));SafeTick();
 }
 void ShowStatus(){MessageBox.Show(status+(blockDetails.Length>0?"\n"+blockDetails:"")+"\nWindows startup: "+(startupItem.Checked?"enabled":"disabled")+(runningNow && settings.EnableClose?"\nTo bring the configured app forward and request exit, hold for "+(settings.CloseHoldMs/1000.0).ToString("0.0",CultureInfo.InvariantCulture)+" s":"")+"\nConfig: "+Path.Combine(root,"companion.ini"),"Maschine Companion",MessageBoxButtons.OK,MessageBoxIcon.Information);}
 static string ProcessesLabel(string details){int pid=details.IndexOf(" PID ",StringComparison.Ordinal);return pid<0?details:details.Substring(0,pid);}
 void UpdateStatus(long now) {
  bool paused=pause.Active(DateTimeOffset.UtcNow) || ableton!=null && ableton.PausedForMidiMode;
  resumeItem.Enabled=paused;manualPauseItem.Visible=!paused;pauseItem.Visible=!paused;
  vegasItem.Enabled=vegasItem.Checked || settings.StandbyLed && !paused && !sessionLocked && !suspended && controllers.Count>0 && !abletonHost && !runningNow && !ledBlockedNow && now>=launchUntil;
  string next=sessionLocked?"Paused: Windows is locked or desktop unavailable":suspended?"Paused: computer is sleeping":pause.Active(DateTimeOffset.UtcNow)?(pause.Indefinite?"Paused until resumed":"Paused until "+pause.Until.Value.ToLocalTime().ToString("HH:mm")):controllers.Count==0?"Controller disconnected":abletonHost && ableton!=null?ableton.Status:dawBlockedNow?"Paused: "+dawDetails:runningNow?"Running: "+ProcessesLabel(blockDetails):ledBlockedNow?"Paused: "+blockDetails:now<launchUntil?"Waiting after launch request":"Ready: hold Maschine for "+(settings.HoldMs/1000.0).ToString("0.0",CultureInfo.InvariantCulture)+" s";
  if(next==status)return;status=next;statusItem.Text=next;tray.Text=next.Length>63?next.Substring(0,63):next;tray.Icon=runningNow?System.Drawing.SystemIcons.Information:System.Drawing.SystemIcons.Application;Log("status: "+next+(blockDetails.Length>0?"; "+blockDetails:""));
 }
 protected override void SetVisibleCore(bool value){base.SetVisibleCore(false);}
 void Reload(){try{var next=Settings.Load(Path.Combine(root,"companion.ini"));string restart=Settings.RestartRequired(settings,next);if(restart!=null)throw new Exception("Restart the companion after changing "+restart);if(settings.StandbyLed && !next.StandbyLed)foreach(var c in controllers.Values)if(c.LedSet && !LedOutputBlocked()){StandbyLight.Set(c.Path,0);c.LedSet=false;}settings=next;dimItem.Checked=settings.DisplayDim;if(ableton!=null)ableton.FixedVelocity=settings.FixedVelocity;lastProcessCheck=-1000;foreach(var c in controllers.Values){c.Hold.CancelUntilRelease();c.Action=0;c.LedAttempted=false;c.Vegas.Ready(false,clock.ElapsedMilliseconds);}Log("config reloaded");}catch(Exception ex){Log("config error: "+ex.Message);tray.ShowBalloonTip(5000,"Config error",ex.Message,ToolTipIcon.Error);}}
 void Scan() {
  uint count=0,size=(uint)Marshal.SizeOf(typeof(Device));if(GetRawInputDeviceList(null,ref count,size)==uint.MaxValue){Log("Raw Input enumeration failed");return;}
  Device[] devices=new Device[count];if(GetRawInputDeviceList(devices,ref count,size)==uint.MaxValue)return;
  var seen=new HashSet<IntPtr>();
  foreach(var d in devices) {
   if(d.type!=2)continue;uint chars=0;GetRawInputDeviceInfo(d.handle,0x20000007,IntPtr.Zero,ref chars);if(chars>32768)continue;
   IntPtr p=Marshal.AllocHGlobal((int)(chars+1)*2);string path;
   try{if(GetRawInputDeviceInfo(d.handle,0x20000007,p,ref chars)==uint.MaxValue)continue;path=Marshal.PtrToStringUni(p);}finally{Marshal.FreeHGlobal(p);}
   if(path==null || path.IndexOf("vid_17cc&pid_1700",StringComparison.OrdinalIgnoreCase)<0)continue;
   seen.Add(d.handle);if(controllers.ContainsKey(d.handle))continue;
   p=Marshal.AllocHGlobal(32);uint bytes=32;
   try {
    Marshal.WriteInt32(p,32);if(GetRawInputDeviceInfo(d.handle,0x2000000b,p,ref bytes)==uint.MaxValue)continue;
    ushort page=(ushort)Marshal.ReadInt16(p,20),usage=(ushort)Marshal.ReadInt16(p,22);uint key=((uint)page<<16)|usage;
    if(!registered.Contains(key)) {
     if(!RegisterRawInputDevices(new[]{new Registration{page=page,usage=usage,flags=0x100|0x2000|(usage==0?0x20u:0u),target=Handle}},1,(uint)Marshal.SizeOf(typeof(Registration)))){Log("Raw Input registration error="+Marshal.GetLastWin32Error());continue;}
     registered.Add(key);
    }
    controllers[d.handle]=new Controller{Path=path};Log("controller connected "+path);
   }finally{Marshal.FreeHGlobal(p);}
  }
  var gone=new List<IntPtr>();foreach(var handle in controllers.Keys)if(!seen.Contains(handle))gone.Add(handle);
  foreach(var handle in gone){controllers.Remove(handle);Log("controller disconnected");}
 }
 long retryTickAt;
 void SafeTick() {
  if(exitSignal.WaitOne(0)){Close();return;}
  if(clock.ElapsedMilliseconds<retryTickAt)return;
  try{Tick();}catch(Exception ex){if(ableton!=null)ableton.Pause(false);retryTickAt=clock.ElapsedMilliseconds+1000;foreach(var c in controllers.Values){c.Vegas.Ready(false,clock.ElapsedMilliseconds);c.Action=0;c.Hold.CancelUntilRelease();}Log("monitoring temporarily unavailable: "+ex.Message);}
 }
 void Tick() {
  if(stopping || suspended)return;long now=clock.ElapsedMilliseconds;
  if(now-lastDesktopCheck>=500){lastDesktopCheck=now;SetSessionLocked(SessionState.ShouldBlock(sessionReportedLocked,sessionDisconnected,SessionState.InputAvailable()));}
  if(sessionLocked){UpdateStatus(now);return;}
  if(pause.Until.HasValue && !pause.Active(DateTimeOffset.UtcNow)){pause.Resume();manualPauseItem.Visible=true;if(ableton!=null)ableton.Resume();resumeItem.Enabled=false;pauseItem.Visible=true;lastProcessCheck=-1000;foreach(var c in controllers.Values){c.Action=0;c.Hold.CancelUntilRelease();c.LedAttempted=false;}Log("timed pause expired");}
  if(now-lastScan>=1000){lastScan=now;Scan();}
  if(now-lastProcessCheck>=500){lastProcessCheck=now;var snapshot=Processes.Observe(settings);runningNow=snapshot.Running;abletonHost=snapshot.AbletonRunning;nativeBlockedNow=snapshot.NativeOwner;dawDetails=snapshot.DawDetails;dawBlockedNow=dawDetails.Length>0;ledBlockedNow=snapshot.LedBlocked;blockDetails=dawBlockedNow?dawDetails:snapshot.RunningDetails.Length>0?snapshot.RunningDetails:snapshot.NativeDetails;}
  if(vegasItem.Checked && (runningNow || abletonHost)){vegasItem.Checked=false;Log("manual Vegas mode reset: application started");}
  foreach(var c in controllers.Values)c.Vegas.Ready(vegasItem.Checked && settings.StandbyLed && !pause.Active(DateTimeOffset.UtcNow) && !abletonHost && !runningNow && !ledBlockedNow && now>=launchUntil && !c.Hold.IsDown && !c.Vegas.InputHeld && !c.MarkerSet,now);
  UpdateContrast(now,!pause.Active(DateTimeOffset.UtcNow) && !nativeBlockedNow && !runningNow && now>=launchUntil && (abletonHost?ableton!=null && ableton.OwnsController:!ledBlockedNow));
  if(ableton!=null){
   string controllerPath=null;if(!settings.EnableNativeHandoff || controllers.Count==1)foreach(var c in controllers.Values){controllerPath=c.Path;break;}
   if(abletonHost)foreach(var c in controllers.Values)if(c.LedSet){
    if(!nativeBlockedNow && Processes.DawHandoffAllowed(settings.DawProcesses) && !StandbyLight.IsBusy)try{StandbyLight.Set(c.Path,0);}catch(Exception ex){Log("Ableton standby handoff: "+ex.Message);}
    c.LedSet=false;
   }
   ableton.Tick(controllerPath,abletonHost,pause.Active(DateTimeOffset.UtcNow) || nativeBlockedNow);
   if(abletonHost){foreach(var c in controllers.Values){c.Action=0;c.Hold.CancelUntilRelease();c.LedSet=false;c.LedAttempted=false;}FinishFocus();UpdateStatus(now);return;}
   if(ableton.IsWriting || StandbyLight.IsBusy){UpdateStatus(now);return;}
  }
  if(pause.Active(DateTimeOffset.UtcNow)){UpdateStatus(now);return;}
  bool running=runningNow;
  if(wasRunning && !running)foreach(var c in controllers.Values){c.LedAttempted=false;}
  if(!wasRunning && running)foreach(var c in controllers.Values){c.LedSet=false;}
  wasRunning=running;
  if(wasLedBlocked && !ledBlockedNow)foreach(var c in controllers.Values)c.LedAttempted=false;
  if(!wasLedBlocked && ledBlockedNow) {
   bool canClear=dawBlockedNow && !Processes.LedBlocked(settings.Executable) && Processes.DawHandoffAllowed(settings.DawProcesses);
   foreach(var c in controllers.Values) {
    if(c.LedSet && canClear) {
     try {
      if(!Processes.LedBlocked(settings.Executable) && Processes.DawHandoffAllowed(settings.DawProcesses)) {
       StandbyLight.Set(c.Path,0);Log("standby LED cleared for DAW handoff");
      }
     }catch(Exception ex){Log("DAW LED handoff unavailable: "+ex.Message);}
    } else if(c.LedSet && dawBlockedNow)Log("DAW LED handoff skipped: host already active, plug-in loaded, or inspection unavailable");
    c.LedSet=false;
   }
  }
  wasLedBlocked=ledBlockedNow;
  if(dawBlockedNow)FinishFocus();
  foreach(var c in controllers.Values) {
   if(dawBlockedNow)c.Action=0;
   bool previouslyFired=c.Hold.IsFired;
   if(c.Action==2 && !WindowClose.ValidMain(settings.Executable,c.CloseWindow,c.ClosePid))c.Action=0;
   bool trigger=c.Hold.Tick(now,c.Action==2?settings.CloseHoldMs:settings.HoldMs,c.Action==0 || (c.Action==1 && (running || now<launchUntil)));
   if(trigger && c.Action==1)Launch(c);
   else if(trigger && c.Action==2){FinishFocus();bool requested=Processes.DawBlockers(settings).Length==0 && WindowClose.Request(settings.Executable,c.CloseWindow,c.ClosePid);Log("normal close requested="+requested+" pid="+c.ClosePid+" hwnd="+c.CloseWindow);}
   else if(!previouslyFired && c.Hold.IsFired && !trigger)Log("hold ignored: no eligible action, focus changed, running program or launch cooldown");
   if(!settings.StandbyLed || now<launchUntil || ledBlockedNow)continue;
   if(!c.LedAttempted){
    c.LedAttempted=true;
    try{if(LedOutputBlocked()){c.LedAttempted=false;continue;}StandbyLight.Set(c.Path,settings.StandbyBrightnessValue);c.LedSet=true;Log("standby LED "+settings.StandbyBrightness+" report sent");}catch(Exception ex){Log("standby LED unavailable: "+ex.Message);}
   }
   if(c.LedSet){var frame=c.Vegas.Frame(now,settings.StandbyBrightnessValue,vegasItem.Checked);if(frame!=null)try{if(!LedOutputBlocked())StandbyLight.Frame(c.Path,frame);else c.Vegas.Ready(false,now);}catch(Exception ex){c.Vegas.Ready(false,now);c.LedAttempted=false;Log("standby animation unavailable: "+ex.Message);}}
  }
  FocusLaunchedWindow(now);
  UpdateStatus(now);
 }
 bool LedOutputBlocked(){return sessionLocked || suspended || pause.Active(DateTimeOffset.UtcNow) || NativeLightBlocked();}
 bool NativeLightBlocked(){return Processes.LedBlocked(settings.Executable) || Processes.DawBlockers(settings).Length>0;}
 void ClearLaunchMarker(){
  foreach(var c in controllers.Values)if(c.MarkerSet) {
   try{if(!NativeLightBlocked()){StandbyLight.Marker(c.Path,false,delegate{return !NativeLightBlocked();});Log("launch marker cleared");}}
   catch(Exception ex){Log("launch marker cleanup unavailable: "+ex.Message);}c.MarkerSet=false;
  }
 }
 void ShowLaunchMarker(Controller c){c.MarkerSet=true;try{StandbyLight.Marker(c.Path,true,delegate{return !LedOutputBlocked();});Log("press marker shown");}catch(Exception ex){Log("press marker unavailable: "+ex.Message);}}
 void Launch(Controller pressedController) {
  if(sessionLocked || suspended || pause.Active(DateTimeOffset.UtcNow))return;
  if(StandbyLight.IsBusy){Log("launch skipped: HID write still pending");return;}
  if(Processes.Running(settings.Executable) || Processes.DawBlockers(settings).Length>0)return;
  launchUntil=clock.ElapsedMilliseconds+15000;
  try {
   foreach(var c in controllers.Values)if(c.LedSet && !LedOutputBlocked()){try{StandbyLight.Set(c.Path,0);c.LedSet=false;}catch(Exception ex){Log("LED handoff: "+ex.Message);}}
   if(sessionLocked || suspended || pause.Active(DateTimeOffset.UtcNow))return;
  if(StandbyLight.IsBusy){Log("launch skipped: LED handoff remains pending");return;}
   ClearLaunchMarker();
   if(StandbyLight.IsBusy || LedOutputBlocked()){ClearLaunchMarker();Log("launch cancelled before process start");return;}
   long elapsed=clock.ElapsedMilliseconds-pressedController.PressedAt;Log("launch command: press-to-command ms="+elapsed+" hold threshold ms="+settings.HoldMs+" overhead ms="+Math.Max(0,elapsed-settings.HoldMs));
   var process=Process.Start(new ProcessStartInfo{FileName=settings.Executable,Arguments=settings.Arguments,WorkingDirectory=Path.GetDirectoryName(settings.Executable),UseShellExecute=false,WindowStyle=ProcessWindowStyle.Normal});
   Log("launch requested pid="+(process==null?"unknown":process.Id.ToString()));
   if(process==null)throw new Exception("Launch did not return an application process");
   if(process!=null) {
    FinishFocus();launchedProcess=process;focusDeadline=clock.ElapsedMilliseconds+30000;lastFocusCheck=-1000;focusWindow=IntPtr.Zero;focusAttempts=0;
    Log("foreground permission granted="+WindowFocus.AllowSetForegroundWindow((uint)process.Id));
   }
  }catch(Exception ex){ClearLaunchMarker();Log("launch failed: "+ex.Message);tray.ShowBalloonTip(5000,"Launch failed",ex.Message,ToolTipIcon.Error);}
 }
 void FinishFocus(){if(launchedProcess!=null){launchedProcess.Dispose();launchedProcess=null;}focusWindow=IntPtr.Zero;}
 void FocusLaunchedWindow(long now) {
  if(launchedProcess==null || now-lastFocusCheck<500)return;lastFocusCheck=now;
  try {
   if(launchedProcess.HasExited){Log("foreground wait: launched process exited");FinishFocus();ClearLaunchMarker();return;}
   if(now>=focusDeadline){Log("foreground wait timed out");FinishFocus();return;}
   launchedProcess.Refresh();IntPtr hwnd=launchedProcess.MainWindowHandle;
   bool maschine=launchedProcess.ProcessName.StartsWith("Maschine",StringComparison.OrdinalIgnoreCase);
   if(!WindowFocus.Ready(hwnd,(uint)launchedProcess.Id,maschine)){focusWindow=IntPtr.Zero;return;}
   foreach(var c in controllers.Values)c.MarkerSet=false; // native main window owns the screen now
   if(hwnd!=focusWindow){focusWindow=hwnd;return;}
   bool activated=WindowFocus.Activate(hwnd,(uint)launchedProcess.Id);focusAttempts++;
   Log("foreground attempt="+focusAttempts+" success="+activated+" hwnd="+hwnd);
   if(activated || focusAttempts>=3)FinishFocus();
  }catch(Exception ex){Log("foreground error: "+ex.Message);FinishFocus();}
 }
 void SetSessionLocked(bool locked){
  if(locked==sessionLocked)return;
  sessionLocked=locked;FinishFocus();if(locked && ableton!=null)ableton.Pause(true);
  foreach(var c in controllers.Values){c.Vegas.Ready(false,clock.ElapsedMilliseconds);c.Action=0;c.Hold.CancelUntilRelease();
   if(locked){try{if(c.LedSet && !NativeLightBlocked())StandbyLight.Set(c.Path,0);}catch(Exception ex){Log("lock LED cleanup: "+ex.Message);}c.LedSet=false;}
   else c.LedAttempted=false;
  }
  if(locked)ClearLaunchMarker();else{if(ableton!=null)ableton.ResetState();lastScan=-1000;lastProcessCheck=-1000;retryTickAt=0;}
  Log(locked?"session locked or desktop unavailable":"session unlocked: standby refresh scheduled");UpdateStatus(clock.ElapsedMilliseconds);
 }
 void SleepDetected(){if(ableton!=null)ableton.Pause(true);suspended=true;FinishFocus();foreach(var c in controllers.Values){if(c.Vegas.Animating && c.LedSet && !NativeLightBlocked())try{StandbyLight.Set(c.Path,0);c.LedSet=false;}catch(Exception ex){Log("suspend animation cleanup: "+ex.Message);}c.Vegas.Ready(false,clock.ElapsedMilliseconds);c.Action=0;c.Hold.CancelUntilRelease();}Log("system suspend");UpdateStatus(clock.ElapsedMilliseconds);}
 void WakeDetected(){
  long now=clock.ElapsedMilliseconds;if(!suspended && now-lastWake<2000)return;
  suspended=false;if(ableton!=null)ableton.ResetState();lastWake=now;lastScan=-1000;lastProcessCheck=-1000;retryTickAt=0;launchUntil=0;FinishFocus();
  foreach(var c in controllers.Values){c.Vegas.Ready(false,now);c.Action=0;c.Hold.CancelUntilRelease();c.LedSet=false;c.LedAttempted=false;c.ContrastDim=null;c.ContrastRetryAt=0;}
  Log("system resume: controller inventory and standby state refreshed");SafeTick();
 }
 protected override void WndProc(ref Message m) {
  try {
  if(m.Msg==0x2b1){int change=m.WParam.ToInt32();
   if(change==7)sessionReportedLocked=true;else if(change==8)sessionReportedLocked=false;
   if(change==2 || change==4)sessionDisconnected=true;else if(change==1 || change==3)sessionDisconnected=false;
   if(change==7 || change==8 || change==1 || change==2 || change==3 || change==4)SetSessionLocked(SessionState.ShouldBlock(sessionReportedLocked,sessionDisconnected,SessionState.InputAvailable()));
  }
  if(m.Msg==0x218){int power=m.WParam.ToInt32();if(power==4)SleepDetected();else if(power==7 || power==18)WakeDetected();}
  if(m.Msg==0xfe){Scan();}
  if(m.Msg==0xff) {
   uint bytes=0,header=(uint)(8+2*IntPtr.Size);GetRawInputData(m.LParam,0x10000003,IntPtr.Zero,ref bytes,header);
   if(bytes>=header+8 && bytes<=1048576) {
    IntPtr p=Marshal.AllocHGlobal((int)bytes);
    try {
     if(GetRawInputData(m.LParam,0x10000003,p,ref bytes,header)!=uint.MaxValue && Marshal.ReadInt32(p)==2) {
      Controller c;if(controllers.TryGetValue(Marshal.ReadIntPtr(p,8),out c)) {
       int length=Marshal.ReadInt32(p,(int)header),count=Marshal.ReadInt32(p,(int)header+4);
       if(length>=8 && count>0 && (long)length*count<=bytes-header-8)for(int i=0;i<count;i++) {
        int start=(int)header+8+i*length;
        var report=new byte[length];Marshal.Copy(IntPtr.Add(p,start),report,0,length);bool animated=c.Vegas.Animating;
        if(c.Vegas.Input(report,clock.ElapsedMilliseconds) && animated)c.LedAttempted=false;
        if(ableton!=null && ableton.Feed(c.Path,report))continue;
        if(Marshal.ReadByte(p,start)!=1)continue;
        bool pressed=(Marshal.ReadByte(p,start+1)&1)!=0;if(pressed!=c.Hold.IsDown) {
 Log("Maschine button "+(pressed?"down":"up"));
 if(pressed) {
  c.PressedAt=clock.ElapsedMilliseconds;c.Hold.Input(true,c.PressedAt);
  c.Action=0;
  if(sessionLocked || suspended || pause.Active(DateTimeOffset.UtcNow) || Processes.DawBlockers(settings).Length>0)c.Action=0;
  else if(!Processes.Running(settings.Executable))c.Action=1;
  else if(settings.EnableClose && WindowClose.FindTarget(settings.Executable,out c.CloseWindow,out c.ClosePid))c.Action=2;
  Log("hold action="+(c.Action==1?"launch":c.Action==2?"close":"none"));
  if(c.Action==1)ShowLaunchMarker(c);
 }
}
c.Hold.Input(pressed,clock.ElapsedMilliseconds);if(!pressed){c.Action=0;ClearLaunchMarker();}
       }
      }
     }
    }finally{Marshal.FreeHGlobal(p);}
   }
  }
  }catch(Exception ex){foreach(var c in controllers.Values){c.Action=0;c.Hold.CancelUntilRelease();}Log("input monitoring error: "+ex.Message);}
  base.WndProc(ref m);
 }
 protected override void OnFormClosed(FormClosedEventArgs e) {
  stopping=true;if(ableton!=null){try{ableton.Dispose();}catch(Exception ex){Log("Ableton cleanup: "+ex.Message);}}if(sessionNotifications)SessionState.WTSUnRegisterSessionNotification(Handle);timer.Stop();timer.Dispose();FinishFocus();
  foreach(var c in controllers.Values){try{if(c.LedSet && !LedOutputBlocked())StandbyLight.Set(c.Path,0);}catch(Exception ex){Log("LED cleanup: "+ex.Message);}}
  tray.Visible=false;tray.ContextMenuStrip.Dispose();tray.Dispose();Log("stopped");try{log.Dispose();}catch(IOException){}exitSignal.Dispose();base.OnFormClosed(e);
 }
 [STAThread] static int Main(string[] args) {
  if(args.Length==2 && args[0]=="--ni-client"){int niPid;if(!int.TryParse(args[1],out niPid) || niPid<=0)return 2;return NiClientHost.Run(niPid);}
  if(args.Length==1 && args[0]=="--exit"){try{using(var signal=EventWaitHandle.OpenExisting("Local\\MaschineCompanionExit"))signal.Set();return 0;}catch(WaitHandleCannotBeOpenedException){return 1;}}
  if(args.Length==1 && args[0]=="--ni-self-test"){NiClientHost.TestTransport();return 0;}
  if(args.Length>0 && args[0]=="--self-test"){Tests.Run();return 0;}
  bool created;using(var mutex=new Mutex(true,"Local\\MaschineCompanionLauncher",out created)) {
   if(!created)return 0;
   try{
    FreeConsole();Application.EnableVisualStyles();var app=new Companion(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..")));
    if(args.Length>0 && args[0]=="--smoke"){var stop=new System.Windows.Forms.Timer{Interval=3000};stop.Tick+=(s,e)=>{stop.Stop();stop.Dispose();app.Close();};stop.Start();}
    Application.Run(app);return 0;
   }
   catch(Exception ex){MessageBox.Show(ex.Message,"Maschine Companion",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
   finally{mutex.ReleaseMutex();}
  }
 }
}
static class Tests {
 static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL "+name);}
 public static void Run() {
  {
   DeviceState device;
   Check(DeviceState.TryParse("MDEV1 1 1 1 0 1 1 0 RmlsdGVy Q3V0b2Zm Mi40MCBrSHo= ready".Split(' '),out device) && device.Name=="Filter" && device.Value=="2.40 kHz" && device.Previous && !device.Next,"device feedback parses formatted labels and separate navigation");
   Check(!DeviceState.TryParse("MDEV1 1 0 1 0 1 1 0 - - - ready".Split(' '),out device),"unavailable device cannot be enabled");
   Check(!DeviceState.TryParse("MDEV1 1 1 1 0 1 1 0 !!! - - ready".Split(' '),out device),"malformed device label is rejected");
   Check(DeviceState.TryParse("MDEV2 1 1 1 0 1 1 0 - - - 0 ready".Split(' '),out device) && device.Position==0,"device minimum strip position is available");
   Check(DeviceState.TryParse("MDEV2 1 1 1 0 1 1 0 - - - 127 ready".Split(' '),out device) && device.Position==127,"device maximum strip position parses");
   Check(!DeviceState.TryParse("MDEV2 1 1 1 0 1 1 0 - - - 128 ready".Split(' '),out device),"out of range device strip state rejected");
   NavigationState navigation;
   Check(NavigationState.TryParse("MNAV1 2 1 1 VmVyc2U= ready".Split(' '),out navigation) && navigation.Page==1 && navigation.Label=="Verse","navigation feedback parses page and label");
   Check(!NavigationState.TryParse("MNAV1 2 1 2 VmVyc2U= ready".Split(' '),out navigation),"navigation rejects invalid pages");
   Check(!NavigationState.TryParse("MNAV1 2 1 0 AQ== ready".Split(' '),out navigation),"navigation rejects screen control characters");
  }
  {
   string config=Path.GetTempFileName();
   try{
    string executable=Process.GetCurrentProcess().MainModule.FileName;
    File.WriteAllLines(config,new[]{"# Keep this comment","Executable="+executable,"StandbyBrightness=Bright"});
    Check(!Settings.Load(config).DisplayDim,"display defaults to normal");
    DisplayContrast.Save(config,true);Check(Settings.Load(config).DisplayDim && File.ReadAllText(config).Contains("# Keep this comment") && Settings.Load(config).StandbyBrightness=="Bright","Dim persists without replacing other settings");
    DisplayContrast.Save(config,false);Check(!Settings.Load(config).DisplayDim,"Dim can be disabled and reloaded");
   }finally{File.Delete(config);}
   byte[] dim=DisplayContrast.Report(true,33),normal=DisplayContrast.Report(false,33);
   Check(dim[9]==0 && normal[9]==100 && dim[0]==0xf8 && dim[1]==0x80 && dim[3]==0x20 && dim[5]==1,"contrast uses observed minimum and normal values");
   for(int i=0;i<dim.Length;i++)if(i!=9)Check(dim[i]==normal[i],"Dim changes only the contrast parameter");
  }
  Check(LiveBrowserKeys.JumpTarget(4,20,6)==10 && LiveBrowserKeys.JumpTarget(10,20,-6)==4
   && LiveBrowserKeys.JumpTarget(18,20,6)==19 && LiveBrowserKeys.JumpTarget(2,20,-6)==0
   && LiveBrowserKeys.JumpTarget(-1,0,6)==-1,"browser jumps six rows directly and clamps at list boundaries");
  {
   long now=0;int reads=0;
   var result=LiveBrowserKeys.WaitForChildren(()=>{reads++;return now>=325;},()=>true,()=>now,delay=>now+=delay);
   Check(result==LiveBrowserKeys.ChildProbe.Populated && now==325,"browser waits for delayed preset rows and finishes early");
   now=0;result=LiveBrowserKeys.WaitForChildren(()=>false,()=>true,()=>now,delay=>now+=delay);
   Check(result==LiveBrowserKeys.ChildProbe.Empty && now==500,"empty plugin branch waits at most 500 ms");
   now=0;result=LiveBrowserKeys.WaitForChildren(()=>false,()=>now<100,()=>now,delay=>now+=delay);
   Check(result==LiveBrowserKeys.ChildProbe.Cancelled && now==100,"browser cancels pending load when focus or selection changes");
   now=0;result=LiveBrowserKeys.WaitForChildren(()=>true,()=>true,()=>now,delay=>now+=delay);
   Check(result==LiveBrowserKeys.ChildProbe.Populated && now==0,"existing presets need no extra wait");
   Check(LiveBrowserKeys.EmptyPluginCanLoad("Plug-Ins List, 53 Items",1)
    && !LiveBrowserKeys.EmptyPluginCanLoad("Plug-Ins List, 53 Items",0)
    && !LiveBrowserKeys.EmptyPluginCanLoad("Plug-Ins List, 53 Items",2)
    && !LiveBrowserKeys.EmptyPluginCanLoad("User Library List",1),"empty branch loading is limited to grouped plugin devices, excluding creators, preset descendants and Places");
  }
  {
   var vegas=new StandbyVegas((min,max)=>min);vegas.Ready(true,0);
   Check(vegas.Frame(0,124,true)[2]==127,"manual Vegas starts immediately");
   vegas.Ready(false,100);Check(vegas.Frame(100,124,true)==null,"manual Vegas respects ownership and pause");
   vegas.Ready(true,100);var held=new byte[64];held[0]=1;held[1]=2;vegas.Input(held,101);
   Check(vegas.Frame(101,124,true)==null,"manual Vegas respects held controls");
   held[1]=0;vegas.Input(held,102);Check(vegas.Frame(102,124,true)[2]==127,"manual Vegas resumes after release");
   vegas.Ready(false,200);vegas.Ready(true,200);
   Check(vegas.Frame(180200,124)==null && vegas.Frame(3600200,124)==null,"unchecked Vegas stays off regardless of idle time");
   Check(vegas.Frame(3600200,124,true)[2]==127,"checking Vegas starts immediately after any idle duration");
  }
  {
   var vegas=new StandbyVegas((min,max)=>min);vegas.Ready(true,0);
   var idle=new byte[64];idle[0]=1;
   Check(!vegas.Input(idle,1000) && !vegas.Input(idle,179000),"unchanged idle reports do not interrupt Vegas");
   Check(vegas.Frame(180000,124)==null,"Vegas never starts from inactivity alone");
   var frame=vegas.Frame(180000,124,true);Check(frame[1]==124 && frame[2]==127,"Vegas keeps Maschine steady and starts a random button at maximum");
   Check(vegas.Frame(180499,124,true)==null && vegas.Frame(180500,124,true)[2]==126,"Vegas button fade waits half a second per level");
   Check(vegas.Frame(181000,124,true)[2]==124 && vegas.Frame(181500,124,true)[2]==0,"Vegas button fades through Dim to Off");
   Check(vegas.Frame(183499,124,true)==null && vegas.Frame(183500,124,true)[2]==127,"Vegas waits at least two seconds after fading out");
   idle[7]=1;Check(vegas.Input(idle,183600) && !vegas.Animating,"encoder activity cancels current Vegas light");
   idle[1]=2;vegas.Input(idle,363600);Check(vegas.Frame(999999,124,true)==null,"a held button prevents animation");
   idle[1]=0;vegas.Input(idle,1000000);Check(vegas.Frame(1000000,124,true)[2]==127,"checked Vegas resumes on release without an idle timer");
   vegas.Ready(false,1180000);Check(vegas.Frame(2000000,124,true)==null,"Vegas never runs outside eligible standby");
  }
  {
   var vegas=new StandbyVegas((min,max)=>max-1);vegas.Ready(true,0);
   Check(vegas.Frame(180000,126,true)[55]==71 && vegas.Frame(180500,126,true)[55]==70 && vegas.Frame(181000,126,true)[55]==69 && vegas.Frame(181500,126,true)[55]==68,"Vegas pads use four brightness levels with one color");
   Check(vegas.Frame(182000,126,true)[55]==0 && vegas.Frame(188999,126,true)==null && vegas.Frame(189000,126,true)[55]==71,"Vegas random pause includes seven seconds");
   var pad=new byte[64];pad[0]=2;pad[1]=12;pad[2]=0x16;pad[3]=64;
   Check(vegas.Input(pad,189001) && vegas.InputHeld && !vegas.Animating,"pad input stops Vegas and detects held pads");
   pad[2]=0x30;pad[3]=0;vegas.Input(pad,189002);Check(!vegas.InputHeld,"pad release clears Vegas activity hold");
   var strip=new byte[64];strip[0]=1;strip[10]=80;Check(vegas.Input(strip,189003) && vegas.InputHeld,"strip touch stops Vegas even though strip LEDs are excluded");
  }
  for(int target=2;target<=55;target++){
   int selected=target;var vegas=new StandbyVegas((min,max)=>min==2?selected:min);vegas.Ready(true,0);
   var frame=vegas.Frame(180000,127,true);for(int led=1;led<81;led++)Check(led==1?frame[led]==127:led==selected?frame[led]!=0:frame[led]==0,"Vegas animates exactly one eligible LED and never the strip");
  }
  var indefinitePause=new TimedPause();indefinitePause.StartIndefinite();Check(indefinitePause.Active(DateTimeOffset.UtcNow.AddDays(2)),"manual pause has no automatic expiry");indefinitePause.Resume();Check(!indefinitePause.Active(DateTimeOffset.UtcNow),"manual pause resumes explicitly");
  using(var release=new ManualResetEvent(false)) {
   bool timeout=false,overlapRejected=false;
   try {
    try{StandbyLight.RunWrite(delegate{release.WaitOne();});}catch(Exception){timeout=true;}
    Check(timeout && StandbyLight.IsBusy,"HID timeout returns without losing in-flight ownership");
    try{StandbyLight.RunWrite(delegate{throw new Exception("should not run");});}catch(Exception){overlapRejected=true;}
    Check(overlapRejected,"reject overlapping HID writes");
   }finally{release.Set();Check(SpinWait.SpinUntil(delegate{return !StandbyLight.IsBusy;},2000),"HID worker completes safely");}
  }
  Check(StartupRegistration.Command(@"C:\Program Files\Maschine Companion\app.exe")=="\""+@"C:\Program Files\Maschine Companion\app.exe"+"\"","startup path is quoted");
  foreach(bool bottom in new[]{false,true}) {
   var bar=LaunchScreen.Report(true,bottom);var blank=LaunchScreen.Report(false,bottom);Check(bar.Length==265 && bar[0]==0xe0 && bar[3]==(bottom?2:0),"OLED report header");
   int lit=0;
   for(int y=0;y<16;y++)for(int x=0;x<128;x++) {
    bool on=(bar[9+(y/8)*128+x] & (1<<(y%8)))==0;int globalY=y+(bottom?16:0);
    if(on){lit++;Check(x<8 && globalY==13,"underscore stays at left edge in first row");}
   }
   Check(lit==(bottom?0:8),"8-pixel first-row underscore only");
   for(int i=9;i<265;i++)Check(blank[i]==0xff,"blank OLED pixels");
  }
  Check(SessionState.ShouldBlock(true,false,true),"lock notification wins over available desktop");
  Check(SessionState.ShouldBlock(false,true,true),"disconnected session stays blocked");
  Check(SessionState.ShouldBlock(false,false,false),"secure desktop blocks actions");
  Check(!SessionState.ShouldBlock(false,false,true),"unlock plus normal desktop permits actions");
  var timed=new TimedPause();var instant=new DateTimeOffset(2026,10,11,12,0,0,TimeSpan.Zero);timed.Start(instant);Check(timed.Active(instant.AddMinutes(59)),"pause lasts one hour");Check(!timed.Active(instant.AddHours(1)),"pause expires at deadline");timed.Start(instant);timed.Resume();Check(!timed.Active(instant),"manual resume");
  var h=new HoldGate();h.Input(true,0);Check(!h.Tick(1199,1200,false),"short hold");Check(h.Tick(1200,1200,false),"threshold");Check(!h.Tick(2500,1200,false),"one launch per hold");
  h.Input(false,2600);h.Input(true,2700);Check(!h.Tick(3900,1200,true),"running block");Check(!h.Tick(4000,1200,false),"no delayed launch after blocked hold");
  h.Reset();Check(!h.Tick(10000,1200,false),"disconnect reset");h.Input(true,10001);h.Input(false,10100);Check(!h.Tick(12000,1200,false),"release cancels");
  h.Input(true,13000);h.Input(true,14000);Check(h.Tick(14200,1200,false),"duplicate report keeps hold");
  var reloaded=new HoldGate();reloaded.Input(true,0);reloaded.CancelUntilRelease();Check(!reloaded.Tick(5000,1200,false),"reload cancels current hold");reloaded.Input(false,5100);reloaded.Input(true,5200);Check(reloaded.Tick(6400,1200,false),"reload allows a fresh press");
  var closing=new HoldGate();closing.Input(true,0);Check(!closing.Tick(1999,2000,false),"close hold threshold");Check(closing.Tick(2000,2000,false),"close hold fires");Check(!closing.Tick(5000,2000,false),"close/launch cannot retrigger without release");
  closing.Input(false,5100);closing.Input(true,5200);Check(!closing.Tick(7200,2000,true),"cancel close when target is blocked");Check(!closing.Tick(7300,2000,false),"cancelled close does not resume");
  Check(!WindowClose.Request(@"C:\apps\target.exe",IntPtr.Zero,0),"invalid close target never accepted");
  Check(WindowClose.MatchesPath(@"C:\Apps\Target.exe",@"c:\apps\target.exe"),"close target path ignores case");
  Check(!WindowClose.MatchesPath(@"D:\Apps\Target.exe",@"C:\Apps\Target.exe"),"same process name at different path is not a close target");
  Check(!WindowClose.MatchesPath("Target.exe",@"C:\Apps\Target.exe"),"relative close target path rejected");
  IntPtr selectedWindow;uint selectedPid;
  var targets=new List<WindowClose.Candidate>{new WindowClose.Candidate(new IntPtr(11),101,true),new WindowClose.Candidate(new IntPtr(22),202,true)};
  Check(WindowClose.Select(targets,new IntPtr(22),true,out selectedWindow,out selectedPid) && selectedWindow==new IntPtr(22) && selectedPid==202,"active configured instance wins");
  Check(!WindowClose.Select(targets,new IntPtr(99),false,out selectedWindow,out selectedPid),"multiple background instances rejected");
  Check(WindowClose.Select(new List<WindowClose.Candidate>{targets[0]},new IntPtr(99),false,out selectedWindow,out selectedPid) && selectedPid==101,"single background instance selected");
  targets[1].Ready=false;
  Check(!WindowClose.Select(targets,new IntPtr(22),true,out selectedWindow,out selectedPid),"blocked active instance does not fall back to another");
  Check(!WindowClose.Select(new List<WindowClose.Candidate>{targets[0]},new IntPtr(33),true,out selectedWindow,out selectedPid),"target child window does not close background main window");
  Check(!WindowClose.Select(targets,new IntPtr(99),false,out selectedWindow,out selectedPid),"blocked second instance still makes background selection ambiguous");
  var originalSettings=new Settings();var changedSettings=new Settings();
  Check(Settings.RestartRequired(originalSettings,changedSettings)==null,"unchanged connection settings reload");
  changedSettings.EnableNativeHandoff=true;Check(Settings.RestartRequired(originalSettings,changedSettings)=="EnableNativeHandoff","handoff change requires restart before applying config");
  changedSettings.EnableNativeHandoff=false;changedSettings.MidiActivityInputs="Other input";Check(Settings.RestartRequired(originalSettings,changedSettings)=="MidiActivityInputs","activity input change requires restart");
  changedSettings.MidiActivityInputs="";changedSettings.EnableAbleton=true;Check(Settings.RestartRequired(originalSettings,changedSettings)=="EnableAbleton","integration change requires restart");
  changedSettings.EnableAbleton=false;changedSettings.FixedVelocity=80;changedSettings.DisplayDim=true;Check(Settings.RestartRequired(originalSettings,changedSettings)==null,"velocity and dim remain reloadable");
  Check(new Settings().StandbyBrightnessValue==0x7c,"default Dim");Check(Settings.BrightnessValue("normal")==0x7e,"Normal brightness");Check(Settings.BrightnessValue("Bright")==0x7f,"Bright brightness");
  bool invalidBrightness=false;try{Settings.BrightnessValue("100");}catch{invalidBrightness=true;}Check(invalidBrightness,"reject invalid brightness");
  Check(Processes.MatchesDaw("Ableton Live 12 Standard","Ableton Live*"),"Ableton prefix");Check(!Processes.MatchesDaw("Ableton Plugin Scanner","Ableton Live*"),"exclude helper");Check(Processes.MatchesDaw("REAPER","reaper"),"case-insensitive exact DAW");
  Check(Settings.ParseDaws(" Ableton Live*.exe ; reaper.exe ").Length==2,"DAW config parse");Check(Settings.ParseDaws("").Length==0,"disable DAW list");
  bool invalidRule=false;try{Settings.ParseDaws("*");}catch{invalidRule=true;}Check(invalidRule,"reject global wildcard");
  Check(Processes.DawBlockers(new[]{Process.GetCurrentProcess().ProcessName}).Length>0,"detect actual blocker process");
  foreach(string name in new[]{"FL64","FL64 (scaled)","reaper","Cubase14","Nuendo 13","Studio One","Bitwig Studio","ProTools","Cakewalk","Sonar"}) {
   bool matches=false;foreach(string rule in new Settings().DawProcesses)if(Processes.MatchesDaw(name,rule))matches=true;Check(matches,"default DAW coverage: "+name);
  }
  bool scannerBlocked=false;foreach(string rule in new Settings().DawProcesses)if(Processes.MatchesDaw("Ableton Plugin Scanner",rule) || Processes.MatchesDaw("FL Studio Installer",rule))scannerBlocked=true;Check(!scannerBlocked,"exclude helper and installer processes");
  var dawSetting=new Settings{DawProcesses=new[]{Process.GetCurrentProcess().ProcessName}};
  Check(dawSetting.PauseWhenDawRunning && Processes.DawBlockers(dawSetting).Length>0,"DAW pause enabled by default");
  Check(Processes.Observe(dawSetting).DawDetails.Length>0,"snapshot finds DAW blocker");
  dawSetting.PauseWhenDawRunning=false;Check(Processes.DawBlockers(dawSetting).Length==0,"DAW pause disabled preserves list");Check(Processes.Observe(dawSetting).DawDetails.Length==0,"snapshot honors disabled DAW pause");
  string temp=Path.GetTempFileName();try {File.WriteAllText(temp,"Executable="+Process.GetCurrentProcess().MainModule.FileName+"\nHoldMilliseconds=1200\nStandbyLed=false");var s=Settings.Load(temp);Check(s.HoldMs==1200 && !s.StandbyLed,"config");File.AppendAllText(temp,"\nPauseWhenDawRunning=false");Check(!Settings.Load(temp).PauseWhenDawRunning,"parse DAW pause switch");File.AppendAllText(temp,"\nFixedVelocity=99");Check(Settings.Load(temp).FixedVelocity==99,"parse fixed velocity");File.AppendAllText(temp,"\nFixedVelocity=128");bool badVelocity=false;try{Settings.Load(temp);}catch{badVelocity=true;}Check(badVelocity,"reject invalid velocity");File.AppendAllText(temp,"\nFixedVelocity=127");File.AppendAllText(temp,"\nHoldMilliseconds=1");bool rejected=false;try{Settings.Load(temp);}catch{rejected=true;}Check(rejected,"bad config");}finally{File.Delete(temp);}
  Console.WriteLine("PASS standby Vegas timing/input/LED exclusions, hold threshold, repeat, running guard, release, disconnect, duplicate reports, config validation");
 }
}
