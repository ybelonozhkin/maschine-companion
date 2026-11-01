using System;
using System.Text;

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
 public long Epoch;public bool Available,Enabled,Left,Right,Previous,Next;public string Name,Parameter,Value;public int Position=-1;
 public static bool TryParse(string[] fields,out DeviceState state){
  state=null;bool modern=fields.Length==13 && fields[0]=="MDEV2";if(!(modern || fields.Length==12 && fields[0]=="MDEV1") || fields[fields.Length-1]!="ready")return false;
  int position=-1;if(modern && (!int.TryParse(fields[11],out position) || position< -1 || position>127))return false;
  long epoch;if(!long.TryParse(fields[1],out epoch) || epoch<0)return false;
  var flags=new int[6];for(int i=0;i<flags.Length;i++)if(!int.TryParse(fields[i+2],out flags[i]) || flags[i]<0 || flags[i]>1)return false;
  if(flags[1]>flags[0])return false;
  var labels=new string[3];try{for(int i=0;i<labels.Length;i++){labels[i]=fields[i+8]=="-"?"":new UTF8Encoding(false,true).GetString(Convert.FromBase64String(fields[i+8]));if(labels[i].Length>80 || labels[i].IndexOfAny(new[]{'\r','\n','\0','\u0001','\u0002','\u0003','\u0004'})>=0)return false;}}catch(FormatException){return false;}catch(DecoderFallbackException){return false;}
  state=new DeviceState{Epoch=epoch,Available=flags[0]!=0,Enabled=flags[1]!=0,Left=flags[2]!=0,Right=flags[3]!=0,Previous=flags[4]!=0,Next=flags[5]!=0,Name=labels[0],Parameter=labels[1],Value=labels[2],Position=position};return true;
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
