using System;

// Mikro MK3 native reports, validated against the local 2026-10-17 capture.
// No device I/O: callbacks must enqueue/send immediately and must not write LEDs.
enum MikroTransport { Play, Stop, Record, TapTempo, Metronome, Follow, GridFeedback, Mute, Solo, FixedVelocity, SixteenVelocity, PadMode, KeyboardMode, ScaleToggle, Events, Restart, Loop, Erase, Volume, Tempo, Browse, Step, Duplicate, Favorites, Pitch, Mod, Pattern, Scene }
sealed class MikroInput {
 readonly bool[] commandPads=new bool[16];
 readonly int[] held=new int[16];readonly int[] voices=new int[128];ulong buttons;int? encoder;bool arrowChord,gridHold;
 public event Action<int,int,bool> Note;
 public event Action<int,int> Pressure;
 public event Action<MikroTransport> Transport;
 public event Action<int> Bank;
 public event Action<int> SelectEvent;
 public bool NotesEnabled=true;
 public bool SelectOnly;public bool StepOnly=false;
 public event Action<int> StepPad;
 public bool PatternOnly,SceneOnly;public int PatternSelectable=65535;readonly bool[] patternPads=new bool[16];
 public event Action<int> PatternPad;
 public event Action<int> Edit;
 public event Action<bool> Modifier;
 public event Action<bool> Repeat;
 public event Action<bool> Grid;
 public bool RepeatHeld {get{return (buttons&(1UL<<17))!=0;}}
 public bool SwingHeld {get{return (buttons&(1UL<<4))!=0;}}
 public bool Select {get{return (buttons&(1UL<<36))!=0;}}
 public bool Shift {get{return (buttons&(1UL<<25))!=0;}}
 public bool Group {get{return (buttons&(1UL<<14))!=0;}}
 public event Action<int> Selected;
 public event Action<int> Strike;
 public event Action<int> Encoder;
 public event Action<int> RootEncoder;
 public event Action<int> Strip;
 int stripValue;
 public int[] KeyboardNotes;
 public int[] PreviewNotes;
 public int Pitch(int position){return KeyboardNotes==null?BaseNote+position:KeyboardNotes[position];}
 public int PreviewPitch(int position){return PreviewNotes==null?BaseNote+position:PreviewNotes[position];}
 public int SelectedNote=-1,FixedValue=127;public bool Fixed, Sixteen;
 public bool IsPadHeld(int pad){return held[pad]>=0;}
 public void SetMode(bool fixedMode,bool sixteenMode){ReleaseAll();Fixed=fixedMode;Sixteen=sixteenMode;}
 public static int StepVelocity(int position){return 1+(position*126+7)/15;}
 public int BaseNote=36;
 public MikroInput(){for(int i=0;i<held.Length;i++)held[i]=-1;}
 public static int PadPosition(int hardwarePad){
  if(hardwarePad<0 || hardwarePad>=16)throw new ArgumentOutOfRangeException("hardwarePad");
  return (3-hardwarePad/4)*4+hardwarePad%4;
 }
 public static int Velocity(int raw){return Math.Max(1,Math.Min(127,raw>>5));}
 public bool IsNoteHeld(int note){return note>=0 && note<128 && voices[note]>0;}
 public void Feed(byte[] report){
  if(report==null || (report.Length!=64 && report.Length!=128))return;
  if(BaseNote<0 || BaseNote>112)throw new InvalidOperationException("BaseNote must be 0..112");
  for(int offset=0;offset<report.Length;offset+=64){
   if(report[offset]==1){
    ulong current=0;for(int b=0;b<5;b++)current|=(ulong)report[offset+1+b]<<(8*b);
    ulong down=current & ~buttons;bool oldShift=Shift,oldSelect=Select,oldRepeat=RepeatHeld,oldGroup=Group;buttons=current;
    bool patternSelection=PatternOnly && !SceneOnly && (Select || oldSelect);
    bool nextGrid=Shift && (current&(1UL<<21))!=0 && !((StepOnly || PatternOnly && !SceneOnly) && Select);
    if(gridHold && !nextGrid){gridHold=false;if(Grid!=null)Grid(false);}
    if(oldRepeat!=RepeatHeld && Repeat!=null && !patternSelection)Repeat(RepeatHeld);
    int strip=report[offset+10]|(report[offset+11]<<8);
    if(strip<=200 && strip!=stripValue){stripValue=strip;if(Strip!=null && !patternSelection)Strip(strip);}
    if(patternSelection && Select && !oldSelect)ReleasePatternPads();
    if(StepOnly && oldSelect && !Select)EndStepPreview();
    if(((oldShift && !oldGroup)!=(Shift && !Group) || (StepOnly || PatternOnly && !SceneOnly) && oldSelect!=Select) && Modifier!=null)Modifier(Shift && !Group);
    int position=report[offset+7]&15;
    bool previewTransition=StepOnly && (Select || oldSelect);
    if(encoder.HasValue && (!previewTransition || StepOnly && Select && oldSelect)){int delta=(position-encoder.Value+16)%16;if(delta!=0 && delta!=8){var turn=(current&(1UL<<25))!=0?RootEncoder:Encoder;if(turn!=null)turn(delta>8?delta-16:delta);}}
    encoder=position;
    if(Group && previewTransition){NavigateArrows(current,down);continue;}
    if(previewTransition){if(Select && (down&(1UL<<39))!=0 && Transport!=null)Transport(MikroTransport.ScaleToggle);arrowChord=(current&((1UL<<8)|(1UL<<9)))!=0;continue;}
    if(patternSelection){NavigateArrows(current,down);continue;}
    if(nextGrid && !gridHold && (oldShift!=Shift || (down&(1UL<<21))!=0)){gridHold=true;if(Grid!=null)Grid(true);}
    if((down&(1UL<<2))!=0 && Transport!=null)Transport(MikroTransport.Browse);
    if((down&(1UL<<30))!=0 && Transport!=null)Transport(MikroTransport.Step);
    if((down&(1UL<<32))!=0 && Transport!=null)Transport(MikroTransport.Pattern);
    if((down&(1UL<<31))!=0 && Transport!=null)Transport(MikroTransport.Scene);
    if((down&(1UL<<35))!=0 && Transport!=null)Transport(MikroTransport.Duplicate);
    if((down&(1UL<<5))!=0 && Transport!=null)Transport(MikroTransport.Tempo);
    if((down&(1UL<<3))!=0 && Transport!=null)Transport(MikroTransport.Volume);
    if((down&(1UL<<18))!=0 && Transport!=null)Transport(Shift?MikroTransport.Loop:MikroTransport.Restart);
    if((down&(1UL<<19))!=0 && Transport!=null)Transport(MikroTransport.Erase);
    if((down&(1UL<<1))!=0 && Transport!=null)Transport(MikroTransport.Favorites);
    if((down&(1UL<<10))!=0 && Transport!=null)Transport(MikroTransport.Pitch);
    if((down&(1UL<<11))!=0 && Transport!=null)Transport(MikroTransport.Mod);
    if((down&(1UL<<33))!=0 && Transport!=null)Transport(MikroTransport.Events);
    if((down&(1UL<<27))!=0 && Transport!=null)Transport(MikroTransport.PadMode);
    if((down&(1UL<<28))!=0 && Transport!=null)Transport(MikroTransport.KeyboardMode);
    if((down&(1UL<<39))!=0 && Transport!=null)Transport(MikroTransport.ScaleToggle);
    if((down&(1UL<<26))!=0 && Transport!=null)Transport((current&(1UL<<25))!=0?MikroTransport.SixteenVelocity:MikroTransport.FixedVelocity);
    if((down&(1UL<<37))!=0 && Transport!=null)Transport(MikroTransport.Solo);
    if((down&(1UL<<38))!=0 && Transport!=null)Transport(MikroTransport.Mute);
    if((down&(1UL<<21))!=0 && !Shift && Transport!=null)Transport(MikroTransport.Follow);
    if((down&(1UL<<20))!=0 && Transport!=null)Transport((current&(1UL<<25))!=0?MikroTransport.Metronome:MikroTransport.TapTempo);
    NavigateArrows(current,down);
    if((down&(1UL<<22))!=0 && Transport!=null)Transport(MikroTransport.Play);
    if((down&(1UL<<24))!=0 && Transport!=null)Transport(MikroTransport.Stop);
    if((down&(1UL<<23))!=0 && Transport!=null)Transport(MikroTransport.Record);
   }else if(report[offset]==2){
    for(int i=offset+1;i+2<offset+64;i+=3){
     int pad=report[i],kind=report[i+1]&0xf0,value=((report[i+1]&15)<<8)|report[i+2];
     if(pad==0 && kind==0 && value==0)break;
     if(pad>=16)continue;
     if(kind==0x30 && patternPads[pad]){patternPads[pad]=false;if(PatternPad!=null)PatternPad(PadPosition(pad)+16);continue;}
     if(patternPads[pad])continue;
     if(kind==0x30 && commandPads[pad]){commandPads[pad]=false;continue;}
     if(commandPads[pad])continue;
     if(kind==0x10 && held[pad]<0){
      if(SceneOnly){commandPads[pad]=true;int scenePosition=PadPosition(pad);if(scenePosition%4==3 && PatternPad!=null)PatternPad(40+3-scenePosition/4);continue;}
      if(PatternOnly){int launchPosition=PadPosition(pad);if(Select){commandPads[pad]=true;if((PatternSelectable&(1<<launchPosition))!=0 && PatternPad!=null)PatternPad(48+launchPosition);continue;}if(Shift && launchPosition>=4){commandPads[pad]=true;continue;}patternPads[pad]=true;if(PatternPad!=null)PatternPad(launchPosition+(Shift?32:0));continue;}
      if(Shift && !(StepOnly && Select)){commandPads[pad]=true;if(Edit!=null)Edit(PadPosition(pad)+1);continue;}
      if(!NotesEnabled){commandPads[pad]=true;continue;}
      if(StepOnly && !Select){commandPads[pad]=true;if(StepPad!=null)StepPad(PadPosition(pad));continue;}
      if(SelectOnly && !StepOnly){commandPads[pad]=true;int pitch=Pitch(PadPosition(pad));if(pitch>=0 && SelectEvent!=null)SelectEvent(pitch);continue;}
      int position=PadPosition(pad),note=StepOnly?PreviewPitch(position):Sixteen?SelectedNote:Pitch(position);
      if(note<0 || note>127)continue;
      held[pad]=note;
      if(!Sixteen){SelectedNote=note;if(Selected!=null)Selected(note);}
      if(Strike!=null)Strike(pad);
      int velocity=Sixteen?StepVelocity(position):Fixed?FixedValue:Velocity(value);
      if(Sixteen && voices[note]>0 && Note!=null)Note(note,0,false);
      bool first=voices[note]++==0;
      if((first || Sixteen) && Note!=null)Note(note,velocity,true);
     }else if(kind==0x30 && held[pad]>=0){
      int note=held[pad];held[pad]=-1;
      if(--voices[note]==0 && Note!=null)Note(note,0,false);
     }
     else if(kind==0x40 && held[pad]>=0 && !Fixed && !Sixteen && Pressure!=null)Pressure(held[pad],Velocity(value));
     // Pressure updates an existing voice; it never creates a strike or releases a pad.
    }
   }
  }
 }
 void EndStepPreview(){
  CancelHeldNotes();
 }
 public void CancelHeldNotes(){
  // Consume cancelled pads until physical release, including repeated down reports.
  for(int pad=0;pad<16;pad++)if(held[pad]>=0){held[pad]=-1;commandPads[pad]=true;}
  for(int note=0;note<128;note++)if(voices[note]>0){voices[note]=0;if(Note!=null)Note(note,0,false);}
 }
 void NavigateArrows(ulong current,ulong down){
  ulong arrows=current&((1UL<<8)|(1UL<<9));
  if(arrows==((1UL<<8)|(1UL<<9))){if(!arrowChord && Bank!=null)Bank(0);arrowChord=true;}
  else if(arrows==0)arrowChord=false;
  else if(!arrowChord){
   if((down&(1UL<<8))!=0 && Bank!=null)Bank(-1);
   if((down&(1UL<<9))!=0 && Bank!=null)Bank(1);
  }
 }
 public void ReleasePatternPads(){
  for(int pad=0;pad<16;pad++)if(patternPads[pad]){patternPads[pad]=false;commandPads[pad]=true;if(PatternPad!=null)PatternPad(PadPosition(pad)+16);}
 }
 public void ReleaseAll(){
  for(int pad=0;pad<16;pad++){held[pad]=-1;commandPads[pad]=false;}
  ReleasePatternPads();
  for(int note=0;note<128;note++)if(voices[note]>0){voices[note]=0;if(Note!=null)Note(note,0,false);}
 }
 public void Reset(){ReleaseAll();if(gridHold){gridHold=false;if(Grid!=null)Grid(false);}if(Shift && Modifier!=null)Modifier(false);if(RepeatHeld && Repeat!=null)Repeat(false);buttons=0;encoder=null;arrowChord=false;stripValue=0;}
}
