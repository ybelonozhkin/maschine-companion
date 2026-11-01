using System;
using System.Collections.Generic;

static class AftertouchTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static byte[] Pad(int id,int kind,int raw){var data=new byte[64];data[0]=2;data[1]=(byte)id;data[2]=(byte)(kind|(raw>>8));data[3]=(byte)raw;return data;}
 public static int Main(){
  var sent=new List<string>();bool allowed=true;
  var expression=new PadAftertouch((n,v)=>sent.Add("P:"+n+":"+v),v=>sent.Add("C:"+v),()=>allowed);
  Check(PadAftertouch.Convert(0)==0&&PadAftertouch.Convert(4095)==127&&PadAftertouch.Convert(2048)==64,"pressure normalization includes zero and endpoints");
  expression.Held(0,60,true);expression.Pressure(0,4095);Check(sent.Count==0,"Off is the default");
  expression.Mode=AftertouchMode.Polyphonic;Check(sent[sent.Count-1]=="P:60:127","enabling aftertouch preserves held-note pressure");
  expression.Held(1,64,true);expression.Pressure(1,2048);Check(sent[sent.Count-1]=="P:64:64","different notes have independent pressure");
  expression.Held(0,60,false);Check(sent[sent.Count-1]=="P:60:0","release resets only its own pitch");
  expression.Held(2,64,true);expression.Pressure(2,4095);expression.Held(2,64,false);Check(sent[sent.Count-1]=="P:64:64","coincident pitches retain the remaining pad's pressure");
  expression.Mode=AftertouchMode.Channel;Check(sent[sent.Count-2]=="P:64:0"&&sent[sent.Count-1]=="C:64","mode changes clear old output without releasing notes");
  expression.Held(0,60,true);expression.Pressure(0,4095);Check(sent[sent.Count-1]=="C:127","channel uses maximum held pressure");
  expression.Held(0,60,false);Check(sent[sent.Count-1]=="C:64","releasing the maximum falls back to the other pad");
  allowed=false;expression.Refresh();Check(sent[sent.Count-1]=="C:0","generated-playback gate clears expressive pressure");
  int count=sent.Count;expression.Pressure(1,1024);Check(sent.Count==count,"blocked pressure is not sent");
  allowed=true;expression.Refresh();Check(sent[sent.Count-1]=="C:32","refresh uses the latest pressure");
  expression.Clear();Check(sent[sent.Count-1]=="C:0","cleanup resets channel pressure");count=sent.Count;expression.Pressure(1,4095);Check(sent.Count==count,"released pads cannot revive expression");

  foreach(bool sixteen in new[]{false,true}){
   sent.Clear();var input=new MikroInput{Fixed=!sixteen,Sixteen=sixteen,SelectedNote=60};
   var mapper=new PadAftertouch((n,v)=>sent.Add(n+":"+v),v=>{},()=>true);mapper.Mode=AftertouchMode.Polyphonic;
   input.PadHeld+=mapper.Held;input.RawPressure+=mapper.Pressure;int notes=0,repeatPressure=0;input.Note+=(n,v,d)=>notes++;input.Pressure+=(n,v)=>repeatPressure++;
   input.Feed(Pad(12,0x40,4095));Check(sent.Count==0,"pressure before a strike is ignored");
   input.Feed(Pad(12,0x10,3200));input.Feed(Pad(12,0x40,4095));Check(sent[sent.Count-1]==(sixteen?"60:127":"36:127")&&notes==1&&repeatPressure==0,"Fixed/16 Vel keep expressive pressure separate from repeat velocity");
   input.CancelHeldNotes();Check(sent[sent.Count-1].EndsWith(":0")&&notes==2,"cancel clears pressure and emits one Note Off");
   count=sent.Count;input.Feed(Pad(12,0x40,4095));Check(sent.Count==count,"cancelled physical pad cannot send pressure");
  }
  var commandInput=new MikroInput{StepOnly=true};int heldEvents=0,rawEvents=0;
  commandInput.PadHeld+=(p,n,d)=>heldEvents++;commandInput.RawPressure+=(p,v)=>rawEvents++;
  commandInput.Feed(Pad(12,0x10,3200));commandInput.Feed(Pad(12,0x40,4095));commandInput.Feed(Pad(12,0x30,0));Check(heldEvents==0&&rawEvents==0,"Step commands do not emit playing expression");
  bool fail=true;int otherReset=0;
  var faulty=new PadAftertouch((n,v)=>{if(v==0&&n==60&&fail)throw new Exception("injected pressure cleanup failure");if(n==64&&v==0)otherReset++;},v=>{},()=>true){Mode=AftertouchMode.Polyphonic};
  faulty.Held(0,60,true);faulty.Held(1,64,true);faulty.Pressure(0,4095);faulty.Pressure(1,4095);
  bool threw=false;try{faulty.Clear();}catch(Exception){threw=true;}Check(threw&&otherReset==1,"one cleanup failure does not prevent other notes being reset");
  fail=false;faulty.Clear();Check(otherReset==1,"failed cleanup can be retried without resending successful resets");
  Console.WriteLine("PASS aftertouch normalization, modes, per-pad aggregation, shared notes, cleanup, Fixed/16 Vel and command exclusion");return 0;
 }
}
