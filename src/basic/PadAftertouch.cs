using System;
using System.Collections.Generic;

enum AftertouchMode { Off, Channel, Polyphonic }

// Physical pad expression only. NativeMidi deduplicates the actual wire values.
sealed class PadAftertouch {
 readonly int[] notes=new int[16],pressure=new int[16],sentPoly=new int[128];
 readonly Action<int,int> poly;readonly Action<int> channel;readonly Func<bool> allowed;
 AftertouchMode mode;int sentChannel;
 public PadAftertouch(Action<int,int> polyOutput,Action<int> channelOutput,Func<bool> canSend){
  poly=polyOutput;channel=channelOutput;allowed=canSend;for(int i=0;i<16;i++)notes[i]=-1;
 }
 public AftertouchMode Mode {get{return mode;}set{
  if(!Enum.IsDefined(typeof(AftertouchMode),value))throw new ArgumentOutOfRangeException("value");
  if(mode==value)return;ClearOutputs();mode=value;Refresh();
 }}
 public static int Convert(int raw){return (Math.Max(0,Math.Min(4095,raw))*127+2047)/4095;}
 public void Held(int pad,int note,bool down){
  if(pad<0||pad>=16||note<0||note>127)return;int previous=notes[pad];
  notes[pad]=down?note:-1;pressure[pad]=0;
  if(previous>=0&&previous!=note)Apply(previous);Apply(note);
 }
 public void Pressure(int pad,int raw){if(pad<0||pad>=16||notes[pad]<0)return;pressure[pad]=Convert(raw);Apply(notes[pad]);}
 void Apply(int note){
  if(mode==AftertouchMode.Off)return;
  if(!allowed()){ClearOutputs();return;}
  int maximum=0;for(int p=0;p<16;p++)if(notes[p]>=0&&(mode==AftertouchMode.Channel||notes[p]==note))maximum=Math.Max(maximum,pressure[p]);
  if(mode==AftertouchMode.Channel){if(maximum!=0||sentChannel!=0){channel(maximum);sentChannel=maximum;}}
  else if(maximum!=0||sentPoly[note]!=0){poly(note,maximum);sentPoly[note]=maximum;}
 }
 public void Refresh(){
  if(!allowed()){ClearOutputs();return;}
  if(mode==AftertouchMode.Channel){Apply(0);return;}
  if(mode==AftertouchMode.Polyphonic){var seen=new HashSet<int>();foreach(int note in notes)if(note>=0&&seen.Add(note))Apply(note);}
 }
 void ClearOutputs(){
  Exception error=null;
  for(int n=0;n<128;n++)if(sentPoly[n]!=0)try{poly(n,0);sentPoly[n]=0;}catch(Exception ex){error=ex;}
  if(sentChannel!=0)try{channel(0);sentChannel=0;}catch(Exception ex){error=ex;}
  if(error!=null)throw error;
 }
 public void Clear(){try{ClearOutputs();}finally{for(int p=0;p<16;p++){notes[p]=-1;pressure[p]=0;}}}
}
