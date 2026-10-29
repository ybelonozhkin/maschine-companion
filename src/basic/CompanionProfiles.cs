using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

partial class Companion {
 ToolStripMenuItem controllerMenu,modeMenu,basicItem,surfaceItem,launcherItem,channelMenu,clockMenu,internalItem,externalItem,clockInputsMenu,saveMidiItem,resetStepItem,detectItem,aftertouchMenu;
 readonly List<ToolStripMenuItem> aftertouchItems=new List<ToolStripMenuItem>();
 readonly List<ToolStripMenuItem> channelItems=new List<ToolStripMenuItem>();
 AbletonIntegration CreateControllerEngine(){return new AbletonIntegration(Log,activityInputs:settings.MidiActivityInputs,fixedVelocity:settings.FixedVelocity,nativeHandoff:settings.EnableNativeHandoff,basicDirectory:root,basicClockSource:settings.ClockSource,basicClockInput:settings.ClockInput,basicTempo:settings.BasicTempo,displayDim:settings.DisplayDim);}
 bool ControllerDisplayDim {get{return ableton!=null&&ableton.DeviceSerial.Length>0?ableton.DisplayDim:settings.DisplayDim;}}
 void AddControllerMenus(ContextMenuStrip menu){
  controllerMenu=new ToolStripMenuItem("Mikro MK3");modeMenu=new ToolStripMenuItem("Mode");
  basicItem=new ToolStripMenuItem("Basic mode",null,(s,e)=>ChooseProfile(ControllerProfile.Basic));
  surfaceItem=new ToolStripMenuItem("Control surface",null,(s,e)=>ChooseProfile(ControllerProfile.ControlSurface));
  launcherItem=new ToolStripMenuItem("Launcher standby",null,(s,e)=>UseLauncher());
  modeMenu.DropDownItems.AddRange(new ToolStripItem[]{basicItem,surfaceItem,launcherItem});
  channelMenu=new ToolStripMenuItem("Basic MIDI channel");
  for(int n=1;n<=16;n++){int channel=n;var item=new ToolStripMenuItem(n.ToString(),null,(s,e)=>ControllerAction(()=>ableton.SelectChannel(channel)));channelMenu.DropDownItems.Add(item);channelItems.Add(item);}
  clockMenu=new ToolStripMenuItem("Basic clock");
  internalItem=new ToolStripMenuItem("Internal",null,(s,e)=>ControllerAction(()=>ableton.SelectClock(MidiClockSource.Internal)));
  externalItem=new ToolStripMenuItem("External MIDI",null,(s,e)=>ControllerAction(()=>ableton.SelectClock(MidiClockSource.External)));
  clockInputsMenu=new ToolStripMenuItem("External input");clockInputsMenu.DropDownOpening+=(s,e)=>PopulateClockInputs();
  clockMenu.DropDownItems.AddRange(new ToolStripItem[]{internalItem,externalItem,new ToolStripSeparator(),clockInputsMenu});
  aftertouchMenu=new ToolStripMenuItem("Aftertouch");foreach(AftertouchMode mode in Enum.GetValues(typeof(AftertouchMode))){AftertouchMode selected=mode;var item=new ToolStripMenuItem(mode.ToString(),null,(s,e)=>ControllerAction(()=>ableton.SelectAftertouch(selected)));aftertouchMenu.DropDownItems.Add(item);aftertouchItems.Add(item);}
  saveMidiItem=new ToolStripMenuItem("Save MIDI file...",null,(s,e)=>SaveBasicMidi());
  resetStepItem=new ToolStripMenuItem("Reset step sequencer...",null,(s,e)=>{if(MessageBox.Show("Clear the local Step pattern? Save a MIDI file first if you want to keep it.","Reset step sequencer",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)==DialogResult.OK)ControllerAction(()=>ableton.ResetLocalSequencer());});
  menu.Items.Remove(dimItem);menu.Items.Remove(vegasItem);
  controllerMenu.DropDownItems.AddRange(new ToolStripItem[]{modeMenu,channelMenu,clockMenu,aftertouchMenu,new ToolStripSeparator(),dimItem,vegasItem,new ToolStripSeparator(),saveMidiItem,resetStepItem});
  menu.Items.Insert(1,controllerMenu);
  detectItem=new ToolStripMenuItem("Detect",null,(s,e)=>ControllerAction(()=>{if(ableton!=null)ableton.Detect();}));menu.Items.Insert(2,detectItem);
 }
 void ControllerAction(Action action){try{action();if(ableton!=null){settings.ClockSource=ableton.ClockSource;settings.ClockInput=ableton.ClockInputName;settings.BasicTempo=ableton.InternalTempo;}lastProcessCheck=-1000;SafeTick();}catch(Exception ex){Log("Controller setting: "+ex.Message);tray.ShowBalloonTip(5000,"Controller setting",ex.Message,ToolTipIcon.Error);}}
 void ChooseProfile(ControllerProfile profile){ControllerAction(()=>{if(ableton==null)ableton=CreateControllerEngine();LocalIni.Update(Path.Combine(root,"companion.ini"),new Dictionary<string,string>{{"EnableAbleton","true"}});settings.EnableAbleton=true;ableton.SelectProfile(profile);});}
 void UseLauncher(){ControllerAction(()=>{if(ableton!=null)ableton.Pause(true);LocalIni.Update(Path.Combine(root,"companion.ini"),new Dictionary<string,string>{{"EnableAbleton","false"}});settings.EnableAbleton=false;foreach(var controller in controllers.Values){controller.LedSet=controller.LedAttempted=false;controller.Hold.CancelUntilRelease();}});}
 void PopulateClockInputs(){clockInputsMenu.DropDownItems.Clear();try{foreach(string name in MidiClockInput.Names()){string input=name;var item=new ToolStripMenuItem(name,null,(s,e)=>ControllerAction(()=>ableton.SelectClock(MidiClockSource.External,input))){Checked=ableton!=null&&ableton.ClockInputName==name};clockInputsMenu.DropDownItems.Add(item);}if(clockInputsMenu.DropDownItems.Count==0)clockInputsMenu.DropDownItems.Add(new ToolStripMenuItem("No MIDI inputs available"){Enabled=false});}catch(Exception ex){Log("Clock input list: "+ex.Message);clockInputsMenu.DropDownItems.Add(new ToolStripMenuItem("MIDI inputs unavailable"){Enabled=false});}}
 void UpdateControllerMenus(){if(controllerMenu==null)return;
  bool enabled=settings.EnableAbleton&&ableton!=null,connected=controllers.Count==1;
  bool identified=connected&&ableton!=null&&ableton.DeviceSerial.Length>0;
  controllerMenu.Text="Mikro MK3"+(identified?" ("+ableton.DeviceSerial+")":connected?"":" (disconnected)");controllerMenu.Enabled=controllers.Count<=1;
  basicItem.Checked=enabled&&ableton.Profile==ControllerProfile.Basic;surfaceItem.Checked=enabled&&ableton.Profile==ControllerProfile.ControlSurface;launcherItem.Checked=!enabled;
  channelMenu.Enabled=clockMenu.Enabled=aftertouchMenu.Enabled=identified;dimItem.Enabled=connected;dimItem.Checked=ControllerDisplayDim;
  foreach(var item in aftertouchItems)item.Checked=identified&&item.Text==ableton.Aftertouch.ToString();
  if(identified){for(int i=0;i<channelItems.Count;i++)channelItems[i].Checked=i+1==ableton.MidiChannel;internalItem.Checked=ableton.ClockSource==MidiClockSource.Internal;externalItem.Checked=!internalItem.Checked;}
  saveMidiItem.Enabled=resetStepItem.Enabled=identified&&ableton.HasLocalPattern;
  detectItem.Enabled=identified&&enabled&&ableton.CanDetect&&!sessionLocked&&!suspended&&!pause.Active(DateTimeOffset.UtcNow);
 }
 void SaveBasicMidi(){using(var dialog=new SaveFileDialog{Title="Save MIDI file",Filter="MIDI file (*.mid)|*.mid",DefaultExt="mid",AddExtension=true,OverwritePrompt=true,FileName="MikroPattern-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".mid"}){if(dialog.ShowDialog()==DialogResult.OK)ControllerAction(()=>{ableton.SaveLocalMidi(dialog.FileName);Log("Local MIDI pattern saved: "+dialog.FileName);});}}
}
