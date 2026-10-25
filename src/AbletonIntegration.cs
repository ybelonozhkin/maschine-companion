using System;
using System.Net;
using System.Globalization;
using System.Windows.Automation;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Text;

sealed class AbletonIntegration : IDisposable {
 readonly INiOwnership ni;
 NiOwnerState niOwner;long niOwnerAt=-10000;
 bool LeaseAllowed {get{return ni==null || ni.Granted;}}
 bool InputAllowed {get{return active && LeaseAllowed;}}
 bool PanelIdle {get{lock(panelGate)return panelTask==null || panelTask.IsCompleted;}}
 NavigationState navigationState;
 bool NavigationMode {get{return navigationState!=null && navigationState.Epoch==feedbackEpoch && navigationState.Enabled;}}
 DeviceState deviceState;long deviceTitleUntil;
 bool DeviceMode {get{return !NavigationMode && deviceState!=null && deviceState.Epoch==feedbackEpoch && deviceState.Enabled;}}
 long gridEpoch=-1,noteTargetEpoch=-1;bool gridAvailable,gridHeld,targetNotes=true,targetMute=true;string gridText="1/16";
 bool GridMode {get{return gridHeld && gridEpoch==feedbackEpoch && gridAvailable;}}
 bool NotesAvailable {get{return noteTargetEpoch!=feedbackEpoch || targetNotes;}}
 bool MuteAvailable {get{return mixAvailable && (noteTargetEpoch!=feedbackEpoch || targetMute);}}
 long eventsEpoch=-1;bool eventsAvailable,eventsEnabled,eventsErase;long loopEpoch=-1;bool loopEnabled;int eventsPresent,eventsSelected;
 int editMask,pendingEditMask;long editEpoch=-1;readonly long[] editFlash=new long[16];
 long mixerEpoch=-1;bool volumeAvailable,volumeEnabled,trackLeft,trackRight;string volumeText="";
 bool VolumeMode {get{return !NavigationMode && mixerEpoch==feedbackEpoch && volumeEnabled;}}
 long tempoEpoch=-1;bool tempoEnabled;string tempoText="";
 bool TempoMode {get{return !NavigationMode && tempoEpoch==feedbackEpoch && tempoEnabled;}}
 long browserEpoch=-1;bool browserEnabled;
 bool BrowserMode {get{return !NavigationMode && browserEpoch==feedbackEpoch && browserEnabled;}}
 PatternState pattern;
 bool PatternMode {get{return pattern!=null && pattern.Epoch==feedbackEpoch && pattern.Enabled;}}
 bool SceneMode {get{return PatternMode && pattern.SceneMode;}}
 int stripMode,stripPosition,modPosition;long browserPollAt;
 bool ExpressionAvailable {get{return active && NotesAvailable && !PatternMode && !StepMode && !input.SelectOnly && !BrowserMode;}}
 StepState steps;
 bool StepMode {get{return steps!=null && steps.Epoch==feedbackEpoch && steps.Enabled;}}
 bool StepPreview {get{return StepMode && input.Select;}}
 bool MelodicPreview {get{return KeyboardMode;}}
 bool PatternSelection {get{return PatternMode && !SceneMode && input.Select;}}
 bool EncoderOverlay {get{return VolumeMode || TempoMode || BrowserMode || GridMode || DeviceMode || NavigationMode;}}
 KeyboardState keyboard,pendingKeyboard;
 bool KeyboardMode {get{return keyboard!=null && keyboard.Epoch==feedbackEpoch && keyboard.Enabled;}}
 long trackNameEpoch=-1;string trackName="",lastScreenLabel="";
 readonly System.Windows.Forms.Timer flashTimer=new System.Windows.Forms.Timer();
 public bool FastRefreshActive {get{return flashTimer.Enabled;}}
 readonly NativeMidi midi=new NativeMidi();readonly MikroInput input=new MikroInput();readonly Action<string> log;
 readonly NoteRepeat repeat;long repeatClockAt=-10000;
 bool RepeatAvailable {get{return ExpressionAvailable && clock.ElapsedMilliseconds-repeatClockAt<1500;}}
 bool RepeatMode {get{return repeat.Enabled;}}
 bool arpView,padRepeatPinned;
 int keyboardPage,repeatPage;
 bool ArpMode {get{return repeat.Arpeggiator;}}
 bool ArpFocus {get{return ArpMode && arpView && !EncoderOverlay;}}
 readonly ConcurrentQueue<int> generatedFlashes=new ConcurrentQueue<int>();
 readonly MidiActivityInputs externalInputs;
 readonly Action<string,byte[]> panelWrite;readonly Action<int> browserKeyWrite;readonly BrowserKeyOutput browserKeys;
 readonly bool startupSplash;bool startupShown;long startupUntil;
 readonly UdpClient socket;readonly Stopwatch clock=Stopwatch.StartNew();
 long stateAt=-10000,nextOpen,velocityDarkUntil,selectionUntil;int mask,padColor=11;int pendingSelected=-1;bool muted,solo,mixAvailable;bool playing,recording,metronome,follow,host,modeChanged;volatile bool active;string path,lastFrame="";string error="";
 readonly long[] padFlashUntil=new long[16];
 readonly long[] flashUntil=new long[128];
 readonly long[] playbackFlashUntil=new long[128];long feedbackEpoch=-1;bool feedbackSeen;
 readonly object panelGate=new object();byte[] pending;string pendingPath;string pendingLabel;Task panelTask;bool owned;long panelGeneration;readonly PanelOutput panelOutput=new PanelOutput();
 public string Status {get{return error.Length>0?"Ableton unavailable: "+error:modeChanged?"Paused after MIDI mode: return to native, then Resume":!midi.Ready?"Waiting for MIDI port":ni!=null && !InputAllowed?(niOwner==null || clock.ElapsedMilliseconds-niOwnerAt>=1000?"Waiting for controller ownership state":niOwner.Uncertain?"Paused: unknown device identity":niOwner.Selected?(niOwner.Count>1?"Maschine selected: choose its Controller manually":"Maschine VST selected"):ni.Status):!active?"Waiting for Ableton control surface":"Ableton: notes + transport";}}
 public int FixedVelocity {get{return input.FixedValue;}set{if(value<1 || value>127)throw new ArgumentOutOfRangeException("FixedVelocity");input.FixedValue=value;}}
 public bool IsWriting {get{return !PanelIdle || ni!=null && ni.Busy;}}
 public bool OwnsController {get{return InputAllowed && !modeChanged;}}
 public bool PausedForMidiMode {get{return modeChanged;}}
 public AbletonIntegration(Action<string> logger,Action<string,byte[]> writer=null,int statePort=39473,string activityInputs="",int fixedVelocity=127,Action<int> browserKeyWriter=null,bool showStartupSplash=true,bool nativeHandoff=false,INiOwnership ownership=null){
  if(nativeHandoff)ni=ownership ?? new NiControllerSession(logger,()=>PanelIdle);
  flashTimer.Interval=15;flashTimer.Start();flashTimer.Stop();flashTimer.Tick+=(sender,args)=>{RenderPanel();if(!FlashesActive(clock.ElapsedMilliseconds))flashTimer.Stop();};
  FixedVelocity=fixedVelocity;startupSplash=showStartupSplash;
  log=logger;panelWrite=writer ?? StandbyLight.Frame;browserKeys=new BrowserKeyOutput(()=>InputAllowed && host && BrowserMode && !modeChanged,logger);browserKeys.Enabled=browserKeyWriter==null;browserKeyWrite=browserKeyWriter ?? browserKeys.Post;
  repeat=new NoteRepeat((n,v,d)=>{if(!d || InputAllowed)midi.Note(n,v,d);},logger);
  repeat.Generated+=note=>generatedFlashes.Enqueue(note);
  externalInputs=new MidiActivityInputs(activityInputs,logger);
  socket=new UdpClient(new IPEndPoint(IPAddress.Loopback,statePort));socket.Client.Blocking=false;
  input.Note+=(n,v,d)=>{if(active){repeat.Note(n,v,d);if(d && !input.Sixteen)Latch(flashUntil,n);}};input.Transport+=Control;
  input.Pressure+=(n,v)=>{if(active)repeat.Pressure(n,v);};
  input.Repeat+=held=>{
   if(held && !input.Shift && DeviceMode)midi.DeviceClose();
   if(held && input.Shift){
    if(active && !input.Group){
     if(ArpMode)repeat.SetLatch(!repeat.Latch);
     else if(!KeyboardMode && RepeatAvailable){padRepeatPinned=!(padRepeatPinned && RepeatMode);repeat.SetEnabled(padRepeatPinned);}
    }
    return;
   }
   if(ArpMode){if(held){if(ArpFocus){input.CancelHeldNotes();ResetRepeat();}else{arpView=true;if(VolumeMode || TempoMode)midi.Transport(MikroTransport.KeyboardMode);}}return;}
   if(held && KeyboardMode && RepeatAvailable){repeat.SetArpeggiator(true);arpView=true;if(VolumeMode || TempoMode)midi.Transport(MikroTransport.KeyboardMode);return;}
   if(held && padRepeatPinned){padRepeatPinned=false;repeat.SetEnabled(false);return;}
   if((!held || RepeatAvailable) && !padRepeatPinned)repeat.SetEnabled(held);
  };
  input.Grid+=held=>{if(held){if(active && gridEpoch==feedbackEpoch && gridAvailable){gridHeld=true;midi.GridHold(true);}}else{bool wasHeld=gridHeld;gridHeld=false;if(wasHeld && midi.Ready)midi.GridHold(false);}};
  input.Modifier+=shift=>{if(active)midi.Modifier(shift && !StepPreview && !PatternSelection);};
  input.Edit+=command=>{if(active){Latch(editFlash,command-1);midi.Edit(command);}};
  input.Strike+=pad=>{Latch(padFlashUntil,pad);};
  input.Selected+=n=>{if(active){pendingSelected=n;selectionUntil=clock.ElapsedMilliseconds+600;midi.SelectNote(n);}};
  input.StepPad+=position=>{if(active && StepMode)midi.StepPad(position);};
  input.PatternPad+=value=>{if(midi.Ready && (active || value>=16))midi.PatternPad(value);};
  input.SelectEvent+=pitch=>{if(active)midi.SelectEvent(pitch);};
  input.Strip+=value=>{if(ExpressionAvailable){if(stripMode==1){stripPosition=value;midi.PitchBend(StripPitch(value));}else if(stripMode==2 && value>0){modPosition=value;midi.Modulation((value-1)*127/199);}}};
  input.Encoder+=delta=>TurnEncoder(delta,false);
  input.RootEncoder+=delta=>TurnEncoder(delta,true);
  input.Bank+=Navigate;
  try{midi.Create();}catch(Exception ex){error=ex.Message;log("Ableton MIDI setup: "+error);}
 }
 void TurnEncoder(int delta,bool shift){
  if(!active || input.Group)return;
  if(StepPreview)midi.StepSelectTurn(delta);
  else if(GridMode)midi.GridTurn(delta);
  else if(NavigationMode)midi.NavigationTurn(delta,shift);
  else if(DeviceMode){deviceTitleUntil=0;midi.DeviceTurn(delta,shift);}
  else if(PatternMode)midi.PatternTurn(delta,!SceneMode && shift);
  else if(ArpFocus)repeat.TurnArp(delta,false);
  else if(RepeatMode && !ArpMode)repeat.Turn(delta,repeatPage==1 || input.SwingHeld);
  else if(BrowserMode)midi.BrowserTurn(delta*(shift?6:1));
  else if(TempoMode)midi.TempoTurn(delta,shift);
  else if(VolumeMode)midi.VolumeTurn(delta,shift);
  else if(StepMode){if(!shift)midi.StepTurn(delta);}
  else if(input.SelectOnly)midi.EventTurn(delta,shift);
  else if(KeyboardMode){if(keyboardPage==2)midi.ScaleMode(delta>0);else if(keyboardPage==1)midi.ScaleTypeTurn(delta);else if(shift)midi.OctaveTurn(delta);else midi.ScaleTurn(delta);}
  else if(NotesAvailable && !shift)midi.PadBankTurn(delta);
 }
 void Navigation(out bool left,out bool right){
  left=right=false;
  if(input.Group){left=mixerEpoch==feedbackEpoch && trackLeft;right=input.Shift || mixerEpoch==feedbackEpoch && trackRight;return;}
  if(StepPreview || GridMode)return;
  if(NavigationMode){left=navigationState.Page>0;right=navigationState.Page<1;return;}
  if(DeviceMode){left=input.Shift?deviceState.Previous:deviceState.Left;right=input.Shift?deviceState.Next:deviceState.Right;return;}
  if(PatternMode)return;
  if(ArpFocus){left=repeat.ArpLeft;right=repeat.ArpRight;return;}
  if(RepeatMode && !ArpMode){left=repeatPage>0;right=repeatPage<1;return;}
  if(!EncoderOverlay && !StepMode && !input.SelectOnly && KeyboardMode){left=keyboardPage>0;right=keyboardPage<2;}
 }
 void Navigate(int direction){
  if(!active || direction==0)return;
  bool left,right;Navigation(out left,out right);if(direction<0?!left:!right)return;
  if(input.Group){input.CancelHeldNotes();ResetRepeat();midi.ResetExpression();if(PatternMode)input.ReleasePatternPads();if(direction>0 && input.Shift)midi.CreateTrack();else midi.Track(direction);return;}
  if(NavigationMode && !GridMode){midi.NavigationPage(direction);return;}
  if(DeviceMode){deviceTitleUntil=input.Shift?clock.ElapsedMilliseconds+900:0;midi.DeviceNavigate(direction,input.Shift);return;}
  if(ArpFocus){repeat.TurnArp(direction,true);return;}
  if(RepeatMode && !ArpMode){repeatPage=Math.Max(0,Math.Min(1,repeatPage+direction));return;}
  if(KeyboardMode)keyboardPage=Math.Max(0,Math.Min(2,keyboardPage+direction));
 }
 void ResetRepeat(){repeat.Reset();arpView=padRepeatPinned=false;int ignored;while(generatedFlashes.TryDequeue(out ignored)){} }
 void Control(MikroTransport command){
  if(!active)return;
  if(command==MikroTransport.ViewToggle){midi.ViewToggle();return;}
  if(command==MikroTransport.Navigate){midi.NavigationToggle();return;}
  if(command==MikroTransport.ScaleToggle && NavigationMode)return;
  if(ArpMode){
   if(command==MikroTransport.KeyboardMode)arpView=false;
   if(command==MikroTransport.PadMode || command==MikroTransport.Step || command==MikroTransport.Events || command==MikroTransport.Browse || command==MikroTransport.Pattern || command==MikroTransport.Scene){input.CancelHeldNotes();ResetRepeat();}
  }
  if(command==MikroTransport.Pattern || command==MikroTransport.Scene){ResetRepeat();input.ReleaseAll();midi.ResetExpression();if(command==MikroTransport.Scene)midi.SceneToggle();else midi.PatternToggle();return;}
  if(PatternMode && (command==MikroTransport.ScaleToggle || command==MikroTransport.FixedVelocity || command==MikroTransport.SixteenVelocity))return;
  if(command==MikroTransport.Favorites){if(BrowserMode)midi.BrowserFavorite();return;}
  if(command==MikroTransport.Pitch || command==MikroTransport.Mod){if(ExpressionAvailable){midi.ResetExpression();stripPosition=modPosition=0;int selected=command==MikroTransport.Pitch?1:2;stripMode=stripMode==selected?0:selected;}return;}
  if(command==MikroTransport.Duplicate && !StepMode)return;
  if(command==MikroTransport.ScaleToggle && (GridMode || DeviceMode))return;
  if(command==MikroTransport.ScaleToggle && BrowserMode){midi.BrowserPress(input.Shift);return;}
  if(command==MikroTransport.ScaleToggle && StepPreview){midi.StepSelectReset();return;}
  if(command==MikroTransport.ScaleToggle && StepMode && !EncoderOverlay)return;
  if(command==MikroTransport.Erase && !input.SelectOnly)return;
  if(command==MikroTransport.ScaleToggle && VolumeMode){midi.VolumeReset();return;}
  if(command==MikroTransport.ScaleToggle && TempoMode)return;
  if(command==MikroTransport.ScaleToggle){if(!input.SelectOnly && !(KeyboardMode && input.Shift))midi.Bank(0);return;}
  if(command==MikroTransport.GridFeedback)return;
  if(!NotesAvailable && (command==MikroTransport.FixedVelocity || command==MikroTransport.SixteenVelocity))return;
  if(command==MikroTransport.FixedVelocity){input.SetMode(!input.Fixed,false);return;}
  if(command==MikroTransport.SixteenVelocity){
   if(input.SelectedNote<0){velocityDarkUntil=clock.ElapsedMilliseconds+200;return;}
   input.SetMode(false,!input.Sixteen);return;
  }
  if(command==MikroTransport.Mute && !MuteAvailable || command==MikroTransport.Solo && !mixAvailable)return;
  midi.Transport(command);
 }
 public static bool NativeBlocked(){
  var processes=Process.GetProcesses();
  try{foreach(var process in processes){
   string name;try{name=process.ProcessName;}catch(InvalidOperationException){continue;}catch(System.ComponentModel.Win32Exception){continue;}
   foreach(string blocked in NativeProcessNames)if(string.Equals(name,blocked,StringComparison.OrdinalIgnoreCase))return true;
  }return false;}finally{foreach(var process in processes)process.Dispose();}
 }
 static readonly string[] NativeProcessNames={"Maschine 3","Maschine3","Maschine 2","Maschine2","Controller Editor","Komplete Kontrol"};
 public void Tick(string controllerPath,bool hostRunning,bool blocked){
  host=hostRunning;if(!hostRunning){startupShown=false;startupUntil=0;}
  if(controllerPath==null || path!=null && path!=controllerPath)startupShown=false;
  int packets=0;while(socket.Available>0 && packets++<32){IPEndPoint sender=null;byte[] data=socket.Receive(ref sender);if(data.Length>512)continue;
   int newMask,p,r,metro=0,following=0,selected=-1,mute=0,soloed=0,mix=0,color=11,baseNote=36;long epoch=0;string[] fields=Encoding.ASCII.GetString(data).Split(' ');
   if(fields[0]=="MO1"){NiOwnerState parsed;if(NiOwnerState.TryParse(fields,out parsed)){niOwner=parsed;niOwnerAt=clock.ElapsedMilliseconds;}continue;}
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
   if(fields[0]=="MDEV1"){
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
   bool modern=(fields.Length==6 && fields[0]=="MC2") || (fields.Length==7 && fields[0]=="MC3") || (fields.Length==8 && fields[0]=="MC4") || (fields.Length==9 && fields[0]=="MC5") || (fields.Length==10 && fields[0]=="MC6") || (fields.Length==14 && fields[0]=="MC7");
   bool format=(fields.Length==5 && fields[0]=="MC1") || (modern && int.TryParse(fields[4],out color) && color>=1 && color<=17
    && (fields.Length<7 || int.TryParse(fields[5],out baseNote) && baseNote>=0 && baseNote<=112)
    && (fields.Length<8 || long.TryParse(fields[6],out epoch) && epoch>=0)
    && (fields.Length<9 || int.TryParse(fields[7],out metro) && (metro==0||metro==1))
    && (fields.Length<10 || int.TryParse(fields[8],out following) && (following==0||following==1))
    && (fields.Length<14 || int.TryParse(fields[9],out selected) && selected>=-1 && selected<=127
     && int.TryParse(fields[10],out mute) && (mute==0||mute==1) && int.TryParse(fields[11],out soloed) && (soloed==0||soloed==1)
     && int.TryParse(fields[12],out mix) && (mix==0||mix==1)));
   if(format && int.TryParse(fields[1],out newMask) && newMask>=0 && newMask<=65535 && int.TryParse(fields[2],out p) && int.TryParse(fields[3],out r) && (p==0||p==1) && (r==0||r==1) && fields[fields.Length-1]=="ready"){
    if(feedbackEpoch!=epoch){input.CancelHeldNotes();ResetRepeat();repeatClockAt=-10000;stripPosition=modPosition=0;input.KeyboardNotes=null;input.ReleaseAll();midi.ReleaseAll();input.Sixteen=false;pendingSelected=-1;selectionUntil=0;Array.Clear(flashUntil,0,flashUntil.Length);ClearPlayback();}
    if(playing && p==0)ClearPlayback();feedbackEpoch=epoch;
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
  if(!midi.Ready && error.Length==0 && clock.ElapsedMilliseconds>=nextOpen){nextOpen=clock.ElapsedMilliseconds+500;try{midi.TryOpen();}catch(Exception ex){error=ex.Message;log(error);}}
  input.PreviewNotes=MelodicPreview?keyboard.Notes:null;
  bool next=host && !blocked && !modeChanged && controllerPath!=null && midi.Ready && clock.ElapsedMilliseconds-stateAt<2000;
  if(ni!=null){bool request=next && niOwner!=null && niOwner.Epoch==feedbackEpoch && clock.ElapsedMilliseconds-niOwnerAt<1000 && niOwner.CompanionRequested;if(!request && active)Deactivate(false);ni.Request(request,feedbackEpoch);next=request && ni.Granted;}
  if(!next){if(active)Deactivate(ni==null && !blocked);return;}
  if(!active || path!=controllerPath){Deactivate(false);path=controllerPath;active=true;lastFrame="";if(startupSplash && !startupShown){startupShown=true;startupUntil=clock.ElapsedMilliseconds+2000;}log("Ableton HID active");}
  externalInputs.Tick(clock.ElapsedMilliseconds);
  if(!RepeatAvailable && RepeatMode){input.CancelHeldNotes();ResetRepeat();}
  if(!ExpressionAvailable && (stripPosition!=0 || modPosition!=0)){midi.ResetExpression();stripPosition=modPosition=0;}
  if(BrowserMode && clock.ElapsedMilliseconds>=browserPollAt){browserPollAt=clock.ElapsedMilliseconds+100;if(browserKeys.Enabled)browserKeys.Post(257);}
  int browserResult;while(browserKeys.TryResult(out browserResult)){if(BrowserMode && browserResult==1)midi.BrowserClose();}
  foreach(int note in externalInputs.Drain())Latch(playbackFlashUntil,note);
  RenderPanel();
 }
 public static int StripPitch(int raw){return raw==0?8192:raw<=100?(raw-1)*8192/99:8192+(raw-101)*8191/99;}
 void Latch(long[] flashes,int index){flashTimer.Start();flashes[index]=clock.ElapsedMilliseconds+50;}
 bool FlashesActive(long now){
  if(!active)return false;
  if(ArpMode && repeat.HasHeldNotes || !generatedFlashes.IsEmpty)return true;
  for(int i=0;i<128;i++)if(flashUntil[i]>now || playbackFlashUntil[i]>now)return true;
  for(int i=0;i<16;i++)if(padFlashUntil[i]>now || editFlash[i]>now)return true;
  return false;
 }
 void ClearPlayback(){Array.Clear(playbackFlashUntil,0,playbackFlashUntil.Length);}
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
  if(StepMode){for(int i=0;i<25;i++){bool page=i>=steps.StripStart && i<steps.StripEnd;frame[56+i]=(byte)(((page?17:steps.Color)<<2)|(i==steps.Strip?3:page?1:0));}}
  if(ExpressionAvailable){
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
  frame[36]=(byte)(StepMode && steps.Duplicate?0x7c:0);
  frame[19]=(byte)(loopEpoch==feedbackEpoch && loopEnabled?0x7f:0x7c);
  frame[20]=(byte)(input.SelectOnly && eventsErase?0x7c:0);
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
 public bool Feed(string controllerPath,byte[] report){
  if(!host)return false;
  if(InputAllowed && path==controllerPath){
   if(report.Length>=5 && report[0]==1 && (report[1]&1)!=0 && (report[4]&2)!=0){modeChanged=true;Pause(true);log("Ableton paused after MIDI-mode shortcut");}
   else try{input.Feed(report);RenderPanel();}catch(Exception ex){error=ex.Message;Pause(true);log("Ableton input stopped: "+error);}
  }
  return true;
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
 public void Pause(bool clear){
  if(ni!=null)ni.Reset();Deactivate(clear && ni==null);
 }
 void Deactivate(bool clear){
  startupUntil=0;flashTimer.Stop();Array.Clear(editFlash,0,16);
  bool wasActive=active;active=false;
  ResetRepeat();
  stripPosition=modPosition=0;browserKeys.NewSession();
  bool hadShift=input.Shift;input.Reset();try{if(hadShift && midi.Ready)midi.Modifier(false);midi.ReleaseAll();}catch(Exception ex){log("Ableton note release: "+ex.Message);}
  Array.Clear(padFlashUntil,0,padFlashUntil.Length);
  Array.Clear(flashUntil,0,flashUntil.Length);
  ClearPlayback();
  externalInputs.Pause();
  lock(panelGate){panelGeneration++;pending=null;if(clear && (owned || wasActive) && path!=null){var blank=new byte[81];blank[0]=0x80;QueuePanel(path,blank);}owned=false;}
  lastFrame="";
 }
 public void ResetState(){stateAt=niOwnerAt=-10000;int drained=0;while(socket.Available>0 && drained++<256){IPEndPoint sender=null;socket.Receive(ref sender);}}
 public void Resume(){modeChanged=false;ResetState();}
 public void Dispose(){
  Pause(true);Task task;lock(panelGate){task=panelTask;}if(task!=null)task.Wait(1500);
  if(ni!=null)ni.Dispose();
  repeat.Dispose();browserKeys.Dispose();flashTimer.Dispose();socket.Close();midi.Dispose();
 }
}

sealed class NiOwnerState {
 public long Epoch;public bool Selected,Uncertain;public int Count;
 public bool CompanionRequested {get{return !Uncertain && !Selected;}}
 public static bool TryParse(string[] fields,out NiOwnerState state){
  state=null;long epoch;int selected,count,uncertain;
  if(fields.Length!=6 || fields[0]!="MO1" || fields[5]!="ready" || !long.TryParse(fields[1],out epoch) || epoch<0 || !int.TryParse(fields[2],out selected) || selected<0 || selected>1 || !int.TryParse(fields[3],out count) || count<0 || count>512 || selected>count || !int.TryParse(fields[4],out uncertain) || uncertain<0 || uncertain>1)return false;
  state=new NiOwnerState{Epoch=epoch,Selected=selected!=0,Count=count,Uncertain=uncertain!=0};return true;
 }
}

sealed class NavigationState {
 public long Epoch;public bool Enabled;public int Page;public string Label;
 public static bool TryParse(string[] fields,out NavigationState state){
  state=null;if(fields.Length!=6 || fields[0]!="MNAV1" || fields[5]!="ready")return false;
  long epoch;int enabled,page;
  if(!long.TryParse(fields[1],out epoch) || epoch<0 || !int.TryParse(fields[2],out enabled) || enabled<0 || enabled>1 || !int.TryParse(fields[3],out page) || page<0 || page>1)return false;
  string label;try{label=fields[4]=="-"?"":new UTF8Encoding(false,true).GetString(Convert.FromBase64String(fields[4]));}catch(FormatException){return false;}catch(DecoderFallbackException){return false;}
  if(label.Length>80)return false;foreach(char c in label)if(char.IsControl(c))return false;
  state=new NavigationState{Epoch=epoch,Enabled=enabled!=0,Page=page,Label=label};return true;
 }
}
sealed class DeviceState {
 public long Epoch;public bool Available,Enabled,Left,Right,Previous,Next;public string Name,Parameter,Value;
 public static bool TryParse(string[] fields,out DeviceState state){
  state=null;if(fields.Length!=12 || fields[0]!="MDEV1" || fields[11]!="ready")return false;
  long epoch;if(!long.TryParse(fields[1],out epoch) || epoch<0)return false;
  var flags=new int[6];for(int i=0;i<flags.Length;i++)if(!int.TryParse(fields[i+2],out flags[i]) || flags[i]<0 || flags[i]>1)return false;
  if(flags[1]>flags[0])return false;
  var labels=new string[3];try{for(int i=0;i<labels.Length;i++){labels[i]=fields[i+8]=="-"?"":new UTF8Encoding(false,true).GetString(Convert.FromBase64String(fields[i+8]));if(labels[i].Length>80 || labels[i].IndexOfAny(new[]{'\r','\n','\0','\u0001','\u0002','\u0003','\u0004'})>=0)return false;}}catch(FormatException){return false;}catch(DecoderFallbackException){return false;}
  state=new DeviceState{Epoch=epoch,Available=flags[0]!=0,Enabled=flags[1]!=0,Left=flags[2]!=0,Right=flags[3]!=0,Previous=flags[4]!=0,Next=flags[5]!=0,Name=labels[0],Parameter=labels[1],Value=labels[2]};return true;
 }
}
sealed class PatternState {
 public long Epoch;public bool Enabled,Left,Right,SceneMode;public int Track,Scene,Playing,Triggered,Recording,Tracks,TrackPlaying,Selected=-1,Selectable;public int[] Colors;
 public static bool TryParse(string[] f,out PatternState state){
  state=null;long epoch;bool selectableFormat=f.Length==31 && f[0]=="MPT5";bool selectionFormat=f.Length==30 && f[0]=="MPT4" || selectableFormat;bool sceneFormat=f.Length==29 && f[0]=="MPT3" || selectionFormat;bool modern=f.Length==29 && f[0]=="MPT2" || sceneFormat;
  if(!(modern || f.Length==28 && f[0]=="MPT1") || f[f.Length-1]!="ready" || !long.TryParse(f[1],out epoch) || epoch<0)return false;
  int[] v=new int[25];for(int i=0;i<25;i++)if(!int.TryParse(f[i+2],out v[i]))return false;
  if(v[0]<0||v[0]>(sceneFormat?2:1)||v[1]<0||v[2]<0||v[3]<0||v[3]>1||v[4]<0||v[4]>1||v[5]<0||v[5]>65535||v[6]<0||v[6]>65535||v[7]<0||v[7]>65535||v[8]<0)return false;
  var colors=new int[16];for(int i=0;i<16;i++){colors[i]=v[i+9];if(colors[i]<0||colors[i]>17)return false;}
  int trackPlaying=0;if(modern){if(!int.TryParse(f[27],out trackPlaying) || trackPlaying<0 || trackPlaying>15)return false;}
  else for(int i=0;i<4;i++)if((v[5]&(0x1111<<i))!=0)trackPlaying|=1<<i;
  int selected=-1;if(selectionFormat && (!int.TryParse(f[28],out selected) || selected< -1 || selected>15))return false;
  int selectable=0;if(selectableFormat){if(!int.TryParse(f[29],out selectable) || selectable<0 || selectable>65535)return false;}else for(int i=0;i<16;i++)if(colors[i]!=0)selectable|=1<<i;
  state=new PatternState{Epoch=epoch,Enabled=v[0]!=0,SceneMode=sceneFormat && v[0]==2,Track=v[1],Scene=v[2],Left=v[3]!=0,Right=v[4]!=0,Playing=v[5],Triggered=v[6],Recording=v[7],Tracks=v[8],Colors=colors,TrackPlaying=trackPlaying,Selected=selected,Selectable=selectable};return true;
 }
}

sealed class StepState {
 public long Epoch;public bool Available,Enabled,Duplicate;public int Pitch,Page,Pages,Valid,Occupied,Cursor,Color,StripStart,StripEnd,Strip;
 public static bool TryParse(string[] f,out StepState state){
  state=null;long epoch;if(f.Length!=16 || f[0]!="MS1" || f[15]!="ready" || !long.TryParse(f[1],out epoch) || epoch<0)return false;
  int[] v=new int[13];for(int i=0;i<13;i++)if(!int.TryParse(f[i+2],out v[i]))return false;
  if(v[0]<0||v[0]>1||v[1]<0||v[1]>v[0]||v[2]<0||v[2]>127||v[3]<0||v[4]<1||v[3]>=v[4]||v[5]<0||v[5]>65535||v[6]<0||v[6]>65535||v[7]<-1||v[7]>15||v[8]<1||v[8]>17||v[9]<0||v[9]>24||v[10]<v[9]||v[10]>25||v[11]<-1||v[11]>24||v[12]<0||v[12]>1)return false;
  state=new StepState{Epoch=epoch,Available=v[0]!=0,Enabled=v[1]!=0,Pitch=v[2],Page=v[3],Pages=v[4],Valid=v[5],Occupied=v[6],Cursor=v[7],Color=v[8],StripStart=v[9],StripEnd=v[10],Strip=v[11],Duplicate=v[12]!=0};return true;
 }
}

sealed class KeyboardState {
 public long Epoch;public bool Enabled,Scale;public int Root,Start,Page=1;public int[] Notes;public string Name;
 public static bool TryParse(string[] fields,out KeyboardState state){
  state=null;long epoch;int mode,scale,root,start,page=1;
  if((fields.Length!=9 && fields.Length!=10) || fields[fields.Length-1]!="ready" || !long.TryParse(fields[1],out epoch) || epoch<0
   || !int.TryParse(fields[2],out mode) || (mode!=0 && mode!=1) || !int.TryParse(fields[3],out scale) || (scale!=0 && scale!=1)
   || !int.TryParse(fields[4],out root) || root<0 || root>11 || !int.TryParse(fields[5],out start) || start<0 || start>127)return false;
  if(fields.Length==10 && (!int.TryParse(fields[8],out page) || page<0 || page>2))return false;
  string[] pitches=fields[6].Split(',');if(pitches.Length!=16)return false;
  int[] notes=new int[16];int previous=-1;bool ended=false;
  for(int i=0;i<16;i++){
   int note;if(!int.TryParse(pitches[i],out note) || note< -1 || note>127)return false;
   if(note<0)ended=true;else{if(ended || note<start || note<=previous)return false;previous=note;}notes[i]=note;
  }
  string name;try{name=Encoding.UTF8.GetString(Convert.FromBase64String(fields[7]));}catch(FormatException){return false;}
  if(name.Length==0 || name.Length>80 || name.IndexOfAny(new[]{'\r','\n','\0'})>=0)return false;
  state=new KeyboardState{Epoch=epoch,Enabled=mode!=0,Scale=scale!=0,Root=root,Start=start,Page=page,Notes=notes,Name=name};return true;
 }
}

static class BankScreen {
 public const string StartupSplash="Mikro\nCompanion";
 static readonly string[] names={"C","C#","D","D#","E","F","F#","G","G#","A","A#","B"};
 public static string PitchName(int note){return names[note%12];}
 public static string NoteName(int note){return names[note%12]+(note/12-2);}
 public static string Range(int baseNote){return NoteName(baseNote)+" - "+NoteName(baseNote+15);}
 public static string ScaleName(string name){return name.Replace("Harmonic Minor","Harm.Min").Replace("Harmonic Major","Harm.Maj").Replace("Melodic Minor","Mel.Min").Replace("Minor Pentatonic","Min.Pent").Replace("Major Pentatonic","Maj.Pent").Replace("Pentatonic","Pent.").Replace("Harmonic","Harm.").Replace("Melodic","Mel.");}
 public static string Mark(string text){return "\u0001"+text+"\u0002";}
 public static string Rhythm(int number,int mask){
  if(number<1 || number>NoteRepeat.RhythmCount || mask<0 || mask>255)throw new ArgumentOutOfRangeException();
  return "RHYTHM "+number.ToString("00")+"\n\u0005"+mask.ToString("X2",CultureInfo.InvariantCulture);
 }
 public static string Cell(string text,bool inverse){return "\u0003"+(inverse?Mark(text):text)+"\u0004";}
 sealed class Run {public string Text;public bool Inverse;public float Fixed;}
 static List<Run> Runs(string text){
  var result=new List<Run>();bool inverse=false;
  for(int i=0;i<text.Length;){
   if(text[i]=='\u0001'){inverse=true;i++;continue;}if(text[i]=='\u0002'){inverse=false;i++;continue;}
   if(text[i]=='\u0003'){int end=text.IndexOf('\u0004',i+1);if(end<0)end=text.Length;string cell=text.Substring(i+1,end-i-1);result.Add(new Run{Text=cell.Replace("\u0001","").Replace("\u0002",""),Inverse=cell.IndexOf('\u0001')>=0,Fixed=40});i=Math.Min(text.Length,end+1);continue;}
   int start=i;while(i<text.Length && text[i]!='\u0001' && text[i]!='\u0002' && text[i]!='\u0003')i++;
   result.Add(new Run{Text=text.Substring(start,i-start),Inverse=inverse});
  }
  return result;
 }
 static float Width(Graphics g,Run run,Font font,StringFormat format){return run.Fixed>0?run.Fixed:g.MeasureString(run.Text,font,10000,format).Width+(run.Text.Trim().Length>0?4:0);}
 static void DrawValue(Graphics g,List<Run> runs,Font font,StringFormat format,float y){
  float x=1;
  foreach(var run in runs){
   if(x>=127)break;float width=Math.Min(127-x,Width(g,run,font,format));float padding=run.Text.Trim().Length>0?2:0;
   string text=Fit(g,run.Text,font,format,Math.Max(1,width-2*padding));
   if(run.Inverse)g.FillRectangle(Brushes.White,(float)Math.Floor(x),y,(float)Math.Ceiling(width),32-y);
   float left=run.Fixed>0?x+Math.Max(padding,(width-g.MeasureString(text,font,10000,format).Width)/2):x+padding;
   g.DrawString(text,font,run.Inverse?Brushes.Black:Brushes.White,left,y,format);x+=width;
  }
 }
 public static byte[] Report(int baseNote,bool bottom){return TextReport(baseNote<0?"":"PAD BANK\n"+Mark(Range(baseNote)),bottom);}
 internal static string Fit(Graphics graphics,string text,Font font,StringFormat format,float maxWidth=126){
  if(graphics.MeasureString(text,font,10000,format).Width<=maxWidth)return text;
  var elements=new List<string>();var iterator=StringInfo.GetTextElementEnumerator(text);
  while(iterator.MoveNext())elements.Add(iterator.GetTextElement());
  // Keep both ends, including distinguishing suffixes such as scale numbers.
  for(int count=elements.Count-1;count>=2;count--){
   int left=(count+1)/2,right=count/2;
   string candidate=string.Join("",elements.GetRange(0,left).ToArray())+".."+string.Join("",elements.GetRange(elements.Count-right,right).ToArray());
   if(graphics.MeasureString(candidate,font,10000,format).Width<=maxWidth)return candidate;
  }
  return graphics.MeasureString("..",font,10000,format).Width<=maxWidth?"..":"";
 }
 public static Bitmap Render(string text){
  var bitmap=new Bitmap(128,32);
  using(var graphics=Graphics.FromImage(bitmap))using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()){
   graphics.Clear(Color.Black);graphics.TextRenderingHint=TextRenderingHint.SingleBitPerPixelGridFit;
   format.FormatFlags|=StringFormatFlags.NoWrap|StringFormatFlags.MeasureTrailingSpaces;
   string[] lines=(text??"").Replace("\r","").Split('\n');
   if(text==null || text.Length==0)return bitmap;
   if(lines.Length==1){using(var font=new Font("Segoe UI",18,FontStyle.Bold,GraphicsUnit.Pixel))DrawValue(graphics,Runs(lines[0]),font,format,5);}
   else{
    graphics.SetClip(new Rectangle(0,0,128,16));
    using(var font=new Font("Segoe UI",15,FontStyle.Bold,GraphicsUnit.Pixel))graphics.DrawString(Fit(graphics,lines[0],font,format),font,Brushes.White,1,-2,format);
    // Keep every changing value pixel in the lower HID report, including inversion.
    graphics.SetClip(new Rectangle(0,16,128,16));
    string value=string.Join(" ",lines,1,lines.Length-1);byte rhythm;
    if(value.Length==3 && value[0]=='\u0005' && byte.TryParse(value.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out rhythm)){
     graphics.FillRectangle(Brushes.White,1,16,126,16);
     for(int step=0;step<8;step++){int x=5+step*15;if((rhythm&(1<<step))!=0)graphics.FillRectangle(Brushes.Black,x,20,10,8);else graphics.DrawRectangle(Pens.Black,x,20,9,7);}
    }else{
     var runs=Runs(value);
     using(var font=new Font("Segoe UI",15,FontStyle.Regular,GraphicsUnit.Pixel))DrawValue(graphics,runs,font,format,14);
    }
   }
  }
  return bitmap;
 }
 public static byte[] TextReport(string text,bool bottom){return TextReports(text)[bottom?1:0];}
 public static byte[][] TextReports(string text){
  var reports=new[]{LaunchScreen.Report(false,false),LaunchScreen.Report(false,true)};
  if(string.IsNullOrEmpty(text))return reports;
  using(var bitmap=Render(text))for(int y=0;y<32;y++)for(int x=0;x<128;x++)if(bitmap.GetPixel(x,y).R>127){int localY=y%16;int index=9+(localY/8)*128+x;reports[y/16][index]=(byte)(reports[y/16][index]&~(1<<(localY%8)));}
  return reports;
 }

}


sealed class PanelOutput {
 string path,label;byte[] leds;byte[][] screen;
 public void Reset(){path=label=null;leds=null;screen=null;}
 static bool Same(byte[] left,byte[] right){
  if(left==null || right==null || left.Length!=right.Length)return false;
  for(int i=0;i<left.Length;i++)if(left[i]!=right[i])return false;
  return true;
 }
 public bool Send(string target,byte[] frame,string text,Action<string,byte[]> write,Func<bool> allowed){
  if(path!=target)Reset();
  var next=label==text && screen!=null?screen:BankScreen.TextReports(text);
  try{
   var reports=new[]{frame,next[0],next[1]};var previous=new[]{leds,screen==null?null:screen[0],screen==null?null:screen[1]};
   // Finish this screen before accepting another label, even if one is queued.
   for(int i=0;i<reports.Length;i++)if(!Same(reports[i],previous[i])){
    if(!allowed()){Reset();return false;}
    write(target,reports[i]);
   }
   path=target;label=text;leds=(byte[])frame.Clone();screen=next;return true;
  }catch{Reset();throw;}
 }
}

static class LiveBrowserKeys {
 static void Report(string reason,Action<string> logger){if(reason!=lastReason){lastReason=reason;logger(reason);}}
 [StructLayout(LayoutKind.Sequential)]public struct Keyboard {public ushort key,scan;public uint flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Sequential)]public struct Mouse {public int x,y;public uint data,flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Explicit)]public struct Payload {[FieldOffset(0)]public Keyboard keyboard;[FieldOffset(0)]public Mouse mouse;}
 [StructLayout(LayoutKind.Sequential)]public struct Input {public uint type;public Payload data;}
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
 public enum BrowserFocus {None,Outside,Contents,Sidebar,Search}
 public static BrowserFocus FocusTarget(BrowserFocus area,int key){
  if(key==256)return BrowserFocus.Contents;
  if(key==37)return BrowserFocus.Sidebar;
  if(key==13 && area!=BrowserFocus.Contents)return BrowserFocus.Contents;
  return area==BrowserFocus.Contents || area==BrowserFocus.Sidebar || area==BrowserFocus.Outside?BrowserFocus.None:BrowserFocus.Contents;
 }
 static BrowserFocus FocusArea(AutomationElement element){
  BrowserFocus area=BrowserFocus.Outside;
  for(int i=0;element!=null && i<64;i++,element=TreeWalker.RawViewWalker.GetParent(element)){
   var current=element.Current;
   if(current.AutomationId.EndsWith("BrowserToolbar.SearchField",StringComparison.Ordinal))return BrowserFocus.Search;
   if(current.AutomationId.Contains(".SidebarSplitter.ContentPane"))return BrowserFocus.Contents;
   if(current.ControlType==ControlType.Tree)area=current.Name=="Browser Sidebar"?BrowserFocus.Sidebar:BrowserFocus.Contents;
   if(current.AutomationId=="ContentBrowser")return area;
  }return BrowserFocus.Outside;
 }
 static AutomationElement FindTree(AutomationElement browser,BrowserFocus area){
  var trees=browser.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Tree));
  foreach(AutomationElement tree in trees){bool sidebar=tree.Current.Name=="Browser Sidebar";if(sidebar==(area==BrowserFocus.Sidebar))return tree;}return null;
 }
 public static bool TargetAllowed(string processName,IntPtr mainWindow,IntPtr window){return window!=IntPtr.Zero && mainWindow==window && processName.StartsWith("Ableton Live ",StringComparison.OrdinalIgnoreCase);}
 public static bool IsJump(int key){return key>=961 && key<=1087 && key!=1024;}
 public static int JumpTarget(int selected,int count,int delta){return count<=0?-1:Math.Max(0,Math.Min(count-1,(selected<0?(delta>0?-1:count):selected)+delta));}
 public static bool Allowed(int key){return key==256 || key==13 || key==49 || key>=37 && key<=40 || IsJump(key);}
 [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 static AutomationElement Selected(AutomationElement tree){
  if(tree==null)return null;object pattern;
  if(tree.TryGetCurrentPattern(SelectionPattern.Pattern,out pattern)){var items=((SelectionPattern)pattern).Current.GetSelection();if(items.Length==1)return items[0];}
  return tree.FindFirst(TreeScope.Descendants,new PropertyCondition(SelectionItemPattern.IsSelectedProperty,true));
 }
 public enum ChildProbe {Cancelled,Empty,Populated}
 public static ChildProbe WaitForChildren(Func<bool> hasChildren,Func<bool> valid,Func<long> milliseconds,Action<int> delay){
  long started=milliseconds();
  for(;;){
   if(!valid())return ChildProbe.Cancelled;
   bool children=hasChildren();
   if(!valid())return ChildProbe.Cancelled;
   if(children)return ChildProbe.Populated;
   long remaining=500-(milliseconds()-started);
   if(remaining<=0)return ChildProbe.Empty;
   delay((int)Math.Min(25,remaining));
  }
 }
 static readonly Condition BrowserRow=new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TreeItem),new PropertyCondition(AutomationElement.IsSelectionItemPatternAvailableProperty,true));
 static bool HasChildRows(AutomationElement row){return row.FindFirst(TreeScope.Children,BrowserRow)!=null;}
 static bool InExpandedBranch(AutomationElement tree,AutomationElement row){
  for(var parent=TreeWalker.ControlViewWalker.GetParent(row);parent!=null;parent=TreeWalker.ControlViewWalker.GetParent(parent)){
   if(Automation.Compare(parent,tree))return true;
   object pattern;if(parent.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out pattern) && ((ExpandCollapsePattern)pattern).Current.ExpandCollapseState==ExpandCollapseState.Collapsed)return false;
  }return false;
 }
 static void JumpSelected(AutomationElement tree,int delta,Func<bool> ready,Action<string> logger){
  if(tree==null || !ready())return;
  var selected=Selected(tree);var rows=new List<AutomationElement>();int index=-1;
  foreach(AutomationElement row in tree.FindAll(TreeScope.Descendants,BrowserRow)){
   object selectable;if(!row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selectable) || !InExpandedBranch(tree,row))continue;
   if(selected!=null && Automation.Compare(row,selected))index=rows.Count;
   rows.Add(row);
  }
  // A selected row missing from the current snapshot is a stale tree, not the start of the list.
  if(selected!=null && index<0)return;
  int target=JumpTarget(index,rows.Count,delta);if(target<0 || target==index || !ready())return;
  var current=Selected(tree);
  if(selected!=null && (current==null || !Automation.Compare(selected,current)))return;
  object pattern;if(!rows[target].TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern))return;
  ((SelectionItemPattern)pattern).Select();
  if(ready() && rows[target].TryGetCurrentPattern(ScrollItemPattern.Pattern,out pattern))((ScrollItemPattern)pattern).ScrollIntoView();
  // SelectionItem.Select does not move keyboard focus. Right/Enter must act on this row too.
  if(ready())rows[target].SetFocus();
  logger("Browser jump: "+delta+" rows; selected "+(target+1)+"/"+rows.Count);
 }
 public static bool EmptyPluginCanLoad(string treeName,int parentRows){
  // In Live's grouped Plug-Ins list: creator -> device -> preset.
  // Do not apply this rule to Places, arbitrary folders or unknown layouts.
  return treeName!=null && treeName.StartsWith("Plug-Ins List",StringComparison.Ordinal) && parentRows==1;
 }
 static bool EmptyPluginCanLoad(AutomationElement tree,AutomationElement row){
  int parents=0;var parent=TreeWalker.ControlViewWalker.GetParent(row);
  for(int depth=0;parent!=null && depth<64;depth++,parent=TreeWalker.ControlViewWalker.GetParent(parent)){
   if(Automation.Compare(parent,tree))return EmptyPluginCanLoad(tree.Current.Name,parents);
   var type=parent.Current.ControlType;object selection;
   if(type==ControlType.DataItem || type==ControlType.TreeItem || parent.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection))parents++;
  }return false;
 }
 static string RowPath(AutomationElement tree,AutomationElement row){
  var names=new List<string>();
  for(int depth=0;row!=null && depth<64;depth++,row=TreeWalker.ControlViewWalker.GetParent(row)){
   if(Automation.Compare(row,tree)){names.Reverse();return string.Join("\u001f",names.ToArray());}
   object selection;var type=row.Current.ControlType;
   if(type==ControlType.DataItem || type==ControlType.TreeItem || row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection))names.Add(row.Current.Name);
  }return null;
 }
 sealed class RowAddress {
  public string Path;public int Occurrence;
  public static RowAddress Capture(AutomationElement tree,AutomationElement row){
   string path=RowPath(tree,row);if(path==null)return null;int occurrence=0;
   foreach(AutomationElement candidate in tree.FindAll(TreeScope.Descendants,BrowserRow)){
    if(RowPath(tree,candidate)!=path)continue;
    if(Automation.Compare(candidate,row))return new RowAddress{Path=path,Occurrence=occurrence};
    occurrence++;
   }return null;
  }
  public AutomationElement Resolve(AutomationElement tree){
   int occurrence=0;foreach(AutomationElement candidate in tree.FindAll(TreeScope.Descendants,BrowserRow))
    if(RowPath(tree,candidate)==Path && occurrence++==Occurrence)return candidate;
   return null;
  }
 }
 static bool PreserveExpandedSelection(AutomationElement tree,RowAddress address,Func<bool> ready,Action<string> logger){
  if(address==null || !ready())return false;
  var row=address.Resolve(tree);if(row==null)return false;
  var selected=Selected(tree);
  if(selected==null || !Automation.Compare(selected,row)){
   string previous=selected==null?"none":selected.Current.Name;object pattern;
   if(!ready() || !row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern))return false;
   ((SelectionItemPattern)pattern).Select();
   if(!ready())return false;row.SetFocus();
   logger("Browser expansion kept selection: "+previous+" -> "+row.Current.Name);
  }
  return true;
 }
 static void TraceBrowserSelection(AutomationElement tree,string stage,Action<string> logger){
  var row=Selected(tree);logger("Browser "+stage+": "+(row==null?"none":row.Current.Name+"; screen Y="+row.Current.BoundingRectangle.Y.ToString("0",CultureInfo.InvariantCulture)));
 }
 static bool ExpandSelected(AutomationElement tree,Func<bool> ready,Action<string> logger){
  var row=Selected(tree);if(row==null)return false;object pattern;
  Func<bool> valid=()=>{if(!ready())return false;var selected=Selected(tree);return selected!=null && Automation.Compare(row,selected);};
  if(row.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out pattern)){
   var expand=(ExpandCollapsePattern)pattern;var state=expand.Current.ExpandCollapseState;
   if(state==ExpandCollapseState.LeafNode)return false;
   if(!valid())return true;
   bool canLoadEmpty=EmptyPluginCanLoad(tree,row);
   var address=canLoadEmpty?RowAddress.Capture(tree,row):null;
   if(state==ExpandCollapseState.Expanded && HasChildRows(row)){SendKey(37);return true;}
   if(state!=ExpandCollapseState.Expanded){
    TraceBrowserSelection(tree,"before expand",logger);
    // Use Live's native tree navigation. The accessibility Expand() action changed
    // selection/scrolling in the tested Live version, unlike keyboard Right.
    if(!SendKey(39))return true;
    if(canLoadEmpty){
     var expandedAt=Stopwatch.StartNew();
     var populated=WaitForChildren(()=>{var current=address==null?null:address.Resolve(tree);return current!=null && HasChildRows(current);},ready,()=>expandedAt.ElapsedMilliseconds,System.Threading.Thread.Sleep);
     if(populated==ChildProbe.Cancelled || !PreserveExpandedSelection(tree,address,ready,logger))return true;
     logger("Browser expanded plugin: "+(address==null?"unknown":address.Path)+"; "+populated+"; elapsed ms="+expandedAt.ElapsedMilliseconds);
     TraceBrowserSelection(tree,"after expand",logger);
     return populated!=ChildProbe.Empty;
    }
    return true;
   }
   if(!canLoadEmpty)return true;
   var watch=Stopwatch.StartNew();
   var result=WaitForChildren(()=>HasChildRows(row),valid,()=>watch.ElapsedMilliseconds,System.Threading.Thread.Sleep);
   logger("Browser preset branch: "+row.Current.Name+"; "+result+"; elapsed ms="+watch.ElapsedMilliseconds);
   return result!=ChildProbe.Empty;
  }
  // Live versions without ExpandCollapsePattern still implement standard tree keys.
  if(EmptyPluginCanLoad(tree,row)){
   if(!valid())return true;
   if(HasChildRows(row)){SendKey(37);return true;}
   var address=RowAddress.Capture(tree,row);
   if(address==null)return true;
   if(!SendKey(39))return true;
   var watch=Stopwatch.StartNew();
   var result=WaitForChildren(()=>{var current=address==null?null:address.Resolve(tree);return current!=null && HasChildRows(current);},ready,()=>watch.ElapsedMilliseconds,System.Threading.Thread.Sleep);
   if(!PreserveExpandedSelection(tree,address,ready,logger))return true;
   logger("Browser preset branch: "+row.Current.Name+"; "+result+"; elapsed ms="+watch.ElapsedMilliseconds);
   return result!=ChildProbe.Empty;
  }
  int count=tree.FindAll(TreeScope.Descendants,Condition.TrueCondition).Count;
  if(!ready())return true;SendKey(39);
  System.Threading.Thread.Sleep(40);
  if(!ready())return true;
  var after=Selected(tree);
  if(after!=null && !Automation.Compare(row,after)){SendKey(37);SendKey(37);return true;}
  return tree.FindAll(TreeScope.Descendants,Condition.TrueCondition).Count!=count;
 }
 static bool SendKey(int key){
  var events=new Input[2];for(int i=0;i<2;i++){events[i].type=1;events[i].data.keyboard.key=(ushort)key;events[i].data.keyboard.flags=(uint)(i==0?0:2);}
  return SendInput(2,events,Marshal.SizeOf(typeof(Input)))==2;
 }
 static string lastReason="";static BrowserFocus lastArea=BrowserFocus.None;static DateTime nextInspect;
 public static void Send(int key,Action<string> logger,Func<bool> ready,Action exit,Action<bool> favorite){
  if(!Allowed(key) && key!=257)return;
  IntPtr window=GetForegroundWindow();uint pid;
  if(window==IntPtr.Zero)return;
  bool clicked=key==257 && (GetAsyncKeyState(1)&0x8001)!=0;
  if(key==257 && !clicked && DateTime.UtcNow<nextInspect)return;
  if(key==257)nextInspect=DateTime.UtcNow.AddMilliseconds(500);
  GetWindowThreadProcessId(window,out pid);
  try{using(var process=Process.GetProcessById((int)pid)){
   if(!TargetAllowed(process.ProcessName,process.MainWindowHandle,window)){if(clicked && ready())exit();else if(key!=257)Report("Browser keys ignored: Live main window is not in front",logger);return;}
  }}catch(ArgumentException){return;}catch(System.ComponentModel.Win32Exception){return;}
  foreach(int modifier in new[]{16,17,18,91,92})if((GetAsyncKeyState(modifier)&0x8000)!=0)return;
  // Recheck focus after inspecting the target process.
  if(GetForegroundWindow()!=window)return;
  try {
   var root=AutomationElement.FromHandle(window);
   var browser=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"ContentBrowser"));
   if(browser==null){if(key==257)exit();else Report("Browser focus unavailable: browser is hidden",logger);return;}
   if(key==257){
    Point cursor;if(clicked && GetCursorPos(out cursor) && !browser.Current.BoundingRectangle.Contains(cursor.X,cursor.Y)){if(ready())exit();return;}
    var selection=Selected(FindTree(browser,BrowserFocus.Contents));favorite(selection!=null && LiveFavorites.Contains(selection.Current.Name));return;
   }
   var focused=AutomationElement.FocusedElement;
   BrowserFocus area=FocusArea(focused);
   if(area==BrowserFocus.Outside){
    var candidates=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.HasKeyboardFocusProperty,true));
    foreach(AutomationElement candidate in candidates){BrowserFocus found=FocusArea(candidate);if(found!=BrowserFocus.Outside){focused=candidate;area=found;}}
   }
   if(area!=lastArea){lastArea=area;logger("Browser focus detected: "+area+" ["+(focused==null?"none":focused.Current.Name+"; "+focused.Current.AutomationId)+"]");}
   BrowserFocus target=FocusTarget(area,key);
   if(area==BrowserFocus.Outside && key!=256){if(ready())exit();return;}
   if(target!=BrowserFocus.None){
    AutomationElement tree=FindTree(browser,target);
    if(tree==null || !tree.Current.IsKeyboardFocusable){Report("Browser focus unavailable: "+target,logger);return;}
    if(!ready() || GetForegroundWindow()!=window)return;
    tree.SetFocus();
   }
   if(key==256 || key==37 || key==13 && area!=BrowserFocus.Contents){Report("Browser focus: "+area+" -> "+target,logger);return;}
   if(!ready() || GetForegroundWindow()!=window)return;
   if(IsJump(key)){JumpSelected(FindTree(browser,area),key-1024,()=>ready() && GetForegroundWindow()==window,logger);return;}
   var content=FindTree(browser,BrowserFocus.Contents);
   if(key==13){
    var selected=Selected(content);if(selected==null)return;
    // Fast jumps already align focus when they select a row. Re-focusing it here
    // can scroll Live's virtualized tree again even when selection has not changed.
    TraceBrowserSelection(content,"before activation",logger);
    if(!ready() || GetForegroundWindow()!=window)return;
    var current=Selected(content);if(current==null || !Automation.Compare(selected,current))return;
    if(ExpandSelected(content,()=>ready() && GetForegroundWindow()==window,logger))return;
   }
   if(key==49 && (area!=BrowserFocus.Contents || Selected(content)==null))return;
  } catch(ElementNotAvailableException){Report("Browser focus unavailable: element was replaced",logger);return;}
    catch(InvalidOperationException error){Report("Browser focus unavailable: "+error.Message,logger);return;}
    catch(System.Runtime.InteropServices.COMException error){Report("Browser focus unavailable: "+error.Message,logger);return;}
  if(!ready() || GetForegroundWindow()!=window)return;
  bool sent=SendKey(key);
  Report(sent?"Browser keyboard input active":"Browser SendInput failed: "+Marshal.GetLastWin32Error(),logger);
  if(sent && key==13 && ready())exit();
 }
}

sealed class BrowserKeyOutput : IDisposable {
 readonly Queue<int> pending=new Queue<int>();readonly Queue<int> results=new Queue<int>();readonly Func<bool> ready;readonly Action<string> log;Task worker;bool disposed,closing;int generation;
 public bool Enabled;public volatile bool Favorite;
 public BrowserKeyOutput(Func<bool> canSend,Action<string> logger){ready=canSend;log=logger;}
 public void Post(int key){lock(pending){if(disposed || pending.Count>=32)return;pending.Enqueue(key);if(worker==null || worker.IsCompleted)worker=Task.Factory.StartNew(Run);}}
 bool Ready(){lock(pending)return !disposed && !closing && ready();}
 public void NewSession(){lock(pending){generation++;pending.Clear();results.Clear();Favorite=false;closing=false;}}
 public bool TryResult(out int result){lock(pending){result=0;if(results.Count==0)return false;result=results.Dequeue();return true;}}
 void Run(){for(;;){int key,version;lock(pending){if(disposed || pending.Count==0){worker=null;return;}key=pending.Dequeue();version=generation;}Func<bool> valid=()=>Ready() && version==generation;
  try{if(valid())LiveBrowserKeys.Send(key,log,valid,()=>{lock(pending){if(valid() && results.Count==0){results.Enqueue(1);closing=true;pending.Clear();}}},value=>{if(valid())Favorite=value;});}catch(Exception error){log("Browser keyboard unavailable: "+error.Message);}}}
 public void Dispose(){Task task;lock(pending){disposed=true;pending.Clear();task=worker;}if(task!=null)task.Wait(500);}
}

// Live stores Collections in XMP metadata. Read it; assignments go through Live's
// own number-key command, so the browser and its database remain the authority.
static class LiveFavorites {
 static DateTime nextRead;static string folder;static readonly HashSet<string> names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 public static bool Contains(string name){
  if(DateTime.UtcNow>=nextRead){nextRead=DateTime.UtcNow.AddSeconds(1);Refresh();}
  return names.Contains(name);
 }
 public static IEnumerable<string> RedNames(System.Xml.XmlDocument document){
  foreach(System.Xml.XmlNode item in document.SelectNodes("//*[local-name()='filePath']")){
   var colors=item.ParentNode.SelectNodes("*[local-name()='colors']//*[local-name()='li']");
   foreach(System.Xml.XmlNode color in colors)if(color.InnerText=="1"){yield return System.IO.Path.GetFileName(item.InnerText.Replace('/',System.IO.Path.DirectorySeparatorChar));break;}
  }
 }
 static void Refresh(){
  try{
   if(folder==null){
    string root=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Ableton");Version latest=new Version(0,0);string library=null;
    if(System.IO.Directory.Exists(root))foreach(string dir in System.IO.Directory.GetDirectories(root,"Live *")){
     Version version;if(!Version.TryParse(System.IO.Path.GetFileName(dir).Substring(5),out version) || version<=latest)continue;
     string config=System.IO.Path.Combine(dir,"Preferences","Library.cfg");if(!System.IO.File.Exists(config))continue;
     var doc=new System.Xml.XmlDocument();doc.XmlResolver=null;doc.Load(config);var project=doc.SelectSingleNode("//UserLibrary/LibraryProject");if(project==null)continue;
     var path=project.SelectSingleNode("ProjectPath/@Value");var name=project.SelectSingleNode("ProjectName/@Value");if(path==null||name==null)continue;
     library=System.IO.Path.Combine(path.Value,name.Value);latest=version;
    }
    if(library==null)return;folder=System.IO.Path.Combine(library,"Ableton Folder Info");
   }
   if(!System.IO.Directory.Exists(folder))return;
   var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(string file in System.IO.Directory.GetFiles(folder,"*.xmp",System.IO.SearchOption.AllDirectories)){
    var document=new System.Xml.XmlDocument();document.XmlResolver=null;
    using(var stream=new System.IO.FileStream(file,System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.ReadWrite|System.IO.FileShare.Delete))document.Load(stream);
    foreach(string name in RedNames(document))found.Add(name);
   }
   names.Clear();names.UnionWith(found);
  }catch(System.IO.IOException){}catch(System.Xml.XmlException){}catch(UnauthorizedAccessException){}
 }
}
