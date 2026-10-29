using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

enum ControllerProfile { Basic, ControlSurface }

static class LocalIni {
 public static void Write(string path,string text){
  string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
  try{File.WriteAllText(temporary,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 public static void Update(string path,IDictionary<string,string> values){
  var pending=new Dictionary<string,string>(values,StringComparer.OrdinalIgnoreCase);var lines=new List<string>();
  if(File.Exists(path))foreach(string line in File.ReadAllLines(path)){
   int eq=line.IndexOf('=');string key=eq>0?line.Substring(0,eq).Trim():"";
   if(pending.ContainsKey(key)){lines.Add(key+"="+pending[key]);pending.Remove(key);}else lines.Add(line);
  }
  foreach(var item in pending)lines.Add(item.Key+"="+item.Value);
  Write(path,string.Join(Environment.NewLine,lines.ToArray())+Environment.NewLine);
 }
}

sealed class BasicDeviceSettings {
 public ControllerProfile Profile=ControllerProfile.ControlSurface;
 public int Channel=1,PadBase=36,KeyboardStart=36,ScaleIndex,Root;
 public MidiClockSource ClockSource=MidiClockSource.Internal;
 public string ClockInput="Mikro Companion Send";
 public double BasicTempo=120;
 public bool DisplayDim;
 public int FixedVelocity=127;
 public AftertouchMode Aftertouch=AftertouchMode.Off;
 public bool Keyboard,Scale=true;
 public BasicDeviceSettings Clone(){return (BasicDeviceSettings)MemberwiseClone();}
 public static bool ValidSerial(string serial){if(string.IsNullOrEmpty(serial)||serial.Length>128)return false;foreach(char c in serial)if(c>127 || !char.IsLetterOrDigit(c))return false;return true;}
 public void Validate(){
  if(Channel<1||Channel>16||PadBase<0||PadBase>112||KeyboardStart<0||KeyboardStart>127||Root<0||Root>11||ScaleIndex<0||ScaleIndex>=BasicLayout.ScaleNames.Length || !Enum.IsDefined(typeof(ControllerProfile),Profile))throw new FormatException("Invalid device MIDI/layout settings");
  if(!Enum.IsDefined(typeof(MidiClockSource),ClockSource)||BasicTempo<20||BasicTempo>999||double.IsNaN(BasicTempo)||double.IsInfinity(BasicTempo))throw new FormatException("Invalid device clock settings");
  if(string.IsNullOrEmpty(ClockInput)||ClockInput.Length>128||ClockInput.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new FormatException("Invalid device clock input");
  if(FixedVelocity<1||FixedVelocity>127)throw new FormatException("Invalid device fixed velocity");
  if(!Enum.IsDefined(typeof(AftertouchMode),Aftertouch))throw new FormatException("Invalid device aftertouch mode");
 }
}

sealed class BasicDeviceStore {
 readonly string path;readonly Dictionary<string,Dictionary<string,string>> sections=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
 public BasicDeviceStore(string directory){path=Path.Combine(directory,"devices.ini");if(!File.Exists(path))return;Dictionary<string,string> current=null;
  foreach(string raw in File.ReadAllLines(path)){
   string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith(";"))continue;
   if(line.StartsWith("[")&&line.EndsWith("]")){string name=line.Substring(1,line.Length-2);if(!name.StartsWith("Mikro."))throw new FormatException("Invalid devices.ini section");string serial=name.Substring(6);if(!BasicDeviceSettings.ValidSerial(serial)||sections.ContainsKey(serial))throw new FormatException("Invalid or duplicate device serial");current=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);sections.Add(serial,current);continue;}
   int eq=line.IndexOf('=');if(current==null||eq<=0)throw new FormatException("Invalid devices.ini entry");current[line.Substring(0,eq).Trim()]=line.Substring(eq+1).Trim();
  }
 }
 public BasicDeviceSettings Load(string serial,BasicDeviceSettings defaults=null){
  if(!BasicDeviceSettings.ValidSerial(serial))throw new ArgumentException("A valid device serial is required");
  var result=defaults==null?new BasicDeviceSettings():defaults.Clone();Dictionary<string,string> values;if(!sections.TryGetValue(serial,out values)){result.Validate();return result;}
  foreach(var pair in values){switch(pair.Key.ToLowerInvariant()){
   case "profile":result.Profile=(ControllerProfile)Enum.Parse(typeof(ControllerProfile),pair.Value,true);break;
   case "midichannel":result.Channel=int.Parse(pair.Value,CultureInfo.InvariantCulture);break;
   case "padbase":result.PadBase=int.Parse(pair.Value,CultureInfo.InvariantCulture);break;
   case "keyboardstart":result.KeyboardStart=int.Parse(pair.Value,CultureInfo.InvariantCulture);break;
   case "root":result.Root=int.Parse(pair.Value,CultureInfo.InvariantCulture);break;
   case "scaleindex":result.ScaleIndex=int.Parse(pair.Value,CultureInfo.InvariantCulture);break;
   case "keyboard":result.Keyboard=bool.Parse(pair.Value);break;
   case "scale":result.Scale=bool.Parse(pair.Value);break;
   case "clocksource":result.ClockSource=(MidiClockSource)Enum.Parse(typeof(MidiClockSource),pair.Value,true);break;
   case "clockinput":result.ClockInput=pair.Value;break;
   case "basictempo":result.BasicTempo=double.Parse(pair.Value,CultureInfo.InvariantCulture);break;
   case "displaydim":result.DisplayDim=bool.Parse(pair.Value);break;
   case "fixedvelocity":result.FixedVelocity=int.Parse(pair.Value,CultureInfo.InvariantCulture);break;
   case "aftertouch":result.Aftertouch=(AftertouchMode)Enum.Parse(typeof(AftertouchMode),pair.Value,true);break;
  }}result.Validate();return result;
 }
 public void Save(string serial,BasicDeviceSettings value){
  if(!BasicDeviceSettings.ValidSerial(serial))throw new ArgumentException("A valid device serial is required");value.Validate();
  var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
   {"Profile",value.Profile.ToString()},{"MidiChannel",value.Channel.ToString(CultureInfo.InvariantCulture)},
   {"PadBase",value.PadBase.ToString(CultureInfo.InvariantCulture)},{"KeyboardStart",value.KeyboardStart.ToString(CultureInfo.InvariantCulture)},
   {"Root",value.Root.ToString(CultureInfo.InvariantCulture)},{"ScaleIndex",value.ScaleIndex.ToString(CultureInfo.InvariantCulture)},
   {"Keyboard",value.Keyboard.ToString().ToLowerInvariant()},{"Scale",value.Scale.ToString().ToLowerInvariant()},
   {"ClockSource",value.ClockSource.ToString()},{"ClockInput",value.ClockInput},{"BasicTempo",value.BasicTempo.ToString("0.###",CultureInfo.InvariantCulture)},
   {"DisplayDim",value.DisplayDim.ToString().ToLowerInvariant()},{"FixedVelocity",value.FixedVelocity.ToString(CultureInfo.InvariantCulture)},
   {"Aftertouch",value.Aftertouch.ToString()}};
  Dictionary<string,string> previous;bool existed=sections.TryGetValue(serial,out previous);sections[serial]=values;
  try{var text=new StringBuilder("# Local settings, keyed by controller serial number\n");foreach(var section in sections){text.Append("\n[Mikro.").Append(section.Key).Append("]\n");foreach(var item in section.Value)text.Append(item.Key).Append('=').Append(item.Value).Append('\n');}LocalIni.Write(path,text.ToString());}
  catch{if(existed)sections[serial]=previous;else sections.Remove(serial);throw;}
 }
}

static class BasicLayout {
 // Standard interval definitions, authored independently of Live's scale catalogue.
 public static readonly string[] ScaleNames={"Major","Minor","Dorian","Phrygian","Lydian","Mixolydian","Locrian","Harmonic Minor","Melodic Minor","Major Pentatonic","Minor Pentatonic","Blues","Chromatic"};
 static readonly int[][] intervals={new[]{0,2,4,5,7,9,11},new[]{0,2,3,5,7,8,10},new[]{0,2,3,5,7,9,10},new[]{0,1,3,5,7,8,10},new[]{0,2,4,6,7,9,11},new[]{0,2,4,5,7,9,10},new[]{0,1,3,5,6,8,10},new[]{0,2,3,5,7,8,11},new[]{0,2,3,5,7,9,11},new[]{0,2,4,7,9},new[]{0,3,5,7,10},new[]{0,3,5,6,7,10},new[]{0,1,2,3,4,5,6,7,8,9,10,11}};
 static readonly int[] banks={0,4,20,36,52,68,84,100,112};
 public static int Bank(int current,int delta){for(int i=0;i<Math.Abs(delta);i++){int next=current;foreach(int bank in banks)if(delta>0?bank>current&&(next==current||bank<next):bank<current&&(next==current||bank>next))next=bank;current=next;}return current;}
 public static int[] Notes(BasicDeviceSettings settings){var notes=new int[16];int count=0;for(int n=settings.KeyboardStart;n<128&&count<16;n++)if(!settings.Scale||Array.IndexOf(intervals[settings.ScaleIndex],(n-settings.Root+120)%12)>=0)notes[count++]=n;while(count<16)notes[count++]=-1;return notes;}
 public static int DefaultStart(BasicDeviceSettings settings){return 36+settings.Root;}
}
