using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
class HidReports {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle file,out IntPtr data);
 [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
 [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,IntPtr caps);
 [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type,IntPtr caps,ref ushort count,IntPtr data);
 [DllImport("hid.dll")] static extern int HidP_InitializeReportForID(int type,byte id,IntPtr data,byte[] report,uint length);
 static int Main(string[] args) {
  if(args.Length!=1){Console.WriteLine("Usage: HidReports.exe <HID device path>");return 1;}
  using(var file=CreateFile(args[0],0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
   if(file.IsInvalid)throw new Exception("Query handle error="+Marshal.GetLastWin32Error());
   IntPtr data;if(!HidD_GetPreparsedData(file,out data))throw new Exception("No preparsed descriptor");
   IntPtr caps=Marshal.AllocHGlobal(64);
   try {
    if(HidP_GetCaps(data,caps)!=0x110000)throw new Exception("GetCaps failed");
    for(int type=0;type<3;type++) {
     int length=(ushort)Marshal.ReadInt16(caps,4+type*2);string name=new[]{"Input","Output","Feature"}[type];
     ushort count=(ushort)Marshal.ReadInt16(caps,48+type*6);Console.WriteLine(name+" maxLength="+length+" buttonCaps="+(ushort)Marshal.ReadInt16(caps,46+type*6)+" valueCaps="+count);
     if(count>0){IntPtr values=Marshal.AllocHGlobal(count*72);try {
      int result=HidP_GetValueCaps(type,values,ref count,data);if(result!=0x110000)throw new Exception("ValueCaps failed="+result);
      for(int i=0;i<count;i++){IntPtr v=IntPtr.Add(values,i*72);Console.WriteLine("  ID=0x"+Marshal.ReadByte(v,2).ToString("X2")+" bits="+(ushort)Marshal.ReadInt16(v,18)+" count="+(ushort)Marshal.ReadInt16(v,20)+" usagePage=0x"+((ushort)Marshal.ReadInt16(v,0)).ToString("X4"));}
     }finally{Marshal.FreeHGlobal(values);}}
     // Value-cap report IDs are authoritative here; InitializeReportForID results are not used as an enumeration.
    }
   }finally{Marshal.FreeHGlobal(caps);HidD_FreePreparsedData(data);}
  }
  return 0;
 }
}
