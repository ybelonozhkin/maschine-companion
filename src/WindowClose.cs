using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
static class WindowClose {
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
 [DllImport("user32.dll",SetLastError=true)] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
 static bool MaschineName(string name){return name=="Maschine 3" || name=="Maschine3" || name=="Maschine 2" || name=="Maschine2";}
 internal sealed class Candidate {public IntPtr Window;public uint Pid;public bool Ready;public Candidate(IntPtr window,uint pid,bool ready){Window=window;Pid=pid;Ready=ready;}}
 public static bool MatchesPath(string actual,string configured) {
  try {return !string.IsNullOrWhiteSpace(actual) && !string.IsNullOrWhiteSpace(configured) && Path.IsPathRooted(actual) && Path.IsPathRooted(configured) && string.Equals(Path.GetFullPath(actual),Path.GetFullPath(configured),StringComparison.OrdinalIgnoreCase);}catch{return false;}
 }
 static bool ReadPath(Process process,out string path){path=null;try{path=process.MainModule.FileName;return true;}catch{return false;}}
 public static bool ValidMain(string executable,IntPtr hwnd,uint pid) {
  if(hwnd==IntPtr.Zero || pid==0)return false;
  try {using(var process=Process.GetProcessById((int)pid)) {
   process.Refresh();string path;
   return ReadPath(process,out path) && MatchesPath(path,executable) && process.MainWindowHandle==hwnd && WindowFocus.Ready(hwnd,pid,MaschineName(process.ProcessName)) && IsWindowEnabled(hwnd) && GetWindow(hwnd,4)==IntPtr.Zero;
  }}catch{return false;}
 }
 public static bool Target(string executable,out IntPtr hwnd,out uint pid) {
  hwnd=GetForegroundWindow();pid=0;if(hwnd==IntPtr.Zero)return false;
  GetWindowThreadProcessId(hwnd,out pid);return ValidMain(executable,hwnd,pid);
 }
 internal static bool Select(IList<Candidate> candidates,IntPtr foreground,bool foregroundIsTarget,out IntPtr hwnd,out uint pid) {
  hwnd=IntPtr.Zero;pid=0;
  foreach(var candidate in candidates)if(candidate.Window==foreground) {
   if(!candidate.Ready)return false;
   hwnd=candidate.Window;pid=candidate.Pid;return true;
  }
  if(foregroundIsTarget || candidates.Count!=1 || !candidates[0].Ready)return false;
  hwnd=candidates[0].Window;pid=candidates[0].Pid;return true;
 }
 public static bool FindTarget(string executable,out IntPtr hwnd,out uint pid) {
  if(Target(executable,out hwnd,out pid))return true;
  hwnd=IntPtr.Zero;pid=0;if(string.IsNullOrWhiteSpace(executable))return false;
  IntPtr foreground=GetForegroundWindow();uint foregroundPid=0;GetWindowThreadProcessId(foreground,out foregroundPid);
  string name=Path.GetFileNameWithoutExtension(executable);bool foregroundIsTarget=false;
  try {using(var process=Process.GetProcessById((int)foregroundPid)) {
   string path;foregroundIsTarget=ReadPath(process,out path)?MatchesPath(path,executable):string.Equals(process.ProcessName,name,StringComparison.OrdinalIgnoreCase);
  }}catch{}
  var candidates=new List<Candidate>();
  foreach(var process in Process.GetProcessesByName(name))using(process) {
   try {string path;IntPtr candidate=process.MainWindowHandle;uint candidatePid=(uint)process.Id;
    if(!ReadPath(process,out path) || !MatchesPath(path,executable) || candidate==IntPtr.Zero || !WindowFocus.Ready(candidate,candidatePid,MaschineName(process.ProcessName)) || GetWindow(candidate,4)!=IntPtr.Zero)continue;
    candidates.Add(new Candidate(candidate,candidatePid,ValidMain(executable,candidate,candidatePid)));
   }catch{}
  }
  return Select(candidates,foreground,foregroundIsTarget,out hwnd,out pid);
 }
 public static bool Request(string executable,IntPtr expectedWindow,uint expectedPid) {
  if(!ValidMain(executable,expectedWindow,expectedPid))return false;
  IntPtr selectedWindow;uint selectedPid;
  if(!FindTarget(executable,out selectedWindow,out selectedPid) || selectedWindow!=expectedWindow || selectedPid!=expectedPid)return false;
  if(!WindowFocus.Activate(expectedWindow,expectedPid))return false;
  IntPtr hwnd;uint pid;
  if(!Target(executable,out hwnd,out pid) || hwnd!=expectedWindow || pid!=expectedPid)return false;
  return PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero);
 }
}
