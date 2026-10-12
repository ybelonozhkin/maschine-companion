using System;
using System.Text;
using System.Runtime.InteropServices;
static class SessionState {
 [DllImport("wtsapi32.dll",SetLastError=true)] public static extern bool WTSRegisterSessionNotification(IntPtr hwnd,uint flags);
 [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder name,uint length,out uint needed);
 public static bool ShouldBlock(bool reportedLocked,bool disconnected,bool inputAvailable){return reportedLocked || disconnected || !inputAvailable;}
 public static bool InputAvailable() {
  IntPtr desktop=OpenInputDesktop(0,false,1);if(desktop==IntPtr.Zero)return false;
  try{var name=new StringBuilder(256);uint needed;return GetUserObjectInformation(desktop,2,name,512,out needed) && name.ToString().Equals("Default",StringComparison.OrdinalIgnoreCase);}finally{CloseDesktop(desktop);}
 }
}
