using System;
using System.Diagnostics;

enum MidiClockSource { Internal, External }
struct BasicClockState {public bool Available,Running,External;public double Beat,SongBeat,Tempo;public long Revision;}

sealed class BasicClock {
 readonly object gate=new object();readonly Func<double> time;double tempo=120,anchorTime,anchorBeat,songBeat,lastPulse=double.NegativeInfinity,lastInterval;int intervals;bool running;long revision;MidiClockSource source;
 public BasicClock(Func<double> now=null){time=now ?? (()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency);anchorTime=time();}
 public MidiClockSource Source {get{lock(gate)return source;}}
 public double Tempo {get{lock(gate)return tempo;}}
 public void Configure(MidiClockSource value,double bpm){if(bpm<20||bpm>999||double.IsNaN(bpm)||double.IsInfinity(bpm))throw new ArgumentOutOfRangeException("bpm");lock(gate){double now=time();anchorBeat=source==MidiClockSource.Internal?anchorBeat+(now-anchorTime)*tempo/60:anchorBeat;source=value;tempo=bpm;anchorTime=now;lastPulse=double.NegativeInfinity;intervals=0;lastInterval=0;running=false;revision++;}}
 public void SetTempo(double value){lock(gate){if(source!=MidiClockSource.Internal)return;if(value<20||value>999||double.IsNaN(value))throw new ArgumentOutOfRangeException("value");double now=time();anchorBeat+=(now-anchorTime)*tempo/60;anchorTime=now;tempo=value;}}
 public BasicClockState Read(){lock(gate){double now=time();bool internalClock=source==MidiClockSource.Internal;bool available=internalClock || intervals>=1 && now-lastPulse<=1.0;double fraction=available?Math.Min(now-lastPulse,60/tempo/24)*tempo/60:0;return new BasicClockState{Available=available,Running=internalClock||running,External=!internalClock,Tempo=tempo,Revision=revision,Beat=internalClock?anchorBeat+(now-anchorTime)*tempo/60:anchorBeat+fraction,SongBeat=songBeat+(running?fraction:0)};}}
 public void Receive(uint packet){lock(gate){if(source!=MidiClockSource.External)return;double now=time();int status=(int)(packet&255);
  if(status==0xfa){anchorBeat=songBeat=0;anchorTime=now;lastPulse=double.NegativeInfinity;intervals=0;lastInterval=0;running=true;revision++;return;}
  if(status==0xfb){if(now-lastPulse>1.0){lastPulse=double.NegativeInfinity;intervals=0;lastInterval=0;}running=true;return;}
  if(status==0xfc){running=false;return;}
  if(status==0xf2){songBeat=(((packet>>8)&127)|(((packet>>16)&127)<<7))*.25;anchorTime=now;revision++;return;}
  if(status!=0xf8)return;
  double interval=now-lastPulse;
  if(interval>1.0){intervals=0;lastInterval=0;if(!double.IsNegativeInfinity(lastPulse))running=false;}
  else if(interval>=60.0/999/24 && interval<=60.0/20/24){lastInterval=lastInterval==0?interval:lastInterval*.85+interval*.15;intervals++;tempo=Math.Max(20,Math.Min(999,60/(24*lastInterval)));}
  anchorBeat+=1.0/24;if(running)songBeat+=1.0/24;anchorTime=lastPulse=now;
 }}
}
