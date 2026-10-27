using System;
using System.Net;
using System.Globalization;
using System.Text;
using System.Diagnostics;

sealed partial class AbletonIntegration {
 public void Tick(string controllerPath,bool hostRunning,bool blocked){
  if(disposed)return;
  host=hostRunning;if(!hostRunning){startupShown=false;startupUntil=0;}
  if(controllerPath==null || path!=null && path!=controllerPath)startupShown=false;
  int packets=0;while(socket.Available>0 && packets++<32){IPEndPoint sender=null;byte[] data=socket.Receive(ref sender);if(data.Length>512)continue;
   int newMask,p,r,metro=0,following=0,selected=-1,mute=0,soloed=0,mix=0,color=11,baseNote=36;long epoch=0;string[] fields=Encoding.ASCII.GetString(data).Split(' ');
   if(fields[0]=="MO1"){NiOwnerState parsed;if(!bundledOwnership && NiOwnerState.TryParse(fields,out parsed)){niOwner=parsed;niOwnerAt=clock.ElapsedMilliseconds;}continue;}
   if(fields.Length==5 && fields[0]=="MPC1" && fields[4]=="ready"){
    long source;int duplicate,erase;
    if(long.TryParse(fields[1],out source) && source>=0 && int.TryParse(fields[2],out duplicate) && (duplicate==0||duplicate==1) && int.TryParse(fields[3],out erase) && (erase==0||erase==1)){clipEditEpoch=source;clipDuplicate=duplicate!=0;clipErase=erase!=0;}continue;
   }
   if(fields.Length==6 && fields[0]=="MG1" && fields[5]=="ready"){
    long source;int available,enabled;
    if(long.TryParse(fields[1],out source) && source>=0 && int.TryParse(fields[2],out available) && (available==0||available==1) && int.TryParse(fields[3],out enabled) && (enabled==0||enabled==1) && enabled<=available && Array.IndexOf(new[]{"1/4","1/4T","1/8","1/8T","1/16","1/16T","1/32","1/32T","Custom"},fields[4])>=0){gridEpoch=source;gridAvailable=available!=0;gridText=fields[4];}continue;
   }
   if(fields.Length==5 && fields[0]=="MN1" && fields[4]=="ready"){
    long source;int notes,canMute;
    if(long.TryParse(fields[1],out source) && source>=0 && int.TryParse(fields[2],out notes) && (notes==0||notes==1) && int.TryParse(fields[3],out canMute) && (canMute==0||canMute==1)){noteTargetEpoch=source;targetNotes=notes!=0;targetMute=canMute!=0;}continue;
   }
   if(fields.Length==4 && fields[0]=="MD1" && fields[3]=="ready"){
    long source;try{string name=Encoding.UTF8.GetString(Convert.FromBase64String(fields[2]));if(long.TryParse(fields[1],out source) && source>=0 && name.Length>0 && name.Length<=128 && name.IndexOfAny(new[]{'\r','\n','\0'})<0){trackNameEpoch=source;trackName=name;}}catch(FormatException){}continue;
   }
   if((fields.Length==6 && fields[0]=="MR1" || fields.Length==7 && fields[0]=="MR2") && fields[fields.Length-1]=="ready"){
    long source;double bpm,beat;int running;double age=0;
    if(fields[0]=="MR2"){
     double sampled;if(!double.TryParse(fields[5],NumberStyles.Float,CultureInfo.InvariantCulture,out sampled) || double.IsNaN(sampled) || double.IsInfinity(sampled))continue;
     age=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency-sampled;if(age<0 || age>.5)continue;
    }
    if(long.TryParse(fields[1],out source) && source==feedbackEpoch && double.TryParse(fields[2],NumberStyles.Float,CultureInfo.InvariantCulture,out bpm) && bpm>=20 && bpm<=999 && double.TryParse(fields[3],NumberStyles.Float,CultureInfo.InvariantCulture,out beat) && !double.IsNaN(beat) && !double.IsInfinity(beat) && int.TryParse(fields[4],out running) && (running==0||running==1)){
     repeat.Sync(bpm,beat,running!=0,age);repeatClockAt=clock.ElapsedMilliseconds;
    }continue;
   }
   if(fields.Length==3 && fields[0]=="MBK1"){
    long source;int key;
    if(InputAllowed && host && !blocked && BrowserMode && long.TryParse(fields[1],out source) && source==feedbackEpoch && int.TryParse(fields[2],out key) && LiveBrowserKeys.Allowed(key))browserKeyWrite(key);
    continue;
   }
   if(fields[0]=="MS1"){StepState parsed;if(StepState.TryParse(fields,out parsed))steps=parsed;continue;}
   if(fields[0]=="MPT1" || fields[0]=="MPT2" || fields[0]=="MPT3" || fields[0]=="MPT4" || fields[0]=="MPT5"){PatternState parsed;if(PatternState.TryParse(fields,out parsed))pattern=parsed;continue;}
   if(fields.Length==4 && fields[0]=="MB1" && fields[3]=="ready"){
    long source;int enabled;if(long.TryParse(fields[1],out source) && int.TryParse(fields[2],out enabled) && (enabled==0||enabled==1)){if(browserEpoch!=source || browserEnabled!=(enabled!=0))browserKeys.NewSession();browserEpoch=source;browserEnabled=enabled!=0;}continue;
   }
   if(fields.Length==5 && fields[0]=="MCU1" && fields[4]=="ready"){
    long source;int enabled;double bpm;
    if(long.TryParse(fields[1],out source) && int.TryParse(fields[2],out enabled) && (enabled==0||enabled==1) && double.TryParse(fields[3],NumberStyles.Float,CultureInfo.InvariantCulture,out bpm) && bpm>=20 && bpm<=999){tempoEpoch=source;tempoEnabled=enabled!=0;tempoText=bpm.ToString("0.##",CultureInfo.InvariantCulture)+" BPM";}continue;
   }
   if(fields.Length==8 && fields[0]=="MX1" && fields[7]=="ready"){
    long source;int available,enabled,left,right;
    if(long.TryParse(fields[1],out source) && int.TryParse(fields[2],out available) && (available==0||available==1)
     && int.TryParse(fields[3],out enabled) && (enabled==0||enabled==1) && enabled<=available
     && int.TryParse(fields[4],out left) && (left==0||left==1) && int.TryParse(fields[5],out right) && (right==0||right==1)){
     try{string label=Encoding.UTF8.GetString(Convert.FromBase64String(fields[6]));if(label.Length<=80 && label.IndexOfAny(new[]{'\r','\n','\0'})<0){mixerEpoch=source;volumeAvailable=available!=0;volumeEnabled=enabled!=0;trackLeft=left!=0;trackRight=right!=0;volumeText=label;}}catch(FormatException){}
    }continue;
   }
   if(fields[0]=="MNAV1"){NavigationState parsed;if(NavigationState.TryParse(fields,out parsed))navigationState=parsed;continue;}
   if(fields[0]=="MDEV1" || fields[0]=="MDEV2"){
    DeviceState parsed;if(DeviceState.TryParse(fields,out parsed)){
     if(parsed.Enabled && (!DeviceMode || deviceState.Name!=parsed.Name))deviceTitleUntil=clock.ElapsedMilliseconds+900;
     deviceState=parsed;
    }continue;
   }
   if((fields.Length==7 && fields[0]=="MV1" || fields.Length==8 && fields[0]=="MV2") && fields[fields.Length-1]=="ready"){
    long source;int available,enabled,present,picks,erase=0;
    if(long.TryParse(fields[1],out source) && source>=0 && int.TryParse(fields[2],out available) && (available==0 || available==1)
     && int.TryParse(fields[3],out enabled) && (enabled==0 || enabled==1) && enabled<=available
     && int.TryParse(fields[4],out present) && present>=0 && present<=65535 && int.TryParse(fields[5],out picks) && picks>=0 && picks<=65535 && (fields.Length==7 || int.TryParse(fields[6],out erase) && (erase==0||erase==1))){
     eventsEpoch=source;eventsAvailable=available!=0;eventsEnabled=enabled!=0;eventsPresent=present;eventsSelected=picks;eventsErase=erase!=0;
    }continue;
   }
   if(fields.Length==4 && fields[0]=="MT1" && fields[3]=="ready"){
    long source;int loop;if(long.TryParse(fields[1],out source) && int.TryParse(fields[2],out loop) && (loop==0||loop==1)){loopEpoch=source;loopEnabled=loop!=0;}continue;
   }
   if(fields.Length==4 && fields[0]=="ME1" && fields[3]=="ready"){
    long editSource;int commands;if(long.TryParse(fields[1],out editSource) && int.TryParse(fields[2],out commands) && commands>=0 && commands<=65535){editEpoch=editSource;pendingEditMask=commands;}continue;
   }
   if(fields[0]=="MK1" || fields[0]=="MK2"){KeyboardState parsed;if(KeyboardState.TryParse(fields,out parsed))pendingKeyboard=parsed;continue;}
   int activityNote;long activityEpoch;
   if(fields.Length==3 && fields[0]=="MP1" && long.TryParse(fields[1],out activityEpoch) && int.TryParse(fields[2],out activityNote) && activityNote>=0 && activityNote<128){
    if(active && host && !blocked && activityEpoch==feedbackEpoch){Latch(playbackFlashUntil,activityNote);if(!feedbackSeen){feedbackSeen=true;log("Ableton pad activity received");}}
    continue;
   }
   bool bundled=fields.Length==17 && fields[0]=="MC8";
   NiOwnerState bundledOwner=null;
   bool ownerValid=!bundled || NiOwnerState.TryParse(new[]{"MO1",fields[6],fields[13],fields[14],fields[15],"ready"},out bundledOwner);
   bool modern=(fields.Length==6 && fields[0]=="MC2") || (fields.Length==7 && fields[0]=="MC3") || (fields.Length==8 && fields[0]=="MC4") || (fields.Length==9 && fields[0]=="MC5") || (fields.Length==10 && fields[0]=="MC6") || (fields.Length==14 && fields[0]=="MC7") || bundled;
   bool format=(fields.Length==5 && fields[0]=="MC1") || (modern && int.TryParse(fields[4],out color) && color>=1 && color<=17
    && (fields.Length<7 || int.TryParse(fields[5],out baseNote) && baseNote>=0 && baseNote<=112)
    && (fields.Length<8 || long.TryParse(fields[6],out epoch) && epoch>=0)
    && (fields.Length<9 || int.TryParse(fields[7],out metro) && (metro==0||metro==1))
    && (fields.Length<10 || int.TryParse(fields[8],out following) && (following==0||following==1))
    && ownerValid && (fields.Length<14 || int.TryParse(fields[9],out selected) && selected>=-1 && selected<=127
     && int.TryParse(fields[10],out mute) && (mute==0||mute==1) && int.TryParse(fields[11],out soloed) && (soloed==0||soloed==1)
     && int.TryParse(fields[12],out mix) && (mix==0||mix==1)));
   if(format && int.TryParse(fields[1],out newMask) && newMask>=0 && newMask<=65535 && int.TryParse(fields[2],out p) && int.TryParse(fields[3],out r) && (p==0||p==1) && (r==0||r==1) && fields[fields.Length-1]=="ready"){
    if(feedbackEpoch!=epoch){input.CancelHeldNotes();ResetRepeat();repeatClockAt=-10000;stripPosition=modPosition=0;input.KeyboardNotes=null;input.ReleaseAll();midi.ReleaseAll();input.Sixteen=false;pendingSelected=-1;selectionUntil=0;Array.Clear(flashUntil,0,flashUntil.Length);ClearPlayback();}
    if(playing && p==0)ClearPlayback();feedbackEpoch=epoch;
    if(bundled){bundledOwnership=true;niOwner=bundledOwner;niOwnerAt=clock.ElapsedMilliseconds;}
    if(pendingSelected==selected || clock.ElapsedMilliseconds>=selectionUntil){if(input.Sixteen && input.SelectedNote!=selected)input.ReleaseAll();input.SelectedNote=selected;pendingSelected=-1;selectionUntil=0;}
    if(input.SelectedNote<0 && input.Sixteen)input.SetMode(false,false);
    muted=mute!=0;solo=soloed!=0;mixAvailable=mix!=0;
    if(mask!=newMask || padColor!=color)log("Ableton assigned pad mask=0x"+newMask.ToString("X4")+" color="+color);mask=newMask;padColor=color;input.BaseNote=baseNote;playing=p!=0;recording=r!=0;metronome=metro!=0;follow=following!=0;stateAt=clock.ElapsedMilliseconds;
   }
  }
  editMask=editEpoch==feedbackEpoch?pendingEditMask:0;
  if(input.NotesEnabled!=NotesAvailable){input.CancelHeldNotes();ResetRepeat();input.ReleaseAll();if(midi.Ready)midi.ReleaseAll();input.NotesEnabled=NotesAvailable;input.Sixteen=false;ClearPlayback();Array.Clear(flashUntil,0,128);}
  if(input.PatternOnly!=PatternMode || input.SceneOnly!=SceneMode){input.ReleaseAll();input.Sixteen=false;input.PatternOnly=PatternMode;input.SceneOnly=SceneMode;}
  input.PatternSelectable=PatternMode?pattern.Selectable:0;
  if(input.StepOnly!=StepMode){input.ReleaseAll();input.Sixteen=false;input.StepOnly=StepMode;}
  bool selecting=eventsEpoch==feedbackEpoch && eventsEnabled;
  if(selecting!=input.SelectOnly){input.ReleaseAll();input.Sixteen=false;input.SelectOnly=selecting;}
  if(pendingKeyboard!=null && pendingKeyboard.Epoch==feedbackEpoch){
   bool switched=KeyboardMode!=pendingKeyboard.Enabled;
   keyboard=pendingKeyboard;pendingKeyboard=null;
   if(switched){input.CancelHeldNotes();ResetRepeat();input.Sixteen=false;ClearPlayback();Array.Clear(flashUntil,0,128);Array.Clear(padFlashUntil,0,16);}
   input.KeyboardNotes=KeyboardMode?keyboard.Notes:null;
  }
  midiConnection.Tick(clock.ElapsedMilliseconds);
  input.PreviewNotes=MelodicPreview?keyboard.Notes:null;
  bool next=host && !blocked && !modeChanged && controllerPath!=null && midi.Ready && clock.ElapsedMilliseconds-stateAt<2000;
  if(ni!=null){bool request=next && niOwner!=null && niOwner.Epoch==feedbackEpoch && clock.ElapsedMilliseconds-niOwnerAt<1000 && niOwner.CompanionRequested;if(!request && active)Deactivate(false);ni.Request(request,feedbackEpoch);next=request && ni.Granted;}
  if(!next){if(active)Deactivate(ni==null && !blocked);return;}
  if(!active || path!=controllerPath){Deactivate(false);path=controllerPath;active=true;lastFrame="";if(startupSplash && !startupShown){startupShown=true;startupUntil=clock.ElapsedMilliseconds+2000;}log("Ableton HID active");}
  externalInputs.Tick(clock.ElapsedMilliseconds);
  if(!RepeatAvailable && RepeatMode){input.CancelHeldNotes();ResetRepeat();}
  if((!ExpressionAvailable || DeviceMode) && (stripPosition!=0 || modPosition!=0)){midi.ResetExpression();stripPosition=modPosition=0;}
  if(BrowserMode && clock.ElapsedMilliseconds>=browserPollAt){browserPollAt=clock.ElapsedMilliseconds+100;if(browserKeys.Enabled)browserKeys.Post(257);}
  int browserResult;while(browserKeys.TryResult(out browserResult)){if(BrowserMode && browserResult==1)midi.BrowserClose();}
  foreach(int note in externalInputs.Drain())Latch(playbackFlashUntil,note);
  RenderPanel();
 }
}
