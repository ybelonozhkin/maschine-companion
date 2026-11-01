using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

sealed partial class AbletonIntegration {
 BasicController basic;BasicClock basicClock;MidiClockInput clockInput;BasicDeviceStore deviceStore;
 BasicDeviceSettings deviceDefaults;
 bool basicActive;string settingsRoot,deviceSerial="",clockInputName="Mikro Companion Send";ControllerProfile? requestedProfile;
 long saveAt,basicLeaseRevision=1;int handoverPads;string profileError="";double internalTempo=120;
 public bool ProfilesEnabled {get{return basic!=null;}}
 public bool BasicActive {get{return basicActive;}}
 public string DeviceSerial {get{return deviceSerial;}}
 public ControllerProfile Profile {get{return requestedProfile ?? (basic==null?ControllerProfile.ControlSurface:basic.Settings.Profile);}}
 public int MidiChannel {get{return basic==null?1:basic.Settings.Channel;}}
 public MidiClockSource ClockSource {get{return basicClock==null?MidiClockSource.Internal:basicClock.Source;}}
 public string ClockInputName {get{return clockInputName;}}
 public double InternalTempo {get{return internalTempo;}}
 public bool DisplayDim {get{return basic!=null&&basic.Settings.DisplayDim;}}
 public AftertouchMode Aftertouch {get{return basic==null?AftertouchMode.Off:basic.Settings.Aftertouch;}}
 public bool HasLocalPattern {get{return basic!=null&&basic.Sequence.Snapshot().Length>0;}}
 void InitializeBasic(string directory,MidiClockSource source,string inputName,double bpm,bool displayDim){
  settingsRoot=directory;basicClock=new BasicClock();internalTempo=bpm;basicClock.Configure(source,bpm);clockInputName=inputName;clockInput=new MidiClockInput(basicClock.Receive);
  basic=new BasicController(basicClock,(n,v,d)=>{if(midi.Ready&&(!d||basicActive&&LeaseAllowed))midi.Note(n,v,d);},v=>{if(midi.Ready&&!active)midi.PitchBend(v);},v=>{if(midi.Ready&&!active)midi.Modulation(v);},log,input.FixedValue,
   polyPressure:(n,v)=>{if(midi.Ready&&(v==0||basicActive&&LeaseAllowed))midi.PolyPressure(n,v);},channelPressure:v=>{if(midi.Ready&&(v==0||basicActive&&LeaseAllowed))midi.ChannelPressure(v);});
  basic.Activity+=EndIdentification;
  deviceDefaults=new BasicDeviceSettings{ClockSource=source,ClockInput=inputName,BasicTempo=bpm,DisplayDim=displayDim,FixedVelocity=input.FixedValue};basic.Configure(deviceDefaults);
 }
 void BindDevice(){
  var identity=ni as INiDeviceIdentity;string serial=identity==null?null:identity.Serial;
  if(!BasicDeviceSettings.ValidSerial(serial)||serial==deviceSerial)return;
  EndIdentification();
  if(basicActive)DeactivateBasic();SaveBasic(true);var settings=deviceStore.Load(serial,deviceDefaults);if(requestedProfile.HasValue)settings.Profile=requestedProfile.Value;
  clockInput.Dispose();basicClock.Configure(settings.ClockSource,settings.BasicTempo);clockInputName=settings.ClockInput;internalTempo=settings.BasicTempo;
  basic.Configure(settings);aftertouch.Mode=settings.Aftertouch;FixedVelocity=settings.FixedVelocity;basic.Dirty=true;deviceSerial=serial;requestedProfile=null;profileError="";log("Controller settings loaded for serial="+serial);SaveBasic(true);
 }
 public void SelectProfile(ControllerProfile value){
  if(basic==null)throw new InvalidOperationException("Controller profiles are not enabled");if(!Enum.IsDefined(typeof(ControllerProfile),value))throw new ArgumentOutOfRangeException("value");
  Pause(false);requestedProfile=deviceSerial.Length==0?(ControllerProfile?)value:null;basic.Settings.Profile=value;basic.Dirty=true;basicLeaseRevision++;SaveBasic(true);modeChanged=false;ResetState();
 }
 public void SelectChannel(int channel){if(basic==null||channel<1||channel>16)throw new ArgumentOutOfRangeException("channel");basic.SetChannel(channel);if(basicActive)midi.Channel=channel;SaveBasic(true);}
 public void SelectClock(MidiClockSource source,string inputName=null){
  if(basic==null)throw new InvalidOperationException("Controller profiles are not enabled");var next=basic.Settings.Clone();next.ClockSource=source;if(inputName!=null)next.ClockInput=inputName;next.Validate();
  basic.CancelPlaying();clockInputName=next.ClockInput;clockInput.Dispose();basicClock.Configure(source,internalTempo);
  basic.Settings.ClockSource=source;basic.Settings.ClockInput=clockInputName;basic.Dirty=true;SaveClock();SaveBasic(true);
 }
 public void SelectDisplayDim(bool value){if(basic==null)return;basic.Settings.DisplayDim=value;deviceDefaults.DisplayDim=value;basic.Dirty=true;SaveBasic(true);}
 public void SelectAftertouch(AftertouchMode value){if(basic==null)throw new InvalidOperationException("Controller profiles are not enabled");if(!Enum.IsDefined(typeof(AftertouchMode),value))throw new ArgumentOutOfRangeException("value");aftertouch.Mode=value;basic.Aftertouch=value;basic.Dirty=true;SaveBasic(true);log("Aftertouch="+value);}
 public void ReloadClock(MidiClockSource source,string inputName,double bpm){
  if(basic==null)return;deviceDefaults.ClockSource=source;deviceDefaults.ClockInput=inputName;deviceDefaults.BasicTempo=bpm;
  if(source==ClockSource&&inputName==clockInputName&&bpm==internalTempo)return;
  basic.CancelPlaying();clockInput.Dispose();clockInputName=inputName;internalTempo=bpm;basicClock.Configure(source,bpm);
  basic.Settings.ClockSource=source;basic.Settings.ClockInput=inputName;basic.Settings.BasicTempo=bpm;basic.Dirty=true;SaveBasic(true);
 }
 void SaveClock(){deviceDefaults.ClockSource=ClockSource;deviceDefaults.ClockInput=clockInputName;deviceDefaults.BasicTempo=internalTempo;LocalIni.Update(Path.Combine(settingsRoot,"companion.ini"),new Dictionary<string,string>{{"ClockSource",ClockSource.ToString()},{"ClockInput",clockInputName},{"BasicTempo",internalTempo.ToString("0.###",CultureInfo.InvariantCulture)}});}
 void SaveBasic(bool force=false){
  if(basic==null)return;long now=clock.ElapsedMilliseconds;if(!force&&now<saveAt)return;
  if(basic.TempoDirty){internalTempo=basicClock.Tempo;basic.Settings.BasicTempo=internalTempo;basic.Dirty=true;SaveClock();basic.TempoDirty=false;saveAt=now+500;}
  if(basic.Dirty&&BasicDeviceSettings.ValidSerial(deviceSerial)){deviceStore.Save(deviceSerial,basic.Settings);basic.Dirty=false;saveAt=now+500;}
 }
 public void SaveLocalMidi(string path){if(basic==null)throw new InvalidOperationException("No local sequencer");basic.Sequence.SaveMidi(path,basicClock.Read().Tempo,basic.Settings.Channel);}
 public void ResetLocalSequencer(){if(basic!=null)basic.Sequence.Reset();}
 bool TickProfiles(string controllerPath,bool blocked){
  if(basic==null)return false;
  if(profileError.Length>0)return true;
  try{
   if(ni!=null&&ni.Granted)BindDevice();
   bool liveReady=host&&clock.ElapsedMilliseconds-stateAt<2000;
   bool wantsLive=Profile==ControllerProfile.ControlSurface&&liveReady;
   if(wantsLive&&basicActive&&basic.Busy){basic.LiveReady=true;wantsLive=false;}else basic.LiveReady=false;
   if(wantsLive){if(basicActive)DeactivateBasic();clockInput.Tick(clock.ElapsedMilliseconds,clockInputName,false);midi.Channel=1;return false;}
   if(active)Deactivate(false);
   bool available=!blocked&&!modeChanged&&controllerPath!=null&&midi.Ready;
   bool nativeSelected=host&&niOwner!=null&&clock.ElapsedMilliseconds-niOwnerAt<1000&&(!niOwner.CompanionRequested||!nativeHandoffEnabled&&niOwner.Count>0);
   bool request=available&&!nativeSelected;
   if(!request&&basicActive)DeactivateBasic();
   if(ni!=null)ni.Request(request,basicLeaseRevision);
   if(!request||ni!=null&&!ni.Granted){clockInput.Tick(clock.ElapsedMilliseconds,clockInputName,false);return true;}
   BindDevice();
   if(Profile==ControllerProfile.ControlSurface&&liveReady&&!basicActive){midi.Channel=1;return false;}
   if(!basicActive||path!=controllerPath){if(basicActive)DeactivateBasic();path=controllerPath;midi.Channel=basic.Settings.Channel;basicActive=true;basic.Activate(handoverPads);handoverPads=0;lastFrame="";log("Basic MIDI active, channel="+midi.Channel);}
   clockInput.Tick(clock.ElapsedMilliseconds,clockInputName,ClockSource==MidiClockSource.External);
   basic.WaitingForLive=Profile==ControllerProfile.ControlSurface;basic.Tick();RenderBasicPanel();SaveBasic();return true;
  }catch(Exception ex){if(profileError!=ex.Message)log("Basic controller: "+ex.Message);profileError=ex.Message;try{if(basicActive)DeactivateBasic();}finally{if(ni!=null)ni.Request(false,-1);}return true;}
 }
 void RenderBasicPanel(){if(!basicActive||!LeaseAllowed)return;byte[] frame=basic.Frame();string label=IdentificationLabel(basic.Screen);string key="basic:"+Convert.ToBase64String(frame)+label;if(key==lastFrame)return;lastFrame=key;
  lock(panelGate){pendingPath=path;pending=frame;pendingLabel=label;if(panelTask==null||panelTask.IsCompleted)panelTask=System.Threading.Tasks.Task.Factory.StartNew(WritePanels);}
 }
 void DeactivateBasic(){
  if(!basicActive)return;handoverPads|=basic.HeldPadMask;basicActive=false;lock(panelGate){panelGeneration++;pending=null;}basic.Deactivate();clockInput.Dispose();if(midi.Ready)midi.ReleaseAll();SaveBasic(true);lastFrame="";
 }
 void ResumeProfiles(){if(basic==null)return;deviceStore=new BasicDeviceStore(settingsRoot);profileError="";basicLeaseRevision++;}
 void TrackReleasedPads(byte[] report){if(handoverPads==0||report==null||(report.Length!=64&&report.Length!=128))return;for(int offset=0;offset<report.Length;offset+=64)if(report[offset]==2)for(int i=offset+1;i+2<offset+64;i+=3)if(report[i]<16&&(report[i+1]&240)==48)handoverPads&=~(1<<report[i]);}
 string BasicStatus {get{return profileError.Length>0?"Controller settings: "+profileError:basicActive?(basic.LiveReady?"Basic: Live ready; finish playing to switch":basic.WaitingForLive?"Basic: waiting for Live script":"Basic MIDI: channel "+MidiChannel):"Waiting for controller ownership";}}
}
