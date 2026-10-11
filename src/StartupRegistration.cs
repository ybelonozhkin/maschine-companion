using System;
using Microsoft.Win32;
static class StartupRegistration {
 const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
 const string ValueName="MaschineCompanion";
 public static string Command(string executable){return "\""+executable+"\"";}
 public static bool Enabled(string executable){using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key!=null && string.Equals(key.GetValue(ValueName) as string,Command(executable),StringComparison.OrdinalIgnoreCase);}
 public static void Set(bool enabled,string executable) {
  using(var key=Registry.CurrentUser.CreateSubKey(RunKey)) {
   if(key==null)throw new Exception("Cannot open current-user Windows startup settings");
   if(enabled)key.SetValue(ValueName,Command(executable),RegistryValueKind.String);
   else if(string.Equals(key.GetValue(ValueName) as string,Command(executable),StringComparison.OrdinalIgnoreCase))key.DeleteValue(ValueName,false);
  }
 }
}
