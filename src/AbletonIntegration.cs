using System;
using System.Net;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading.Tasks;

sealed partial class AbletonIntegration : IDisposable {
 readonly INiOwnership ni;
 readonly bool nativeHandoffEnabled;
 volatile bool disposed;
 NiOwnerState niOwner;long niOwnerAt=-10000;bool bundledOwnership;
 bool LeaseAllowed {get{return ni==null || ni.Granted;}}
 bool InputAllowed {get{return !disposed && active && LeaseAllowed;}}
 bool PanelIdle {get{lock(panelGate)return panelTask==null || panelTask.IsCompleted;}}
 NavigationState navigationState;
 bool NavigationMode {get{return navigationState!=null && navigationState.Epoch==feedbackEpoch && navigationState.Enabled;}}
 DeviceState deviceState;long deviceTitleUntil;
 bool DeviceMode {get{return !NavigationMode && deviceState!=null && deviceState.Epoch==feedbackEpoch && deviceState.Enabled;}}
 bool DeviceStripMode {get{return DeviceMode && !GridMode && !StepPreview && !input.Group && deviceState.Position>=0;}}
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
 long clipEditEpoch=-1;bool clipDuplicate,clipErase;
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
 readonly MidiConnectionRetry midiConnection;
 readonly NoteRepeat repeat;long repeatClockAt=-10000;
 readonly PadAftertouch aftertouch;
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
 long stateAt=-10000,velocityDarkUntil,selectionUntil;int mask,padColor=11;int pendingSelected=-1;bool muted,solo,mixAvailable;bool playing,recording,metronome,follow,host,modeChanged;volatile bool active;string path,lastFrame="";string error="";
 readonly long[] padFlashUntil=new long[16];
 readonly long[] flashUntil=new long[128];
 readonly long[] playbackFlashUntil=new long[128];long feedbackEpoch=-1;bool feedbackSeen;
 readonly object panelGate=new object();byte[] pending;string pendingPath;string pendingLabel;Task panelTask;bool owned;long panelGeneration;readonly PanelOutput panelOutput=new PanelOutput();
 public string Status {get{return basic!=null&&profileError.Length>0?BasicStatus:error.Length>0?"Ableton unavailable: "+error:modeChanged?"Paused after MIDI mode: return to native, then Resume":midiConnection.Error.Length>0?(midiConnection.RetryAvailable?"MIDI unavailable; use Resume: ":"Retrying MIDI connection: ")+midiConnection.Error:!midi.Ready?"Waiting for MIDI port":basicActive?BasicStatus:ni!=null && !InputAllowed?(niOwner==null || clock.ElapsedMilliseconds-niOwnerAt>=1000?"Waiting for controller ownership state":niOwner.Uncertain?"Paused: unknown device identity":niOwner.Selected?(niOwner.Count>1?"Maschine selected: choose its Controller manually":"Maschine VST selected"):ni.Status):!active?"Waiting for Ableton control surface":"Ableton: notes + transport";}}
 public bool MidiRetryAvailable {get{return midiConnection.RetryAvailable;}}
 public int FixedVelocity {get{return input.FixedValue;}set{if(value<1 || value>127)throw new ArgumentOutOfRangeException("FixedVelocity");input.FixedValue=value;if(basic!=null){basic.FixedVelocity=value;basic.Settings.FixedVelocity=value;basic.Dirty=true;}}}
 public bool IsWriting {get{return !PanelIdle || ni!=null && ni.Busy;}}
 public bool OwnsController {get{return (InputAllowed||basicActive&&LeaseAllowed) && !modeChanged;}}
 public bool PausedForMidiMode {get{return modeChanged;}}
 public AbletonIntegration(Action<string> logger,Action<string,byte[]> writer=null,int statePort=39473,string activityInputs="",int fixedVelocity=127,Action<int> browserKeyWriter=null,bool showStartupSplash=true,bool nativeHandoff=false,INiOwnership ownership=null,string basicDirectory=null,MidiClockSource basicClockSource=MidiClockSource.Internal,string basicClockInput="Mikro Companion Send",double basicTempo=120,bool displayDim=false){
  if(basicDirectory!=null)deviceStore=new BasicDeviceStore(basicDirectory);
  nativeHandoffEnabled=nativeHandoff;
  if(nativeHandoff||basicDirectory!=null)ni=ownership ?? new NiControllerSession(logger,()=>PanelIdle);
  flashTimer.Interval=15;flashTimer.Start();flashTimer.Stop();flashTimer.Tick+=(sender,args)=>{RenderPanel();if(!FlashesActive(clock.ElapsedMilliseconds))flashTimer.Stop();};
  FixedVelocity=fixedVelocity;startupSplash=showStartupSplash;
  log=logger;panelWrite=writer ?? StandbyLight.Frame;browserKeys=new BrowserKeyOutput(()=>InputAllowed && host && BrowserMode && !modeChanged,logger);browserKeys.Enabled=browserKeyWriter==null;browserKeyWrite=browserKeyWriter ?? browserKeys.Post;
  repeat=new NoteRepeat((n,v,d)=>{if(!d || InputAllowed)midi.Note(n,v,d);},logger);
  aftertouch=new PadAftertouch((n,v)=>{if(midi.Ready&&(v==0||InputAllowed))midi.PolyPressure(n,v);},v=>{if(midi.Ready&&(v==0||InputAllowed))midi.ChannelPressure(v);},()=>InputAllowed&&!repeat.Enabled);
  input.PadHeld+=aftertouch.Held;input.RawPressure+=aftertouch.Pressure;
  repeat.Generated+=note=>generatedFlashes.Enqueue(note);
  externalInputs=new MidiActivityInputs(activityInputs,logger);
  socket=new UdpClient(new IPEndPoint(IPAddress.Loopback,statePort));socket.Client.Blocking=false;
  input.Note+=(n,v,d)=>{if(active){repeat.Note(n,v,d);if(d && !input.Sixteen)Latch(flashUntil,n);}};input.Transport+=Control;
  input.Activity+=EndIdentification;
  input.Pressure+=(n,v)=>{if(active)repeat.Pressure(n,v);};
  input.Repeat+=held=>{
   aftertouch.Clear();
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
  input.Strip+=value=>{if(DeviceMode){if(DeviceStripMode && value>0 && value<=200){deviceTitleUntil=0;midi.DeviceStrip((value-1)*127/199);}return;}if(ExpressionAvailable){if(stripMode==1){stripPosition=value;midi.PitchBend(StripPitch(value));}else if(stripMode==2 && value>0){modPosition=value;midi.Modulation((value-1)*127/199);}}};
  input.Encoder+=delta=>TurnEncoder(delta,false);
  input.RootEncoder+=delta=>TurnEncoder(delta,true);
  input.Bank+=Navigate;
  midiConnection=new MidiConnectionRetry(midi.Create,midi.TryOpen,()=>midi.Ready,log);midiConnection.Tick(clock.ElapsedMilliseconds);
  if(basicDirectory!=null)InitializeBasic(basicDirectory,basicClockSource,basicClockInput,basicTempo,displayDim);
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
 void ResetRepeat(){try{repeat.Reset();}finally{aftertouch.Clear();arpView=padRepeatPinned=false;int ignored;while(generatedFlashes.TryDequeue(out ignored)){} }}
 void Control(MikroTransport command){
  if(!active)return;
  if(command==MikroTransport.ViewToggle){midi.ViewToggle();return;}
  if(command==MikroTransport.Navigate){midi.NavigationToggle();return;}
  if(command==MikroTransport.ScaleToggle && NavigationMode)return;
  if(command==MikroTransport.ScaleToggle && DeviceMode && !GridMode && !StepPreview && !input.Group){deviceTitleUntil=0;midi.DeviceReset();return;}
  if(ArpMode){
   if(command==MikroTransport.KeyboardMode)arpView=false;
   if(command==MikroTransport.PadMode || command==MikroTransport.Step || command==MikroTransport.Events || command==MikroTransport.Browse || command==MikroTransport.Pattern || command==MikroTransport.Scene){input.CancelHeldNotes();ResetRepeat();}
  }
  if(command==MikroTransport.Pattern || command==MikroTransport.Scene){ResetRepeat();input.ReleaseAll();midi.ResetExpression();if(command==MikroTransport.Scene)midi.SceneToggle();else midi.PatternToggle();return;}
  if(PatternMode && (command==MikroTransport.ScaleToggle || command==MikroTransport.FixedVelocity || command==MikroTransport.SixteenVelocity))return;
  if(command==MikroTransport.Favorites){if(BrowserMode)midi.BrowserFavorite();return;}
  if(command==MikroTransport.Pitch || command==MikroTransport.Mod){if(ExpressionAvailable && !DeviceMode){midi.ResetExpression();stripPosition=modPosition=0;int selected=command==MikroTransport.Pitch?1:2;stripMode=stripMode==selected?0:selected;}return;}
  if(PatternMode && !SceneMode && (command==MikroTransport.Duplicate || command==MikroTransport.Erase)){
   bool duplicate=command==MikroTransport.Duplicate;
   if(clipEditEpoch==feedbackEpoch && (duplicate?clipDuplicate:clipErase))midi.PatternEdit(duplicate);
   return;
  }
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
 public bool Feed(string controllerPath,byte[] report){
  TrackReleasedPads(report);
  if(basic!=null){
   if(basicActive&&LeaseAllowed&&path==controllerPath){if(report.Length>=5&&report[0]==1&&(report[1]&1)!=0&&(report[4]&2)!=0){modeChanged=true;Pause(true);log("Basic paused after MIDI-mode shortcut");}else try{basic.Feed(report);RenderBasicPanel();}catch(Exception ex){profileError=ex.Message;Pause(true);log("Basic input stopped: "+ex.Message);}return true;}
   if(!active)return true;
  }
  if(!host)return false;
  if(InputAllowed && path==controllerPath){
   if(report.Length>=5 && report[0]==1 && (report[1]&1)!=0 && (report[4]&2)!=0){modeChanged=true;Pause(true);log("Ableton paused after MIDI-mode shortcut");}
   else try{input.Feed(report);RenderPanel();}catch(Exception ex){error=ex.Message;Pause(true);log("Ableton input stopped: "+error);}
  }
  return true;
 }
 public void Pause(bool clear){
  EndIdentification();
  try{if(ni!=null)ni.Reset();}finally{Deactivate(clear && ni==null);}
 }
 void Deactivate(bool clear){
  if(active&&basic!=null)handoverPads|=input.HeldPadMask;
  if(basicActive)DeactivateBasic();
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
 public void Resume(){if(disposed)return;ResumeProfiles();modeChanged=false;midiConnection.Resume(clock.ElapsedMilliseconds);ResetState();}
 public void Dispose(){
  if(disposed)return;
  disposed=true;
  lock(panelGate){panelGeneration++;pending=null;}
  var errors=new List<Exception>();
  Action<string,Action> cleanup=(name,action)=>{try{action();}catch(Exception ex){errors.Add(new InvalidOperationException(name+": "+ex.Message,ex));}};
  cleanup("Pause",()=>Pause(true));
  cleanup("Panel output",()=>{Task task;lock(panelGate){task=panelTask;}if(task!=null && !task.Wait(1500))throw new TimeoutException("Panel output did not finish");});
  if(ni!=null)cleanup("NI ownership",ni.Dispose);
  if(basic!=null)cleanup("Basic playing",basic.Dispose);
  if(clockInput!=null)cleanup("MIDI clock input",clockInput.Dispose);
  cleanup("Note Repeat",repeat.Dispose);
  cleanup("MIDI activity inputs",externalInputs.Dispose);
  cleanup("Browser",browserKeys.Dispose);
  cleanup("Flash timer",flashTimer.Dispose);
  cleanup("UDP socket",socket.Close);
  cleanup("MIDI port",midi.Dispose);
  if(errors.Count>0)throw new AggregateException("Ableton cleanup failed: "+string.Join("; ",errors.ConvertAll(error=>error.Message).ToArray()),errors);
 }
}
