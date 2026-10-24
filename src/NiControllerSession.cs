using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// NI interface facts were checked in a separate local interoperability probe.
// This implementation uses its own process lifecycle and Windows transport.
// Protocol references: terminar/rebellion and RufusIbiza/Encdr; no library linked.
interface INiOwnership : IDisposable {
 bool Granted {get;} bool Busy {get;} string Status {get;}
 void Request(bool value,long revision);void Reset();
}
sealed class NiControllerSession : INiOwnership {
 readonly Action<string> log;readonly Func<bool> panelIdle;
 volatile bool wanted,granted,hadControl,failed,disposed;long revision=-1;Task worker;
 public bool Granted {get{return wanted && granted && !disposed;}}
 public bool Busy {get{return worker!=null && !worker.IsCompleted;}}
 public string Status {get{return failed?"NI connection failed: use Resume to retry":Granted?"NI controller acquired":wanted?"Waiting for NI controller ownership":Busy?"Releasing NI controller":"NI controller released";}}
 public NiControllerSession(Action<string> logger,Func<bool> idle){log=logger;panelIdle=idle;}
 public void Request(bool value,long trackRevision){
  if(disposed)return;
  // A changed track is a new explicit request, not a timer-based takeover.
  if(value && revision!=trackRevision && wanted && (failed || hadControl && !granted)){wanted=false;granted=false;failed=false;revision=trackRevision;return;}
  revision=trackRevision;
  if(!value){wanted=false;granted=false;failed=false;return;}
  if(!Busy && !failed){wanted=true;granted=hadControl=false;worker=Task.Factory.StartNew(Run,TaskCreationOptions.LongRunning);}
 }
 void Run(){
  Process child=null;
  try{
   var services=Process.GetProcessesByName("NIHostIntegrationAgent");int pid;
   try{if(services.Length!=1)throw new IOException("Expected one NIHostIntegrationAgent");pid=services[0].Id;}finally{foreach(var p in services)p.Dispose();}
   var start=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--ni-client "+pid){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
   child=new Process{StartInfo=start};
   child.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)log("NI helper: "+e.Data);};
   if(!child.Start())throw new IOException("Cannot start NI helper");child.BeginErrorReadLine();
   var reader=Task.Factory.StartNew(()=>{string line;while((line=child.StandardOutput.ReadLine())!=null){
    if(line=="OWN 1"){if(wanted){granted=hadControl=true;log("NI controller ownership granted");}}
    else if(line=="OWN 0"){if(granted)log("NI controller ownership released or lost");granted=false;}
    else if(line.StartsWith("ERROR ",StringComparison.Ordinal))log("NI helper: "+line.Substring(6));
   }},TaskCreationOptions.LongRunning);
   var started=Stopwatch.StartNew();
   while(wanted && !disposed && !child.HasExited){if(!granted && started.ElapsedMilliseconds>8000){failed=true;log("NI ownership was not granted; no automatic retry");break;}Thread.Sleep(50);}
   granted=false;
   // Stop all queued/in-flight companion panel work before releasing NI.
   while(!panelIdle() && !disposed)Thread.Sleep(10);
   child.StandardInput.Close();
   if(!child.WaitForExit(4000)){child.Kill();child.WaitForExit();log("NI helper exceeded shutdown deadline");}
   reader.Wait(1000);
   if(wanted && !disposed)failed=true;
  }catch(Exception ex){granted=false;if(wanted)failed=true;log("NI session: "+ex.Message);}
  finally{granted=false;if(child!=null){try{if(!child.HasExited){child.Kill();child.WaitForExit(1000);}}catch(InvalidOperationException){}child.Dispose();}}
 }
 public void Reset(){Request(false,-1);failed=false;}
 public void Dispose(){wanted=false;granted=false;disposed=true;if(worker!=null)worker.Wait(5500);}
}

static class NiClientHost {
 const uint Yes=0x74727565,Product=0x1700;
 static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
 public static int Run(int servicePid){
  var stop=new ManualResetEvent(false);var parents=Task.Factory.StartNew(()=>{try{while(Console.ReadLine()!=null){}}finally{stop.Set();}},TaskCreationOptions.LongRunning);
  try{
   string serial=null;var units=new HashSet<string>();
   using(var device=new Registration(servicePid,Words(0x03447500,Product,0x4e694d32,0x70726d79,0),stop,packet=>{
    if(packet.Length>=16 && U32(packet,0)==0x03444e2b && U32(packet,8)==Product){uint length=U32(packet,12);if(length<2 || length>129 || packet.Length!=16+length || packet[packet.Length-1]!=0)throw new IOException("Malformed device state");
     string value=Encoding.ASCII.GetString(packet,16,(int)length-1);if(!value.All(c=>c<128 && char.IsLetterOrDigit(c)))throw new IOException("Invalid serial format");lock(units){units.Add(value);if(units.Count>1)throw new IOException("Multiple Mikro controllers are not supported");}
    }
   })){
    device.Send(Words(0x03447143));var discovery=Stopwatch.StartNew();
    while(discovery.ElapsedMilliseconds<1200 && !stop.WaitOne(20)){}
    lock(units){if(units.Count!=1)throw new IOException("Expected exactly one Mikro MK3");serial=units.Single();}
    bool initialized=false,hasControl=false;var controlGate=new object();
    byte[] identity=Encoding.ASCII.GetBytes(serial+"\0");
    using(var instance=new Registration(servicePid,Join(Words(0x03444900,Product,0x4e694d32,0x70726d79,(uint)identity.Length),identity),stop,packet=>{
     if(packet.Length==8 && U32(packet,0)==0x03434e00){uint state=U32(packet,4);if(state!=0 && state!=Yes)throw new IOException("Invalid ownership state");lock(controlGate){hasControl=state==Yes;if(initialized)Publish(hasControl);}}
    })){
     byte[] title=Encoding.ASCII.GetBytes("Maschine Companion\0");
     byte[][] setup={Words(0x03566775,0x4b657973),Words(0x03566775,0x524b6579),Words(0x03436753),Join(Words(0x0349734e,0x70001006,0xf6b24000,(uint)title.Length),title),Words(0x0344674d,Product),Words(0x03434300,0x73747274),Words(0x03497354,0),Words(0x03436746),Words(0x03497374,Yes),Words(0x03445200,2),Words(0x03444100,3,0,0x04e297b0)};
     foreach(var message in setup){if(stop.WaitOne(0))return 0;instance.Send(message);try{instance.Read(40);}catch(TimeoutException){}}
     lock(controlGate){initialized=true;Publish(hasControl);}
     while(!stop.WaitOne(100)){device.Check();instance.Check();}device.Check();instance.Check();
     Publish(false);
    }
   }
   return 0;
  }catch(OperationCanceledException){return 0;}
  catch(Exception ex){Publish(false);Console.WriteLine("ERROR "+ex.Message.Replace('\n',' ').Replace('\r',' '));return 1;}
  finally{stop.Set();}
 }
 static void Publish(bool owned){Console.WriteLine(owned?"OWN 1":"OWN 0");Console.Out.Flush();}
 internal static void TestTransport(){
  int pid=Process.GetCurrentProcess().Id;string name="NIHWCompanionTest"+Guid.NewGuid().ToString("N");var stop=new ManualResetEvent(false);var release=new ManualResetEvent(false);
  using(var server=Pipe.Listen(name,pid,stop)){
   bool duplicate=false;try{using(var other=Pipe.Listen(name,pid,stop)){}}catch(System.ComponentModel.Win32Exception){duplicate=true;}if(!duplicate)throw new Exception("Duplicate NI server accepted");
   var client=Task.Factory.StartNew(()=>{using(var connection=Pipe.Open(name,pid,stop)){if(connection.Exchange(Words(Yes)).Length!=0)throw new Exception("Empty NI ACK changed");release.WaitOne(5000);}});
   try{server.Accept();if(U32(server.Read(1000),0)!=Yes)throw new Exception("NI message altered");server.Write(new byte[0]);bool timeout=false;try{server.Read(30);}catch(TimeoutException){timeout=true;}if(!timeout)throw new Exception("NI read did not time out");}finally{release.Set();}
   if(!client.Wait(2000))throw new Exception("NI mock client leaked");
  }
  if(Pipe.Names().Contains(name))throw new Exception("NI pipe leaked");
  using(var server=Pipe.Listen(name,pid,stop)){bool refused=false;try{using(var client=Pipe.Open(name,pid+100000,stop)){}}catch(IOException){refused=true;}if(!refused)throw new Exception("NI peer check failed");}
  byte[] req=Encoding.ASCII.GetBytes(name+"Request\0"),note=Encoding.ASCII.GetBytes(name+"Notification\0");byte[] valid=Join(Join(Join(Words(Yes,(uint)req.Length),req),Words((uint)note.Length)),note);
  if(Ports(valid)[0]!=name+"Request" || Ports(Join(valid,Words(0x12345678)))[1]!=name+"Notification")throw new Exception("NI registration parser failed");
  for(int size=0;size<valid.Length;size++){bool refused=false;try{Ports(valid.Take(size).ToArray());}catch(IOException){refused=true;}if(!refused)throw new Exception("Truncated NI registration accepted");}
  foreach(string packet in new[]{"MO1 2 0 0 0 ready","MO1 2 0 1 0 ready","MO1 2 1 1 0 ready","MO1 2 0 2 0 ready","MO1 2 0 0 1 ready"}){NiOwnerState state;if(!NiOwnerState.TryParse(packet.Split(' '),out state))throw new Exception("NI policy parser failed");bool expected=packet=="MO1 2 0 0 0 ready" || packet=="MO1 2 0 1 0 ready";if(state.CompanionRequested!=expected)throw new Exception("Unsafe NI ownership request");}
  foreach(string packet in new[]{"MO1 -1 0 0 0 ready","MO1 2 1 0 0 ready","MO1 2 0 -1 0 ready","MO1 2 0 513 0 ready","MO1 2 0 1 2 ready"}){NiOwnerState state;if(NiOwnerState.TryParse(packet.Split(' '),out state))throw new Exception("Invalid NI policy accepted");}
  Console.WriteLine("PASS NI framed transport, empty ACK, exclusive pipes, peer identity, timeout cancellation, cleanup and owner policy");
 }
 internal static byte[] Words(params uint[] values){var result=new byte[values.Length*4];for(int i=0;i<values.Length;i++)Array.Copy(BitConverter.GetBytes(values[i]),0,result,i*4,4);return result;}
 internal static uint U32(byte[] data,int offset){if(offset<0 || offset>data.Length-4)throw new IOException("Short NI reply");return BitConverter.ToUInt32(data,offset);}
 static byte[] Join(byte[] a,byte[] b){var result=new byte[a.Length+b.Length];Array.Copy(a,result,a.Length);Array.Copy(b,0,result,a.Length,b.Length);return result;}
 internal static string[] Ports(byte[] data){
  if(data.Length>4096 || U32(data,0)!=Yes)throw new IOException("NI refused registration");
  int offset=4;var names=new string[2];
  for(int i=0;i<2;i++){uint size=U32(data,offset);offset+=4;if(size<2 || size>256 || size>data.Length-offset)throw new IOException("Invalid NI endpoint length");if(data[offset+(int)size-1]!=0)throw new IOException("Missing endpoint terminator");
   string name=Utf8.GetString(data,offset,(int)size-1);if(!name.All(c=>c<128 && (char.IsLetterOrDigit(c) || c=='.' || c=='_' || c=='-')))throw new IOException("Invalid NI endpoint name");names[i]=name;offset+=(int)size;
  }
  if(data.Length-offset!=0 && data.Length-offset!=4)throw new IOException("Unexpected NI reply tail");
  if(!names[0].StartsWith("NIHW",StringComparison.Ordinal) || !names[0].EndsWith("Request",StringComparison.Ordinal) || names[1]!=names[0].Substring(0,names[0].Length-7)+"Notification")throw new IOException("Mismatched NI endpoint pair");
  return names;
 }
 sealed class Registration : IDisposable {
  readonly Pipe request,notification;readonly ManualResetEvent stop;readonly Task pump;volatile Exception failure;volatile bool closing;
  public Registration(int pid,byte[] message,ManualResetEvent cancellation,Action<byte[]> callback){
   stop=cancellation;Pipe req=null,note=null;
   try{
    var previous=Pipe.Names();string[] ports;using(var bootstrap=Pipe.Open("com.native-instruments.NIHostIntegrationAgent",pid,stop)){ports=Ports(bootstrap.Exchange(message));}
    if(previous.Contains(ports[0]) || previous.Contains(ports[1]))throw new IOException("NI returned a pre-existing endpoint");
    note=Pipe.Listen(ports[1],pid,stop);notification=note;req=Pipe.Open(ports[0],pid,stop);request=req;
    pump=Task.Factory.StartNew(()=>{try{while(!stop.WaitOne(0) && !closing){notification.Accept();try{while(!stop.WaitOne(0) && !closing){byte[] packet;try{packet=notification.Read(500);}catch(TimeoutException){continue;}if(packet.Length==0)break;notification.Write(new byte[0]);callback(packet);}}catch(IOException ex){var native=ex.InnerException as System.ComponentModel.Win32Exception;if(native==null || native.NativeErrorCode!=109 && native.NativeErrorCode!=232 && native.NativeErrorCode!=233)throw;}finally{notification.Disconnect();}}}catch(Exception ex){if(!closing && !stop.WaitOne(0)){failure=ex;stop.Set();Publish(false);}}},TaskCreationOptions.LongRunning);
    byte[] raw=Encoding.ASCII.GetBytes(ports[1]);request.Write(Join(Words(0x03404300,Yes,0,(uint)raw.Length),Join(raw,new byte[1])));
   }catch{closing=true;if(req!=null)req.Dispose();if(note!=null){note.Cancel();if(pump!=null && !pump.Wait(3000))Environment.Exit(4);note.Dispose();}throw;}
  }
  public void Check(){if(failure!=null)throw new IOException("NI notification connection lost",failure);}
  public void Send(byte[] bytes){Check();request.Write(bytes);}
  public byte[] Read(int timeout){return request.Read(timeout);}
  public void Dispose(){closing=true;request.Dispose();notification.Cancel();if(!pump.Wait(3000))Environment.Exit(4);notification.Dispose();}
 }
 sealed class Pipe : IDisposable {
  readonly SafeFileHandle handle;readonly int pid;readonly bool server;readonly ManualResetEvent stop;
  Pipe(IntPtr value,int peer,bool listening,ManualResetEvent cancellation){if(value==new IntPtr(-1))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());handle=new SafeFileHandle(value,true);pid=peer;server=listening;stop=cancellation;}
  public static Pipe Open(string name,int pid,ManualResetEvent stop){var pipe=new Pipe(CreateFile("\\\\.\\pipe\\"+name,0xc0000000,0,IntPtr.Zero,3,0x40000000,IntPtr.Zero),pid,false,stop);try{pipe.Peer();uint mode=2;if(!SetMode(pipe.handle,ref mode,IntPtr.Zero,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return pipe;}catch{pipe.Dispose();throw;}}
  public static Pipe Listen(string name,int pid,ManualResetEvent stop){return new Pipe(CreateNamedPipe("\\\\.\\pipe\\"+name,3|0x40000000|0x80000,4|2|8,1,65536,65536,2000,IntPtr.Zero),pid,true,stop);}
  void Peer(){uint peer;bool ok=server?GetClientPid(handle,out peer):GetServerPid(handle,out peer);if(!ok || peer!=pid)throw new IOException("Unexpected NI pipe peer");}
  public void Accept(){Operation(null,2000,true);Peer();}
  public void Disconnect(){DisconnectNamedPipe(handle);}
  public void Cancel(){if(!handle.IsClosed)CancelIoEx(handle,IntPtr.Zero);}
  public byte[] Read(int timeout){return Operation(null,timeout,false);}
  public void Write(byte[] data){Operation(data,2000,false);}
  public byte[] Exchange(byte[] data){
   using(var posted=new ManualResetEvent(false)){
    var reader=Task.Factory.StartNew(()=>Operation(null,2000,false,()=>posted.Set()),TaskCreationOptions.LongRunning);
    try{int result=WaitHandle.WaitAny(new WaitHandle[]{posted,stop},1500);if(result==1)throw new OperationCanceledException();if(result!=0)throw new TimeoutException("NI reply read was not posted");Write(data);return reader.GetAwaiter().GetResult();}
    finally{if(!reader.IsCompleted)try{if(!reader.Wait(3000))Environment.Exit(4);}catch(AggregateException){}}
   }
  }
  byte[] Operation(byte[] data,int timeout,bool connect,Action posted=null){
   if(stop.WaitOne(0))throw new OperationCanceledException();
   using(var signal=new EventWaitHandle(false,EventResetMode.ManualReset)){
    var state=new Overlap{Event=signal.SafeWaitHandle.DangerousGetHandle()};IntPtr ov=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlap)));Marshal.StructureToPtr(state,ov,false);
    byte[] bytes=data ?? new byte[65536];var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);uint count=0;bool pending=false;
    try{
     bool ok=connect?ConnectNamedPipe(handle,ov):data==null?ReadFile(handle,pinned.AddrOfPinnedObject(),(uint)bytes.Length,out count,ov):WriteFile(handle,pinned.AddrOfPinnedObject(),(uint)data.Length,out count,ov);
     if(posted!=null)posted();
     int error=ok?0:Marshal.GetLastWin32Error();if(connect && error==535)return new byte[0];
     if(!ok && error!=997)throw IoError(error);
     pending=!ok;var watch=Stopwatch.StartNew();
     while(pending && !signal.WaitOne(Math.Min(50,timeout))){if(stop.WaitOne(0) || watch.ElapsedMilliseconds>=timeout){CancelIoEx(handle,ov);if(!signal.WaitOne(1000))Environment.Exit(4);GetOverlappedResult(handle,ov,out count,false);pending=false;if(stop.WaitOne(0))throw new OperationCanceledException();throw new TimeoutException("NI pipe timeout");}}
     pending=false;if(!GetOverlappedResult(handle,ov,out count,false))throw IoError(Marshal.GetLastWin32Error());
     if(data!=null && count!=data.Length)throw new IOException("Partial NI write");
     if(data!=null || connect)return new byte[0];Array.Resize(ref bytes,(int)count);return bytes;
    }finally{if(pending)Environment.Exit(4);pinned.Free();Marshal.FreeHGlobal(ov);}
   }
  }
  static IOException IoError(int error){return new IOException("NI pipe error "+error,new System.ComponentModel.Win32Exception(error));}
  public void Dispose(){handle.Dispose();}
  public static HashSet<string> Names(){return new HashSet<string>(Directory.GetFiles("\\\\.\\pipe\\").Select(Path.GetFileName),StringComparer.OrdinalIgnoreCase);}
 }
 [StructLayout(LayoutKind.Sequential)]struct Overlap {public UIntPtr Internal,InternalHigh;public uint Offset,OffsetHigh;public IntPtr Event;}
 [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
 [DllImport("kernel32.dll",EntryPoint="CreateNamedPipeW",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateNamedPipe(string name,uint access,uint mode,uint instances,uint output,uint input,uint timeout,IntPtr security);
 [DllImport("kernel32.dll",EntryPoint="SetNamedPipeHandleState",SetLastError=true)]static extern bool SetMode(SafeFileHandle pipe,ref uint mode,IntPtr collection,IntPtr timeout);
 [DllImport("kernel32.dll",EntryPoint="GetNamedPipeServerProcessId",SetLastError=true)]static extern bool GetServerPid(SafeFileHandle pipe,out uint pid);
 [DllImport("kernel32.dll",EntryPoint="GetNamedPipeClientProcessId",SetLastError=true)]static extern bool GetClientPid(SafeFileHandle pipe,out uint pid);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool ConnectNamedPipe(SafeFileHandle pipe,IntPtr ov);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool DisconnectNamedPipe(SafeFileHandle pipe);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadFile(SafeFileHandle pipe,IntPtr data,uint count,out uint read,IntPtr ov);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool WriteFile(SafeFileHandle pipe,IntPtr data,uint count,out uint written,IntPtr ov);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool CancelIoEx(SafeFileHandle pipe,IntPtr ov);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetOverlappedResult(SafeFileHandle pipe,IntPtr ov,out uint count,bool wait);
}
