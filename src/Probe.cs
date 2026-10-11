using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;
using System.Windows.Automation;

class Decoder {
 byte[] previous;
 public List<string> Feed(byte[] r) {
  var result = new List<string>();
  if(r.Length < 8 || r[0]!=1) return result;
  if(previous!=null) {
   int[] bits={0,25,39}; string[] names={"Project","SHIFT","EncoderPress"};
   for(int i=0;i<bits.Length;i++) { int b=1+bits[i]/8, mask=1<<(bits[i]%8);
    if((r[b]&mask)!=(previous[b]&mask)) result.Add("candidate "+names[i]+((r[b]&mask)!=0?" down":" up")); }
   int delta=((r[7]&15)-(previous[7]&15)+16)%16;
   if(delta==8) result.Add("encoder ambiguous delta=8");
   else if(delta!=0) result.Add("candidate encoder delta="+(delta>8?delta-16:delta));
  }
  previous=(byte[])r.Clone(); return result;
 }
}
class Probe : Form {
 [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr hwnd,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out object accessible);
 [DllImport("oleacc.dll")] static extern int AccessibleChildren(Accessibility.IAccessible container,int start,int count,[Out,MarshalAs(UnmanagedType.LPArray,SizeParamIndex=2)] object[] children,out int obtained);
 static string ReadAccessibility(IntPtr hwnd) {
  try {
   Guid iid=new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");object root;
   int hr=AccessibleObjectFromWindow(hwnd,0xFFFFFFFC,ref iid,out root);
   if(hr<0 || root==null)return "HRESULT="+hr;
   var output=new StringBuilder();int budget=60;
   WalkAccessibility(root as Accessibility.IAccessible,0,output,ref budget);return output.ToString();
  } catch(Exception ex){return "error="+ex.GetType().Name+":"+ex.Message;}
 }
 static void WalkAccessibility(Accessibility.IAccessible node,int depth,StringBuilder output,ref int budget) {
  if(node==null || depth>6 || budget<=0)return;
  try {
   --budget;output.Append("[depth="+depth+" name="+node.get_accName(0)+" role="+node.get_accRole(0)+" state="+node.get_accState(0)+"]");
   int count=Math.Min(node.accChildCount,60);object[] children=new object[count];int obtained;
   if(count==0)return;
   int hr=AccessibleChildren(node,0,count,children,out obtained);
   if(hr<0){output.Append("[children-HRESULT="+hr+"]");return;}
   for(int i=0;i<obtained && budget>0;i++) {
    var nested=children[i] as Accessibility.IAccessible;
    if(nested!=null)WalkAccessibility(nested,depth+1,output,ref budget);
    else if(children[i] is int) {
     --budget;int id=(int)children[i];
     try{output.Append("[child="+id+" name="+node.get_accName(id)+" role="+node.get_accRole(id)+" state="+node.get_accState(id)+"]");}
     catch(Exception ex){output.Append("[child="+id+" error="+ex.GetType().Name+"]");}
    }
   }
  } catch(Exception ex){output.Append("[error="+ex.GetType().Name+"]");}
 }
 [StructLayout(LayoutKind.Sequential)] struct Device { public IntPtr handle; public uint type; }
 [StructLayout(LayoutKind.Sequential)] struct Registration { public ushort page,usage; public uint flags; public IntPtr target; }
 [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputDeviceList([Out] Device[] list,ref uint count,uint size);
 [DllImport("user32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,IntPtr data,ref uint size);
 [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(Registration[] devices,uint count,uint size);
 [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint header);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int length);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int length);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
 [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hwnd);
  delegate bool EnumWindowCallback(IntPtr hwnd,IntPtr parameter);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd,EnumWindowCallback callback,IntPtr parameter);
 [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 readonly Dictionary<IntPtr,Decoder> decoders=new Dictionary<IntPtr,Decoder>();
 readonly HashSet<uint> usages=new HashSet<uint>();
 StreamWriter log; Timer timer=new Timer(); string lastState=""; bool inventory;
 void Log(string s) { string line=DateTimeOffset.Now.ToString("o")+" "+s; log.WriteLine(line); Console.WriteLine(line); }
 public Probe(bool inventoryOnly) {
  inventory=inventoryOnly; Directory.CreateDirectory("logs");
  log=new StreamWriter(Path.Combine("logs",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".log")); log.AutoFlush=true;
  Text="Maschine passive diagnostic — close to stop"; Width=620; Height=140;
  Controls.Add(new Label { Dock=DockStyle.Fill,Text="Passive Raw Input probe. Reports go to console and logs.\nNo controller writes or keyboard injection.\nMappings are candidates until confirmed on your hardware." });
  Shown+=(s,e)=> { Enumerate(); if(inventory) Close(); };
  timer.Interval=2000; timer.Tick+=(s,e)=> { Enumerate(); InspectWindow(); }; if(!inventory) timer.Start();
  FormClosed+=(s,e)=> {timer.Stop();timer.Dispose();log.Dispose();};
 }
 void Enumerate() {
  uint count=0; uint size=(uint)Marshal.SizeOf(typeof(Device));
  if(GetRawInputDeviceList(null,ref count,size)==uint.MaxValue) {Log("enumeration error="+Marshal.GetLastWin32Error());return;}
  var devices=new Device[count]; if(GetRawInputDeviceList(devices,ref count,size)==uint.MaxValue) return;
  var seen=new HashSet<IntPtr>();
  foreach(var d in devices) {
   if(d.type!=2) continue;
   uint chars=0; GetRawInputDeviceInfo(d.handle,0x20000007,IntPtr.Zero,ref chars);
   IntPtr p=Marshal.AllocHGlobal((int)(chars+1)*2); string path;
   try { if(GetRawInputDeviceInfo(d.handle,0x20000007,p,ref chars)==uint.MaxValue)continue; path=Marshal.PtrToStringUni(p); } finally{Marshal.FreeHGlobal(p);}
   if(path==null || path.IndexOf("vid_17cc&pid_1700",StringComparison.OrdinalIgnoreCase)<0)continue;
   seen.Add(d.handle); if(decoders.ContainsKey(d.handle))continue;
   uint bytes=32; p=Marshal.AllocHGlobal(32);
   try {
    Marshal.WriteInt32(p,32);
    if(GetRawInputDeviceInfo(d.handle,0x2000000b,p,ref bytes)==uint.MaxValue){Log("device info error="+Marshal.GetLastWin32Error());continue;}
    ushort page=(ushort)Marshal.ReadInt16(p,20), usage=(ushort)Marshal.ReadInt16(p,22);
    Log("collection path="+path+" usagePage="+page.ToString("X4")+" usage="+usage.ToString("X4"));
    decoders[d.handle]=new Decoder(); uint key=((uint)page<<16)|usage;
    if(!inventory && usages.Add(key)) {
     bool ok=RegisterRawInputDevices(new[]{new Registration{page=page,usage=usage,flags=0x100|0x2000|(usage==0 ? 0x20u : 0u),target=Handle}},1,(uint)Marshal.SizeOf(typeof(Registration)));
     Log("register INPUTSINK="+ok+" error="+(ok?0:Marshal.GetLastWin32Error())); if(!ok)usages.Remove(key);
    }
   } finally{Marshal.FreeHGlobal(p);}
  }
  var gone=new List<IntPtr>(); foreach(var key in decoders.Keys)if(!seen.Contains(key))gone.Add(key);
  foreach(var key in gone){decoders.Remove(key);Log("device collection disconnected");}
  if(inventory)Log("matching Raw Input collections="+seen.Count);
 }
 protected override void WndProc(ref Message m) {
  if(m.Msg==0xff) {
   uint bytes=0, header=(uint)(8+2*IntPtr.Size);
   GetRawInputData(m.LParam,0x10000003,IntPtr.Zero,ref bytes,header);
   if(bytes>=header+8 && bytes<1048576) {
    IntPtr p=Marshal.AllocHGlobal((int)bytes);
    try { if(GetRawInputData(m.LParam,0x10000003,p,ref bytes,header)!=uint.MaxValue && Marshal.ReadInt32(p)==2) {
     IntPtr device=Marshal.ReadIntPtr(p,8); Decoder decoder;
     if(decoders.TryGetValue(device,out decoder)) {
      int length=Marshal.ReadInt32(p,(int)header), count=Marshal.ReadInt32(p,(int)header+4);
      if(length>0 && count>0 && (long)length*count<=bytes-header-8)for(int i=0;i<count;i++) {
       byte[] report=new byte[length];Marshal.Copy(IntPtr.Add(p,(int)header+8+i*length),report,0,length);
       Log("report device="+device+" hex="+BitConverter.ToString(report));
       foreach(string evt in decoder.Feed(report))Log(evt);
      }
     }
    }} finally{Marshal.FreeHGlobal(p);}
   }
  }
  // DefWindowProc handles required foreground WM_INPUT cleanup.
  base.WndProc(ref m);
 }
 void InspectWindow() {
  try {
   IntPtr hwnd=GetForegroundWindow();uint pid;GetWindowThreadProcessId(hwnd,out pid);
   using(var process=Process.GetProcessById((int)pid)) {
    string state="foreground="+process.ProcessName;
    if(process.ProcessName.Equals("Maschine 3",StringComparison.OrdinalIgnoreCase)) {
     var title=new StringBuilder(1024);var cls=new StringBuilder(256);
     GetWindowText(hwnd,title,title.Capacity);GetClassName(hwnd,cls,cls.Capacity);
     IntPtr owner=GetWindow(hwnd,4);uint ownerPid=0;if(owner!=IntPtr.Zero)GetWindowThreadProcessId(owner,out ownerPid);
     state+=" hwnd="+hwnd+" title="+title+" class="+cls+" pid="+pid+" owner="+owner+" ownerPid="+ownerPid+" ownerEnabled="+(owner==IntPtr.Zero?"n/a":IsWindowEnabled(owner).ToString());
     if(owner!=IntPtr.Zero && ownerPid==pid && !IsWindowEnabled(owner)) {
      int childCount=0;state+=" nativeChildren=";
      EnumChildWindows(hwnd,delegate(IntPtr child,IntPtr unused) {
       if(++childCount>60)return false;
       var childTitle=new StringBuilder(1024);var childClass=new StringBuilder(256);
       GetWindowText(child,childTitle,childTitle.Capacity);GetClassName(child,childClass,childClass.Capacity);
       uint childPid;GetWindowThreadProcessId(child,out childPid);
       state+="[hwnd="+child+" pid="+childPid+" class="+childClass+" id="+GetDlgCtrlID(child)+" text="+childTitle+" enabled="+IsWindowEnabled(child)+" visible="+IsWindowVisible(child)+" MSAA="+ReadAccessibility(child)+"]";
       return true;
      },IntPtr.Zero);
      state+=" childCount="+childCount+" MSAA="+ReadAccessibility(hwnd);
     }
     try {
      var element=AutomationElement.FromHandle(hwnd);object rawPattern;
      var pattern=element.TryGetCurrentPattern(WindowPattern.Pattern,out rawPattern)?rawPattern as WindowPattern:null;
      state+=" modal="+(pattern==null?"unknown":pattern.Current.IsModal.ToString());
      var nodes=element.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Text),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit)));
      state+=" elements=";for(int i=0;i<Math.Min(nodes.Count,40);i++) {
       var n=nodes[i];object invoke;
       state+="[type="+n.Current.ControlType.ProgrammaticName+" name="+n.Current.Name+" id="+n.Current.AutomationId+" enabled="+n.Current.IsEnabled+" focusable="+n.Current.IsKeyboardFocusable+" invoke="+n.TryGetCurrentPattern(InvokePattern.Pattern,out invoke)+"]";
      }
     } catch(Exception ex){state+=" UIA-error="+ex.GetType().Name+":"+ex.Message;}
    }
    if(state!=lastState){Log(state);lastState=state;}
   }
  } catch(Exception ex){string state="UIA unavailable: "+ex.GetType().Name;if(state!=lastState){Log(state);lastState=state;}}
 }
 [STAThread] static int Main(string[] args) {
  if(args.Length>0 && args[0]=="--self-test") {
   var d=new Decoder();byte[] r=new byte[8];r[0]=1;r[7]=15;
   if(d.Feed(r).Count!=0)throw new Exception("baseline");r[7]=0;r[1]=1;
   var events=d.Feed(r);if(events.Count!=2 || !events[1].EndsWith("=1"))throw new Exception("wrap/edge");
   if(d.Feed(r).Count!=0)throw new Exception("duplicate");r[7]=15;if(!d.Feed(r)[0].EndsWith("=-1"))throw new Exception("reverse");
   if(d.Feed(new byte[]{1}).Count!=0)throw new Exception("short report");
   Console.WriteLine("PASS baseline, edges, duplicate, wrap, reverse, short report");return 0;
  }
  Application.EnableVisualStyles();Application.Run(new Probe(args.Length>0 && args[0]=="--inventory"));return 0;
 }
}
