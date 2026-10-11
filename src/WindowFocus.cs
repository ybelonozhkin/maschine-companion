using System;
using System.Text;
using System.Runtime.InteropServices;
static class WindowFocus {
 [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint pid);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint thread,uint other,bool attach);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd,int command);
 [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
 public static bool Ready(IntPtr hwnd,uint expectedPid,bool maschine) {
  if(hwnd==IntPtr.Zero || !IsWindowVisible(hwnd))return false;
  uint pid;GetWindowThreadProcessId(hwnd,out pid);if(pid!=expectedPid)return false;
  if(maschine){var name=new StringBuilder(256);GetClassName(hwnd,name,name.Capacity);if(!name.ToString().StartsWith("NINormalWindow",StringComparison.Ordinal))return false;}
  return true;
 }
 public static bool Activate(IntPtr hwnd,uint expectedPid) {
  uint pid;uint target=GetWindowThreadProcessId(hwnd,out pid);if(pid!=expectedPid)return false;
  if(IsIconic(hwnd))ShowWindowAsync(hwnd,9);
  if(GetForegroundWindow()==hwnd || SetForegroundWindow(hwnd))return true;
  uint unused;uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),out unused),current=GetCurrentThreadId();
  bool foregroundAttached=false,targetAttached=false;
  try {
   if(foreground!=0 && foreground!=current)foregroundAttached=AttachThreadInput(current,foreground,true);
   if(target!=0 && target!=current && target!=foreground)targetAttached=AttachThreadInput(current,target,true);
   BringWindowToTop(hwnd);SetForegroundWindow(hwnd);
   return GetForegroundWindow()==hwnd;
  } finally {
   if(targetAttached)AttachThreadInput(current,target,false);
   if(foregroundAttached)AttachThreadInput(current,foreground,false);
  }
 }
}
