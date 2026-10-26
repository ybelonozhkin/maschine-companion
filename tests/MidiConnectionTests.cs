using System;
using System.Collections.Generic;

class MidiConnectionTests {
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void RecoveryTests(){
  int creates=0,opens=0;bool ready=false;var logs=new List<string>();
  var connection=new MidiConnectionRetry(()=>{creates++;if(creates==1)throw new InvalidOperationException("service starting");},()=>{opens++;ready=true;return true;},()=>ready,logs.Add);
  connection.Tick(0);Check(creates==1 && opens==0 && !ready,"create failure never opens output");
  connection.Tick(499);Check(creates==1,"creation waits for retry deadline");
  connection.Tick(500);Check(creates==2 && opens==1 && ready && connection.Error=="","creation recovers and clears its error");
  connection.Tick(100000);Check(creates==2 && opens==1,"ready connection is not recreated");
  Check(logs.Count==2 && logs[1].Contains("restored"),"one failure and one recovery log");

  creates=opens=0;ready=false;bool created=false;
  connection=new MidiConnectionRetry(()=>{if(!created){creates++;created=true;}},()=>{opens++;if(opens<3)throw new InvalidOperationException("output busy");ready=true;return true;},()=>ready,logs.Add);
  connection.Tick(0);connection.Tick(499);connection.Tick(500);connection.Tick(1499);
  Check(opens==2 && creates==1 && !ready,"open retries retain the existing cable");
  connection.Tick(1500);Check(opens==3 && creates==1 && ready,"busy output recovers without cable recreation");

  int calls=0;ready=false;
  connection=new MidiConnectionRetry(()=>{},()=>{calls++;if(calls==12)ready=true;return ready;},()=>ready,logs.Add);
  for(int i=0;i<12;i++){connection.Tick(i*500);connection.Tick(i*500+1);}
  Check(calls==12 && ready && !connection.RetryAvailable,"asynchronous enumeration is polled without consuming the failure budget");
 }
 static void RetryBudgetTests(){
  int calls=0;bool fail=true,ready=false;var logs=new List<string>();
  var connection=new MidiConnectionRetry(()=>{calls++;if(fail)throw new InvalidOperationException("unavailable");},()=>{ready=true;return true;},()=>ready,logs.Add);
  foreach(long now in new long[]{0,500,1500,3500,7500,12500}){connection.Tick(now);connection.Tick(now+1);}
  Check(calls==6 && connection.RetryAvailable && !ready,"six failures stop automatic attempts");
  connection.Tick(999999);Check(calls==6,"exhausted retries do not busy-loop");
  Check(logs.Count==2,"unchanged errors are not logged at every retry");
  fail=false;connection.Resume(1000000);connection.Tick(999999);Check(calls==6,"Resume uses the current retry deadline");
  connection.Tick(1000000);Check(calls==7 && ready && !connection.RetryAvailable && connection.Error=="","Resume rearms connection recovery");
  foreach(Exception missing in new Exception[]{new DllNotFoundException("missing"),new EntryPointNotFoundException("missing"),new BadImageFormatException("wrong architecture")}){
   calls=0;connection=new MidiConnectionRetry(()=>{calls++;throw missing;},()=>false,()=>false,logs.Add);
   connection.Tick(0);connection.Tick(100000);Check(calls==1 && connection.RetryAvailable,"installation errors require explicit retry");
  }
 }
 static void OutputTests(){
  int opens=0,sends=0,closes=0;bool failSend=true,failClose=false;
  var output=new MidiOutputSession(id=>{opens++;return new IntPtr(opens);},(handle,message)=>{sends++;Check(message==(0xb0u|(123u<<8)),"initialization sends All Notes Off");return failSend?1u:0u;},handle=>{closes++;return failClose?1u:0u;});
  bool failed=false;try{output.Open(0);}catch(InvalidOperationException){failed=true;}
  Check(failed && !output.Ready && opens==1 && closes==1,"failed initialization closes unpublished handle");
  failSend=false;output.Open(0);Check(output.Ready && opens==2 && sends==2,"output is ready only after initialization succeeds");
  output.Open(0);Check(opens==2 && sends==2,"ready output is reused");
  output.Close();Check(!output.Ready && closes==2,"normal close clears readiness");
  failSend=failClose=true;failed=false;try{output.Open(0);}catch(AggregateException){failed=true;}
  Check(failed && !output.Ready && opens==3,"failed initialization and close leave output unusable");
  failed=false;try{output.Open(0);}catch(InvalidOperationException){failed=true;}
  Check(failed && opens==3,"retained handle must close before another open");
  failSend=failClose=false;output.Open(0);Check(output.Ready && opens==4,"retry cleans retained handle before reopening");output.Close();
  output=new MidiOutputSession(id=>new IntPtr(1),(handle,message)=>{throw new InvalidOperationException("driver error");},handle=>{closes++;return 0;});
  int before=closes;failed=false;try{output.Open(0);}catch(InvalidOperationException){failed=true;}
  Check(failed && closes==before+1 && !output.Ready,"managed send exception also closes unpublished handle");
  output=new MidiOutputSession(id=>{throw new InvalidOperationException("open busy");},(handle,message)=>0,handle=>{closes++;return 0;});
  before=closes;failed=false;try{output.Open(0);}catch(InvalidOperationException){failed=true;}
  Check(failed && closes==before && !output.Ready,"failed open publishes no handle and attempts no invalid close");
 }
 static void Main(){RecoveryTests();RetryBudgetTests();OutputTests();Console.WriteLine("PASS MIDI acquisition recovery, backoff, retry budget, Resume, cable reuse and transactional output initialization");}
}
