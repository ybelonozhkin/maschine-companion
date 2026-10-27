using System;
using System.Threading.Tasks;

sealed partial class AbletonIntegration {
 void RenderPanel(){
  if(!InputAllowed)return;
  string label=ScreenLabel(input.BaseNote);if(label!=lastScreenLabel){lastScreenLabel=label;lastFrame="";}
  long now=clock.ElapsedMilliseconds;
  int generated;while(generatedFlashes.TryDequeue(out generated))playbackFlashUntil[generated]=now+50;
  var frame=new byte[81];frame[0]=0x80;
  for(int pad=0;pad<16;pad++){
   int index=MikroInput.PadPosition(pad);
   if(PatternMode){
    if(PatternSelection){if((pattern.Selectable&(1<<index))!=0){int selectionColor=pattern.Colors[index];frame[40+pad]=(byte)(((selectionColor==0?17:selectionColor)<<2)|(pattern.Selected==index?3:0));}continue;}
    if(input.Shift && !SceneMode){if(index<4 && pattern.Track+index<pattern.Tracks)frame[40+pad]=(byte)(4+((pattern.TrackPlaying&(1<<index))!=0?3:0));continue;}
    int clipColor=pattern.Colors[index];if(clipColor!=0){bool queued=(pattern.Triggered&(1<<index))!=0;bool lit=queued?(now/250)%2==0:(pattern.Playing&(1<<index))!=0;bool rec=(pattern.Recording&(1<<index))!=0;frame[40+pad]=(byte)(((rec?1:clipColor)<<2)|(lit?3:pattern.Selected==index?2:0));}continue;
   }
   if(input.Shift && !StepPreview){if((editMask&(1<<index))!=0)frame[40+pad]=(byte)((17<<2)|(editFlash[index]>now?3:0));continue;}
   if(!NotesAvailable)continue;
   if(StepMode && !StepPreview){
    if((steps.Valid&(1<<index))!=0){bool hit=(steps.Occupied&(1<<index))!=0;frame[40+pad]=(byte)(((steps.Cursor==index?17:steps.Color)<<2)|(steps.Cursor==index?3:hit?2:0));}continue;
   }
   int note=StepPreview?input.PreviewPitch(index):input.Sixteen?input.SelectedNote:input.Pitch(index);
   if(note<0)continue;
   if(input.SelectOnly){
    bool picked=(eventsSelected&(1<<index))!=0;
    bool sounding=playbackFlashUntil[note]>now;
    if((eventsPresent&(1<<index))!=0)frame[40+pad]=(byte)((padColor<<2)|(sounding?(picked?3:2):picked?2:0));continue;
   }
   bool selected=note==input.SelectedNote;
   bool pressed=(input.Sixteen?(!ArpMode && input.IsPadHeld(pad)) || padFlashUntil[pad]>now:(!ArpMode && input.IsNoteHeld(note)) || flashUntil[note]>now) || playbackFlashUntil[note]>now;
   bool root=(KeyboardMode && !StepPreview || StepPreview && MelodicPreview) && note%12==keyboard.Root;
   int color=root?(padColor==17?11:17):padColor;
   if(pressed || selected || input.Sixteen || KeyboardMode && !StepPreview || (mask&(1<<index))!=0)frame[40+pad]=(byte)((color<<2)|(input.Sixteen?(pressed?3:index/4):pressed?(selected?3:2):selected?2:0));
  }
  if(StepMode && !DeviceMode){for(int i=0;i<25;i++){bool page=i>=steps.StripStart && i<steps.StripEnd;frame[56+i]=(byte)(((page?17:steps.Color)<<2)|(i==steps.Strip?3:page?1:0));}}
  if(DeviceStripMode){int end=(deviceState.Position*24+63)/127;for(int i=0;i<25;i++)frame[56+i]=(byte)(i<=end?71:0);}
  if(ExpressionAvailable && !DeviceMode){
   frame[11]=(byte)(stripMode==1?127:124);frame[12]=(byte)(stripMode==2?127:124);
   if(stripMode!=0){int position=stripMode==1?(stripPosition==0?12:(stripPosition-1)*24/199):(modPosition==0?0:(modPosition-1)*24/199);int anchor=stripMode==1?12:0;
    for(int i=0;i<25;i++)frame[56+i]=(byte)(i==anchor || i==position?71:i>=Math.Min(anchor,position)&&i<=Math.Max(anchor,position)?70:0);
   }
  }
  frame[2]=(byte)(BrowserMode?(browserKeys.Favorite?127:124):0);
  frame[18]=(byte)(RepeatAvailable?(input.Shift?((ArpMode?repeat.Latch:padRepeatPinned && RepeatMode)?127:124):RepeatMode?127:124):0);frame[5]=(byte)(RepeatMode && !ArpMode?(input.SwingHeld?127:124):0);
  frame[3]=(byte)(BrowserMode && !GridMode?0x7f:0x7c);
  frame[33]=(byte)(PatternMode && !SceneMode?0x7f:0x7c);frame[32]=(byte)(SceneMode?0x7f:0x7c);
  frame[35]=(byte)(NavigationMode?127:input.Shift?124:0);
  frame[31]=(byte)(steps!=null && steps.Epoch==feedbackEpoch && steps.Available?(StepMode && !EncoderOverlay?0x7f:0x7c):0);
  frame[36]=(byte)(PatternMode && !SceneMode?clipEditEpoch==feedbackEpoch && clipDuplicate?0x7c:0:StepMode && steps.Duplicate?0x7c:0);
  frame[19]=(byte)(loopEpoch==feedbackEpoch && loopEnabled?0x7f:0x7c);
  frame[20]=(byte)(PatternMode && !SceneMode?clipEditEpoch==feedbackEpoch && clipErase?0x7c:0:input.SelectOnly && eventsErase?0x7c:0);
  frame[34]=(byte)(eventsEpoch==feedbackEpoch && eventsAvailable?(input.SelectOnly && !EncoderOverlay?0x7f:0x7c):0);
  frame[37]=(byte)(PatternMode && !SceneMode?(input.Select?0x7f:0x7c):StepMode?0x7c:0);
  frame[26]=(byte)(input.Shift?0x7f:0x7c);
  frame[7]=(byte)(deviceState!=null && deviceState.Epoch==feedbackEpoch && deviceState.Available?(DeviceMode && !GridMode?127:124):0);
  frame[6]=(byte)(TempoMode && !GridMode?0x7f:0x7c);
  frame[4]=(byte)(mixerEpoch==feedbackEpoch && volumeAvailable?(VolumeMode && !GridMode?0x7f:0x7c):0);
  frame[28]=(byte)(PatternMode || KeyboardMode || EncoderOverlay || StepMode || input.SelectOnly?0x7c:0x7f);frame[29]=(byte)(KeyboardMode && !PatternMode && !EncoderOverlay && !StepMode && !input.SelectOnly && !ArpFocus?0x7f:0x7c);
  frame[27]=(byte)(!NotesAvailable || velocityDarkUntil>now?0:input.Fixed || input.Sixteen?0x7f:0x7c);
  frame[38]=(byte)(mixAvailable?(solo?0x7f:0x7c):0);frame[39]=(byte)(MuteAvailable?(muted?0x7f:0x7c):0);
  frame[22]=(byte)(GridMode?0x7f:input.Shift?(gridEpoch==feedbackEpoch && gridAvailable?0x7c:0):follow?0x7f:0x7c);
  frame[21]=(byte)(metronome?0x7f:0x7c);
  frame[23]=(byte)(playing?0x7f:0x7c);frame[24]=(byte)(recording?0x7f:0x7c);frame[25]=(byte)(playing?0x7f:0x7c);
  bool left,right;Navigation(out left,out right);
  if(PatternMode)frame[27]=0;
  frame[15]=(byte)(input.Group?0x7f:0x7c);
  frame[9]=(byte)(left?0x7c:0);frame[10]=(byte)(right?0x7c:0);
  if(input.Group && input.Shift)frame[10]=0x7f;
  if(PatternSelection){for(int i=1;i<40;i++)if(i!=9 && i!=10)frame[i]=0;frame[33]=frame[37]=0x7f;}
  if(StepPreview){for(int i=1;i<40;i++)frame[i]=0;frame[37]=0x7f;frame[MelodicPreview?29:28]=0x7f;}
  if(NavigationMode && !StepPreview){frame[35]=127;frame[9]=(byte)(left?124:0);frame[10]=(byte)(right?124:0);}
  frame[15]=(byte)(input.Group?0x7f:0x7c);
  if(input.Group){frame[9]=(byte)(left?0x7c:0);frame[10]=(byte)(right?(input.Shift?0x7f:0x7c):0);}
  string key=(clock.ElapsedMilliseconds<startupUntil?"startup:":"")+Convert.ToBase64String(frame)+":"+input.BaseNote+":"+input.Fixed+":"+input.FixedValue+":"+input.Sixteen+":"+(input.Sixteen?input.SelectedNote:-1)+":"+KeyboardLabel()+":"+input.SelectOnly+":"+(VolumeMode?volumeText:"")+":"+VolumeMode+":"+TempoMode+":"+(TempoMode?tempoText:"")+":"+BrowserMode+":"+GridMode+":"+(GridMode?gridText:"")+":"+(DeviceMode?DeviceLabel():"")+":"+(NavigationMode?NavigationLabel():"")+":"+NotesAvailable+":"+StepPreview+":"+(RepeatMode?repeat.Label:"")+":"+(StepMode?StepLabel():"")+":"+(PatternMode?PatternLabel():"");if(key!=lastFrame){lastFrame=key;QueuePanel(path,frame,input.BaseNote);}
 }
 string TrackHeader(){return trackNameEpoch==feedbackEpoch?trackName:"";}
 string StepLabel(){return "STEP PAGE\n"+BankScreen.Mark((steps.Page+1)+"/"+steps.Pages);}
 string PatternLabel(){
  if(SceneMode)return "SCENES\n"+BankScreen.Mark((pattern.Scene+1)+"-"+(pattern.Scene+4));
  string title=input.Shift?"TRACKS":"SCENES";
  string value=!input.Shift?(pattern.Scene+1)+"-"+(pattern.Scene+4):(pattern.Track+1)+"-"+Math.Min(pattern.Track+4,pattern.Tracks);
  return title+"\n"+BankScreen.Mark(value);
 }
 string KeyboardLabel(){
  if(!KeyboardMode)return "";
  string note=keyboard.Notes[0]>=0?BankScreen.NoteName(keyboard.Notes[0]):"--";
  string scale=BankScreen.ScaleName(keyboard.Name);
  if(keyboardPage==2)return "SCALE MODE\n"+BankScreen.Mark(keyboard.Scale?"ON":"OFF");
  return keyboardPage==1?(keyboard.Scale?"SCALE":"SCALE (OFF)")+"\n"+BankScreen.Mark(scale):"ROOT NOTE\n"+BankScreen.Mark(note);
 }
 string ScreenLabel(int baseNote){
  return ParameterLabel(baseNote);
 }
 string DeviceLabel(){return clock.ElapsedMilliseconds<deviceTitleUntil?"DEVICE\n"+BankScreen.Mark(deviceState.Name):deviceState.Parameter+"\n"+BankScreen.Mark(deviceState.Value);}
 string NavigationLabel(){return (navigationState.Page==0?"POSITION":"MARKER")+"\n"+BankScreen.Mark(navigationState.Label);}
 string ParameterLabel(int baseNote){
  if(baseNote<0)return "";
  if(input.Group)return (TrackHeader().Length>0?TrackHeader():"TRACK")+"\n ";
  if(GridMode)return "GRID\n"+BankScreen.Mark(gridText);
  if(NavigationMode && !StepPreview)return NavigationLabel();
  if(DeviceMode && !StepPreview)return DeviceLabel();
  if(PatternMode)return PatternLabel();
  if(ArpFocus)return repeat.ArpParameter=="RHYTHM"?BankScreen.Rhythm(repeat.ArpRhythmNumber,repeat.ArpRhythmMask):"ARP "+repeat.ArpParameter+"\n"+BankScreen.Mark(repeat.ArpValue);
  if(RepeatMode && !ArpMode){string[] lines=repeat.Label.Split('\n');bool swing=repeatPage==1 || input.SwingHeld;return (swing?"REPEAT SWING":"REPEAT RATE")+"\n"+BankScreen.Mark(swing?lines[2].Replace("SWING ",""):lines[1]);}
  if(StepPreview)return MelodicPreview?"ROOT NOTE\n"+BankScreen.Mark(keyboard.Notes[0]>=0?BankScreen.NoteName(keyboard.Notes[0]):"--"):"PAD BANK\n"+BankScreen.Mark(BankScreen.Range(baseNote));
  if(BrowserMode)return "BROWSER\n ";
  if(TempoMode)return "TEMPO\n"+BankScreen.Mark(tempoText);
  if(VolumeMode)return "VOLUME\n"+BankScreen.Mark(volumeText);
  if(StepMode)return StepLabel();
  if(input.SelectOnly)return "EVENTS\n"+BankScreen.Mark("NOTE SELECT");
  if(input.Sixteen){if(KeyboardMode)return KeyboardLabel();return "16 VEL BANK\n"+BankScreen.Mark(BankScreen.Range(baseNote));}
  if(!NotesAvailable)return "PAD MODE\n ";
  if(KeyboardMode)return KeyboardLabel();
  return "PAD BANK\n"+BankScreen.Mark(BankScreen.Range(baseNote));
 }
 void QueuePanel(string target,byte[] frame,int baseNote=-1){
  lock(panelGate){pendingPath=target;pending=frame;pendingLabel=baseNote>=0 && clock.ElapsedMilliseconds<startupUntil?BankScreen.StartupSplash:ScreenLabel(baseNote);
   if(panelTask==null || panelTask.IsCompleted)panelTask=Task.Factory.StartNew(WritePanels);
  }
 }
 void WritePanels(){
  long outputGeneration=-1;
  for(;;){byte[] frame;string target,label;long generation;
   lock(panelGate){if(pending==null){panelTask=null;return;}frame=pending;target=pendingPath;label=pendingLabel;generation=panelGeneration;pending=null;}
   try{
    if(outputGeneration!=generation){panelOutput.Reset();outputGeneration=generation;}
    // One fresh ownership snapshot per batch; pause can cancel between reports.
    if(NativeBlocked()){panelOutput.Reset();continue;}
    bool complete=panelOutput.Send(target,frame,label,panelWrite,()=>{lock(panelGate)return generation==panelGeneration && LeaseAllowed;});
    if(complete)lock(panelGate){if(generation==panelGeneration)owned=frame[23]!=0;}
   }catch(Exception ex){panelOutput.Reset();log("Ableton LED output: "+ex.Message);}
  }
 }
}
