using System;
using System.Collections.Generic;

class NoteRepeatTests {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
 static void CleanupTests(){
  bool fail=false;int sends=0;
  var repeat=new NoteRepeat((n,v,down)=>{sends++;if(fail && !down)throw new InvalidOperationException("injected note release failure");},Console.WriteLine);
  var thread=(System.Threading.Thread)typeof(NoteRepeat).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(repeat);
  repeat.Note(36,100,true);fail=true;
  bool reported=false;try{repeat.Dispose();}catch(InvalidOperationException){reported=true;}
  Check(reported && !thread.IsAlive,"failed note release still stops and joins scheduler");
  int previous=sends;repeat.Note(38,100,true);repeat.Pump();repeat.Dispose();
  Check(sends==previous,"disposed scheduler cannot emit and disposal is idempotent");
 }
 static void RhythmTests(){
  var masks=new HashSet<int>();
  int[] original={255,85,17,1,51,119,221,239,73,109,218,182,41,83,39,69};
  Check(NoteRepeat.RhythmCount==64,"expanded bank has 64 rhythms");
  for(int pattern=0;pattern<64;pattern++){
   double now=0;var ons=new List<int>();var active=new HashSet<int>();
   using(var arp=new NoteRepeat((n,v,d)=>{if(d){Check(active.Add(n),"rhythm notes never overlap the same pitch");Check(v==90,"rhythm keeps strike velocity");ons.Add(n);}else Check(active.Remove(n),"rhythm NoteOff has a matching NoteOn");},Console.WriteLine,()=>now,false)){
    arp.Sync(120,0,true);arp.SetArpeggiator(true);arp.TurnArp(100,true);
    Check(arp.ArpParameter=="RHYTHM" && !arp.ArpRight,"Rhythm is the last bounded Arp page");
    arp.TurnArp(pattern,false);int mask=arp.ArpRhythmMask;
    Check(arp.ArpRhythmNumber==pattern+1 && mask>0 && masks.Add(mask),"64 distinct nonempty rhythm presets");
    if(pattern<original.Length)Check(mask==original[pattern],"original rhythm numbers remain unchanged");
    if(pattern==0)Check(mask==255,"first rhythm preserves continuous Arp");
    if(pattern==8)Check(mask==73,"Tresillo has attacks on steps 1, 4 and 7");
    if(pattern==9)Check(mask==109,"Cinquillo has attacks on steps 1, 3, 4, 6 and 7");
    arp.SetLatch(true);foreach(int note in new[]{60,64,67})arp.Note(note,90,true);
    foreach(int note in new[]{60,64,67}){arp.Pressure(note,10);arp.Note(note,0,false);}
    int sounded=0;
    for(int step=0;step<16;step++){
     now=step*.125;int before=ons.Count;arp.Pump();bool hit=(mask&(1<<(step%8)))!=0;
     Check(ons.Count==before+(hit?1:0),"rhythm mask repeats over two cycles: "+pattern+" step "+step);
     if(hit){Check(ons[ons.Count-1]==new[]{60,64,67}[sounded%3],"rests do not consume chord notes");sounded++;}
     now+=.063;arp.Pump();Check(active.Count==0,"gate releases finish even around rests");
    }
    arp.TurnArp(100,false);Check(arp.ArpRhythmNumber==64,"rhythm selection stops at preset 64");
    arp.TurnArp(-100,false);Check(arp.ArpRhythmNumber==1,"rhythm selection stops at preset 1");
   }
   Check(active.Count==0,"leaving rhythmic Arp releases voices");
  }
  {
   double now=0;var active=new HashSet<int>();
   using(var arp=new NoteRepeat((n,v,d)=>{if(d)active.Add(n);else active.Remove(n);},Console.WriteLine,()=>now,false)){
    arp.Sync(120,0,false);arp.SetArpeggiator(true);arp.TurnArp(3,true);arp.TurnArp(50,false);
    arp.TurnArp(2,true);arp.TurnArp(1,false);arp.Note(60,90,true);arp.Pump();
    now=.124;arp.Pump();Check(active.Contains(60),"100 percent gate lasts until the rest boundary");
    now=.125;arp.Pump();Check(active.Count==0,"a rhythm rest releases even a full-length gated note");
   }
  }
  {
   double now=0;var hits=new List<double>();
   using(var arp=new NoteRepeat((n,v,d)=>{if(d)hits.Add(now);},Console.WriteLine,()=>now,false)){
    arp.Sync(120,0,false);arp.SetArpeggiator(true);arp.TurnArp(1,false);
    arp.TurnArp(4,true);arp.TurnArp(25,false);arp.TurnArp(1,true);arp.TurnArp(1,false);arp.Note(60,90,true);
    for(int step=0;step<12;step++){now=step/12.0;arp.Pump();}
    Check(hits.Count==6,"triplet rhythm preserves its active steps with swing enabled");
    for(int hit=0;hit<hits.Count;hit++)Check(Math.Abs(hits[hit]-hit/6.0)<1e-8,"triplet rests stay evenly timed");
   }
  }
  {
   double now=0;var hits=new List<double>();
   using(var arp=new NoteRepeat((n,v,d)=>{if(d)hits.Add(now);},Console.WriteLine,()=>now,false)){
    arp.Sync(120,1.125,true);arp.SetArpeggiator(true);arp.TurnArp(5,true);arp.TurnArp(1,false);arp.Note(60,90,true);
    now=.0625;arp.Pump();Check(hits.Count==0,"starting during playback preserves an absolute-grid rest");
    now=.1875;arp.Pump();Check(hits.Count==1,"next active rhythm step remains aligned to Live's grid");
    arp.Reset();hits.Clear();now=0;arp.Sync(120,0,false);arp.SetArpeggiator(true);arp.Note(60,90,true);arp.Pump();
    now=.375;arp.Pump();Check(hits.Count==1,"delayed pump landing on a rest does not replay missed hits");
    now=.5;arp.Pump();Check(hits.Count==2,"delayed pump resumes at the next active step");
    arp.Reset();hits.Clear();arp.SetEnabled(true);arp.Note(60,90,true);
    arp.Pump();now+=.125;arp.Pump();Check(hits.Count==2,"Arp rhythm does not affect Pad Mode Note Repeat");
   }
  }
 }
 static void ArpTests(){
  {
   double now=0;var ons=new List<string>();var active=new HashSet<int>();
   using(var arp=new NoteRepeat((n,v,d)=>{if(d){Check(active.Add(n),"Latch retriggers release the previous voice");ons.Add(n+":"+v);}else Check(active.Remove(n),"Latch releases have matching notes");},Console.WriteLine,()=>now,false)){
    arp.Sync(120,0,false);arp.SetLatch(true);Check(!arp.Latch,"Latch cannot enable Arp");
    arp.SetArpeggiator(true);arp.Note(60,70,true);arp.Note(64,80,true);arp.SetLatch(true);
    arp.Pump();arp.Pressure(60,100);arp.Note(60,0,false);arp.Note(64,0,false);arp.Pressure(60,20);
    now=.125;arp.Pump();now=.25;arp.Pump();
    Check(string.Join(",",ons)=="60:70,64:80,60:70","Latch repeats released chord at each note's strike velocity despite changing pressure");
    arp.Note(67,90,true);arp.Note(71,60,true);arp.Note(67,0,false);arp.Note(71,0,false);
    ons.Clear();now=.375;arp.Pump();now=.5;arp.Pump();
    Check(string.Join(",",ons)=="67:90,71:60","new chord replaces the released latched chord and starts at its first note");
    arp.SetLatch(false);Check(!arp.Latch && !arp.HasHeldNotes && active.Count==0,"Latch off releases all remembered notes immediately");
    arp.SetLatch(true);arp.Note(60,90,true);arp.Note(64,80,true);arp.Note(60,0,false);arp.SetLatch(false);
    ons.Clear();now=.625;arp.Pump();Check(string.Join(",",ons)=="64:80","Latch off preserves physically held notes only");
    arp.Note(64,0,false);Check(active.Count==0,"physical release remains balanced after Latch off");
    arp.SetLatch(true);arp.Note(72,100,true);arp.Note(72,0,false);now=.75;arp.Pump();
    arp.Reset();Check(!arp.Latch && !arp.Arpeggiator && !arp.HasHeldNotes && active.Count==0,"reset clears Latch and its chord");
    arp.SetArpeggiator(true);Check(!arp.Latch && !arp.HasHeldNotes,"a new Arp session cannot inherit a latched chord");
    arp.SetLatch(true);arp.Note(60,100,true);arp.Note(60,0,false);arp.Pump();now=3;arp.Pump();
    Check(!arp.Latch && !arp.Enabled && active.Count==0,"clock loss releases latched voices");
   }
  }
  {
   double now=0;var ons=new List<int>();var active=new HashSet<int>();
   using(var arp=new NoteRepeat((n,v,d)=>{if(d){Check(active.Add(n),"held chord conversion releases ordinary voices");ons.Add(n);}else Check(active.Remove(n),"held chord conversion has balanced releases");},Console.WriteLine,()=>now,false)){
    arp.Sync(120,0,false);arp.Note(64,80,true);arp.Note(60,90,true);ons.Clear();arp.SetArpeggiator(true);arp.TurnArp(1,true);arp.TurnArp(3,false);arp.Pump();now=.125;arp.Pump();
    Check(string.Join(",",ons)=="64,60","enabling Arp accepts an already held chord and remembers Played order");
   }
   Check(active.Count==0,"disposing Arp releases all generated notes");
  }
  int[][] expected={new[]{60,64,67,60,64,67},new[]{67,64,60,67,64,60},new[]{60,64,67,64,60,64},new[]{64,60,67,64,60,67},new[]{60,64,67,60,64,67,60,64,67,60,64,67,60,64,67,60,64,67}};
  for(int type=0;type<5;type++){
   double now=0;var ons=new List<int>();var active=new HashSet<int>();
   using(var arp=new NoteRepeat((n,v,d)=>{if(d){Check(v==90,"every Arp order retains strike velocity while pressure changes");Check(active.Add(n),"Arp retrigger releases its previous voice");ons.Add(n);}else Check(active.Remove(n),"Arp NoteOff has a matching NoteOn");},Console.WriteLine,()=>now,false)){
    arp.Sync(120,0,false);arp.SetArpeggiator(true);arp.TurnArp(1,true);arp.TurnArp(type,false);
    foreach(int note in new[]{64,60,67})arp.Note(note,90,true);
    foreach(int note in new[]{64,60,67})arp.Pressure(note,10);
    for(int pulse=0;pulse<6;pulse++){now=pulse*.125;arp.Pump();}
    Check(string.Join(",",ons)==string.Join(",",expected[type]),"Arp order "+type);
    arp.SetArpeggiator(false);Check(active.Count==0,"disabling Arp releases generated voices");
   }
  }
  {
   double now=0;var events=new List<string>();var origins=new List<int>();
   using(var arp=new NoteRepeat((n,v,d)=>events.Add(n+":"+v+":"+d),Console.WriteLine,()=>now,false)){
    arp.Generated+=origins.Add;arp.Sync(120,0,false);arp.SetArpeggiator(true);arp.TurnArp(2,true);arp.TurnArp(1,false);
    arp.Note(60,70,true);arp.Pump();arp.Pressure(60,110);now=.125;arp.Pump();
    Check(events[events.Count-1]=="72:70:True"&&string.Join(",",origins)=="60,60","octave expansion retains source strike velocity and source pad feedback");
    arp.Note(60,0,false);Check(events[events.Count-1]=="72:0:False","source release stops its generated octave");
    int before=events.Count;now=.25;arp.Pump();Check(events.Count==before,"released source cannot repeat");
    arp.Note(127,90,true);arp.TurnArp(7,false);
    for(int pulse=3;pulse<8;pulse++){now=pulse*.125;arp.Pump();}
    foreach(string e in events)Check(int.Parse(e.Split(':')[0])<=127,"expanded pitches remain in MIDI range");
    arp.TurnArp(-10,true);arp.TurnArp(2,false);
    arp.Reset();Check(!arp.Arpeggiator&&!arp.HasHeldNotes,"reset clears all Arp input");
    arp.Turn(1,false);arp.SetEnabled(true);Check(arp.Label.Contains("1/16T"),"drum rate remains independent from Arp settings");
   }
  }
  {
   double now=0;var events=new List<string>();
   using(var arp=new NoteRepeat((n,v,d)=>events.Add(n+":"+d),Console.WriteLine,()=>now,false)){
    arp.Sync(120,0,false);arp.SetArpeggiator(true);arp.TurnArp(3,true);arp.TurnArp(50,false);arp.Note(60,90,true);arp.Pump();
    now=.12;arp.Pump();Check(events.Count==1,"100 percent gate sustains until next grid pulse");
    now=.125;arp.Pump();Check(string.Join(",",events)=="60:True,60:False,60:True","gate retrigger has balanced release");
    arp.Note(64,80,true);now=.25;arp.Pump();Check(events[events.Count-1]=="60:True","adding a held note preserves phrase phase");
    now=.375;arp.Pump();Check(events[events.Count-1]=="64:True","newly held note joins on the existing grid");
    now=3;arp.Pump();Check(!arp.Enabled&&events[events.Count-1]=="64:False","lost clock stops latched Arp and releases its last note");
   }
  }
  {
   double now=0;var hits=new List<double>();int sample=1;
   using(var arp=new NoteRepeat((n,v,d)=>{if(d)hits.Add(now);},Console.WriteLine,()=>now,false)){
    arp.Sync(120,0,true);arp.SetArpeggiator(true);arp.Note(60,100,true);arp.Note(64,70,true);
    for(int tick=0;tick<=8000;tick++){now=tick*.0005;double sent=sample*.1,delay=new[]{.003,.047,.021,.039}[sample%4];if(now>=sent+delay){arp.Sync(120,sent*2,true,now-sent);sample++;}arp.Pump();}
   }
   Check(hits.Count==33,"Arp retains every pulse with irregular clock delivery");
   for(int i=1;i<hits.Count;i++)Check(Math.Abs(hits[i]-hits[i-1]-.125)<.001,"Arp clock delivery does not change note spacing");
  }
 }
 static void DirectNotesSurviveTransportChanges(){
  double now=0;var events=new List<string>();
  using(var repeat=new NoteRepeat((n,v,d)=>events.Add(n+":"+v+":"+d),Console.WriteLine,()=>now,false)){
   repeat.Sync(120,-4,false);
   repeat.Note(60,90,true);repeat.Note(64,70,true);
   now=.05;repeat.Sync(120,0,true); // First transport sample after count-in.
   Check(events.Count==2,"count-in completion must not release manually held notes when repeat is off");
   now=.1;repeat.Sync(120,8,true);repeat.Pump();
   Check(events.Count==2,"transport seek must not release manually held notes");
   repeat.Note(60,0,false);
   Check(events.Count==3 && events[2]=="60:0:False","physical release ends only its own manual note");
   now=.2;repeat.Sync(120,0,false);
   Check(events.Count==3,"transport stop preserves the other held manual note");
   repeat.Note(64,0,false);
   Check(events.Count==4 && events[3]=="64:0:False","remaining manual note ends on physical release");
   repeat.Note(67,100,true);repeat.Reset();
   Check(events.Count==6 && events[5]=="67:0:False","explicit reset still releases manual notes");
  }
 }
 static int Main(){
  DirectNotesSurviveTransportChanges();
  double now=0;var events=new List<string>();
  using(var repeat=new NoteRepeat((n,v,d)=>events.Add(n+":"+v+":"+d),Console.WriteLine,()=>now,false)){
   repeat.Sync(120,0,false);repeat.SetEnabled(true);repeat.Note(36,90,true);repeat.Pump();
   Check(events.Count==1&&events[0]=="36:90:True","stopped repeat first hit and velocity");
   now=.063;repeat.Pump();Check(events.Count==2&&events[1]=="36:0:False","gate sends note off");
   now=.125;repeat.Pump();Check(events.Count==3,"sixteenth at 120 BPM");
   repeat.Note(38,70,true);now=.250;repeat.Pump();Check(events[events.Count-1]=="38:70:True","held chord repeats together");
   repeat.Note(36,0,false);int before=events.Count;now=.375;repeat.Pump();
   Check(events.Count==before+2&&events[events.Count-1]=="38:70:True","released pad no longer repeats");
   repeat.SetEnabled(false);before=events.Count;now=.6;repeat.Pump();Check(events.Count==before,"button release ends repeats");
   repeat.Reset();events.Clear();now=1;repeat.Sync(120,4.1,true);repeat.SetEnabled(true);repeat.Note(40,100,true);repeat.Pump();Check(events.Count==0,"playing notes wait for grid");
   now=1.075;repeat.Pump();Check(events.Count==1,"transport aligned pulse");
   now=1.625;repeat.Pump();int ons=0;foreach(string e in events)if(e.EndsWith(":True"))ons++;Check(ons==2,"missed ticks do not burst");
   repeat.Reset();events.Clear();repeat.Sync(120,0,false);repeat.SetEnabled(true);repeat.Turn(25,true);repeat.Note(36,80,true);repeat.Pump();
   now+=.125;repeat.Pump();Check(events.Count==2,"75 percent swing delays the odd pulse");
   now+=.0625;repeat.Pump();Check(events.Count==3,"swung offbeat at 75 percent");
   now+=3;repeat.Pump();Check(!repeat.Enabled,"lost clock stops repeat");
  }
  Check(Math.Abs(NoteRepeat.Grid(1,.25,75,false)-.375)<1e-8,"MPC swing proportion");
  Check(Math.Abs(NoteRepeat.Grid(1,1.0/6,75,true)-1.0/6)<1e-8,"triplets remain straight");
  now=0;events.Clear();
  using(var repeat=new NoteRepeat((n,v,d)=>events.Add(n+":"+v+":"+d),Console.WriteLine,()=>now,false)){
   repeat.Sync(120,0,false);repeat.SetEnabled(true);repeat.Note(36,50,true);repeat.Note(38,70,true);repeat.Pump();
   int beforePressure=events.Count;now=.04;repeat.Pressure(36,110);repeat.Pressure(40,127);
   Check(events.Count==beforePressure,"pressure does not retrigger or create a voice");
   now=.124;repeat.Pump();Check(events.Count==4,"pressure does not advance the repeat grid");
   now=.125;repeat.Pump();Check(events[events.Count-2]=="36:110:True"&&events[events.Count-1]=="38:70:True","each held note keeps independent pressure velocity on the next pulse");
   now=.2;repeat.Pressure(36,20);now=.25;repeat.Pump();
   Check(events[events.Count-2]=="36:20:True","relaxing pressure lowers the following repeat velocity");
   repeat.Note(36,0,false);repeat.Pressure(36,127);now=.375;repeat.Pump();
   Check(events[events.Count-1]=="38:70:True","pressure cannot restore a released note");
   repeat.Reset();events.Clear();repeat.SetEnabled(true);repeat.Note(36,60,true);repeat.Pump();
   Check(events.Count==1&&events[0]=="36:60:True","new strike cannot inherit stale pressure");
   repeat.Reset();events.Clear();repeat.Note(36,80,true);repeat.Pressure(36,10);
   Check(events.Count==1&&events[0]=="36:80:True","pressure outside repeat does not alter ordinary playing");
  }
  // Mimic Live samples delivered through a 50 ms UI receive tick. The sender's
  // timestamp must make varying delivery delays invisible to the pulse grid.
  now=0;var hits=new List<double>();int sample=1;
  using(var repeat=new NoteRepeat((n,v,d)=>{if(d)hits.Add(now);},Console.WriteLine,()=>now,false)){
   repeat.Sync(120,0,true);repeat.SetEnabled(true);repeat.Note(36,100,true);
   for(int tick=0;tick<=8000;tick++){
    now=tick*.0005;
    double sent=sample*.1,delay=new[]{.003,.047,.021,.039}[sample%4];
    if(now>=sent+delay){repeat.Sync(120,sent*2,true,now-sent);sample++;}
    repeat.Pump();
   }
  }
  Check(hits.Count==33,"timestamped clock preserves all sixteenth pulses");
  for(int i=1;i<hits.Count;i++)Check(Math.Abs(hits[i]-hits[i-1]-.125)<.001,"packet delay cannot shift repeat timing");
  now=0;hits.Clear();sample=1;
  using(var repeat=new NoteRepeat((n,v,d)=>{if(d)hits.Add(now);},Console.WriteLine,()=>now,false)){
   repeat.Sync(120,0,true);repeat.SetEnabled(true);repeat.Note(36,100,true);
   for(int tick=0;tick<=8000;tick++){
    now=tick*.0005;double sent=sample*.1;
    if(now>=sent+.04){double jitter=sample%2==0?.006:-.006;repeat.Sync(120,sent*2+jitter,true,now-sent);sample++;}
    repeat.Pump();
   }
  }
  for(int i=1;i<hits.Count;i++)Check(Math.Abs(hits[i]-hits[i-1]-.125)<.003,"small position jitter is filtered");
  now=0;hits.Clear();
  using(var repeat=new NoteRepeat((n,v,d)=>{if(d)hits.Add(now);},Console.WriteLine,()=>now,false)){
   repeat.Sync(120,0,true);repeat.SetEnabled(true);repeat.Note(36,100,true);repeat.Pump();
   now=.1;repeat.Sync(60,.2,true);repeat.Pump();now=.15;repeat.Pump();
   Check(hits.Count==2,"tempo change preserves phase and uses new tempo");
   now=.2;repeat.Sync(60,0,true);repeat.Pump();Check(hits.Count==3,"backward seek resets the repeat grid");
   now=.3;repeat.Sync(60,8,true);repeat.Pump();Check(hits.Count==4,"forward seek resets the repeat grid");
  }
  ArpTests();RhythmTests();CleanupTests();Console.WriteLine("PASS note repeat and Arp orders, rhythms, octaves, pressure, gate, releases, clock jitter, tempo, seek, clock loss and failure-safe cleanup");return 0;
 }
}
