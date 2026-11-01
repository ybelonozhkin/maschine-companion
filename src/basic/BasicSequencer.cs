using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

sealed class BasicNote {
 public int Pitch,Velocity;public double Start,Length;
 public BasicNote Copy(){return (BasicNote)MemberwiseClone();}
}

struct BasicStepPosition {
 public int PadCursor,StripStart,StripEnd,StripCursor;
}

// One local pattern. Owns musical data; no Live handles or MIDI port lifetime.
sealed class BasicSequencer {
 readonly object gate=new object();readonly Func<BasicClockState> clock;readonly Action<int,int,bool> send;
 sealed class PatternSnapshot {public List<BasicNote> Notes;public double Length,Grid;public int Page;}
 List<BasicNote> notes=new List<BasicNote>();readonly Stack<PatternSnapshot> undo=new Stack<PatternSnapshot>(),redo=new Stack<PatternSnapshot>();
 readonly Dictionary<int,double> sounding=new Dictionary<int,double>();readonly Dictionary<int,BasicNote> recording=new Dictionary<int,BasicNote>();readonly Dictionary<int,double> recordStarted=new Dictionary<int,double>();
 double length=4,grid=.25,origin,lastPosition=-.000001;long clockRevision=-1;bool playing,record;int page;
 public BasicSequencer(Func<BasicClockState> source,Action<int,int,bool> output){clock=source;send=output;}
 public bool Playing {get{lock(gate)return playing;}}
 public bool Recording {get{lock(gate)return record;}}
 public int Page {get{lock(gate)return page;}}
 public int Pages {get{lock(gate)return Math.Max(1,(int)Math.Ceiling(length/(grid*16)));}}
 public double Length {get{lock(gate)return length;}}
 public double Grid {get{lock(gate)return grid;}}
 public bool CanUndo {get{lock(gate)return undo.Count>0;}}public bool CanRedo {get{lock(gate)return redo.Count>0;}}
 List<BasicNote> CopyNotes(){
  double beat=clock().Beat;var copied=new List<BasicNote>(notes.Count);
  foreach(var note in notes){var copy=note.Copy();BasicNote held;
   if(recording.TryGetValue(note.Pitch,out held)&&ReferenceEquals(note,held))copy.Length=Math.Max(.01,Math.Min(length-note.Start,beat-recordStarted[note.Pitch]));
   copied.Add(copy);
  }return copied;
 }
 PatternSnapshot Capture(){return new PatternSnapshot{Notes=CopyNotes(),Length=length,Grid=grid,Page=page};}
 void Remember(){undo.Push(Capture());if(undo.Count>64){var values=undo.ToArray();undo.Clear();for(int i=63;i>=0;i--)undo.Push(values[i]);}redo.Clear();}
 public BasicNote[] Snapshot(){lock(gate)return CopyNotes().ToArray();}
 public void Navigate(int delta){lock(gate)page=Math.Max(0,Math.Min(Pages-1,page+delta));}
 public void SetGrid(double value){if(value<=0||value>4||double.IsNaN(value))throw new ArgumentOutOfRangeException("value");lock(gate){grid=value;page=Math.Min(page,Pages-1);}}
 public void Start(bool restart=false){lock(gate){if(playing&&!restart)return;FinishRecording();StopSounding();var state=clock();origin=state.External?0:state.Beat;lastPosition=state.External?Math.Floor(state.Beat/grid)*grid-.000001:-.000001;clockRevision=state.Revision;playing=true;}}
 public void Stop(){lock(gate){FinishRecording();playing=record=false;StopSounding();}}
 public void ToggleRecord(){lock(gate){if(record){FinishRecording();record=false;return;}Remember();Start();record=true;}}
 void FinishRecording(){if(recording.Count==0)return;var state=clock();foreach(var pair in recording){pair.Value.Length=Math.Max(.01,Math.Min(length-pair.Value.Start,state.Beat-recordStarted[pair.Key]));}recording.Clear();recordStarted.Clear();}
 void StopSounding(){Exception error=null;foreach(int pitch in sounding.Keys)try{send(pitch,0,false);}catch(Exception ex){error=ex;}sounding.Clear();if(error!=null)throw error;}
 public void Suspend(){Stop();}
 public void Reset(){lock(gate){Stop();Remember();notes.Clear();length=4;page=0;}}
 public void Duplicate(){lock(gate){if(length>=256)return;FinishRecording();Remember();var copied=CopyNotes();foreach(var note in copied){note.Start+=length;notes.Add(note);}length*=2;}}
 public void ErasePitch(int pitch){lock(gate){FinishRecording();Remember();notes.RemoveAll(n=>n.Pitch==pitch);StopSounding();}}
 public void Undo(bool forward){lock(gate){FinishRecording();var source=forward?redo:undo;var target=forward?undo:redo;if(source.Count==0)return;target.Push(Capture());var state=source.Pop();notes=state.Notes;length=state.Length;grid=state.Grid;page=state.Page;StopSounding();}}
 public void Quantize(bool half=false){lock(gate){FinishRecording();Remember();foreach(var note in notes){double target=Math.Floor(note.Start/grid+.5)*grid;note.Start=(half?(note.Start+target)/2:target)%length;}StopSounding();}}
 int Cell(BasicNote note){int cell=(int)Math.Floor(note.Start/grid+.5);int count=(int)Math.Ceiling(length/grid);return cell>=count?0:cell;}
 public void Toggle(int pitch,int step,int velocity){if(pitch<0||pitch>127||step<0||step>=16||velocity<1||velocity>127)return;lock(gate){double start=(page*16+step)*grid;if(start>=length)return;FinishRecording();Remember();int cell=page*16+step;int removed=notes.RemoveAll(n=>n.Pitch==pitch&&Cell(n)==cell);if(removed==0)notes.Add(new BasicNote{Pitch=pitch,Velocity=velocity,Start=start,Length=grid*.8});StopSounding();}}
 public int ValidMask {get{lock(gate){int result=0;for(int i=0;i<16;i++)if((page*16+i)*grid<length)result|=1<<i;return result;}}}
 public int Mask(int pitch){lock(gate){int result=0;foreach(var note in notes)if(note.Pitch==pitch){int cell=Cell(note)-page*16;if(cell>=0&&cell<16)result|=1<<cell;}return result;}}
 public BasicStepPosition Position {get{lock(gate){
  int start=Math.Min(24,(int)(page*16*grid/length*25));
  var result=new BasicStepPosition{PadCursor=-1,StripCursor=-1,StripStart=start,
   StripEnd=Math.Min(25,Math.Max(start+1,(int)Math.Ceiling(Math.Min(length,(page+1)*16*grid)/length*25)))};
  var state=clock();if(!playing||!state.Available||!state.Running)return result;
  double pos=Math.Max(0,state.Beat-origin)%length;int index=(int)(pos/grid)-page*16;
  result.PadCursor=index>=0&&index<16?index:-1;result.StripCursor=Math.Min(24,(int)(pos/length*25));return result;
 }}}
 public int Cursor {get{return Position.PadCursor;}}
 public void RecordNote(int pitch,int velocity,bool down){lock(gate){if(!record||!playing)return;var state=clock();if(!state.Available||!state.Running)return;double position=Math.Max(0,state.Beat-origin);BasicNote note;
  if(recording.TryGetValue(pitch,out note)){note.Length=Math.Max(.01,Math.Min(length-note.Start,state.Beat-recordStarted[pitch]));recording.Remove(pitch);recordStarted.Remove(pitch);}
  if(down){note=new BasicNote{Pitch=pitch,Velocity=velocity,Start=position%length,Length=grid*.8};notes.Add(note);recording[pitch]=note;recordStarted[pitch]=state.Beat;}
 }}
 public void Pump(){lock(gate){if(!playing)return;var state=clock();if(!state.Available||!state.Running){FinishRecording();StopSounding();return;}
  if(clockRevision!=state.Revision){StopSounding();origin=state.External?0:state.Beat;lastPosition=state.External?Math.Floor(state.Beat/grid)*grid-.000001:-.000001;clockRevision=state.Revision;}
  double position=state.Beat-origin;if(position<0){origin=state.Beat;position=0;lastPosition=-.000001;StopSounding();}
  var finished=new List<int>();foreach(var voice in sounding)if(voice.Value<=position)finished.Add(voice.Key);foreach(int pitch in finished){send(pitch,0,false);sounding.Remove(pitch);}
  foreach(var note in notes){double cycle=Math.Floor((position-note.Start)/length);if(cycle<0)continue;double at=cycle*length+note.Start;if(at<=lastPosition||at>position||position-at>Math.Max(.05,state.Tempo/60*.05))continue;if(sounding.ContainsKey(note.Pitch))send(note.Pitch,0,false);send(note.Pitch,note.Velocity,true);sounding[note.Pitch]=at+Math.Max(.01,note.Length);}
  lastPosition=position;
 }}
 public void SaveMidi(string path,double tempo,int channel){BasicNote[] snapshot;double end;lock(gate){snapshot=Snapshot();end=length;}StandardMidiFile.Save(path,snapshot,end,tempo,channel);}
}

static class StandardMidiFile {
 sealed class Item {public long Tick;public byte[] Bytes;public int Order;}
 static void Number(Stream stream,int value,int count){for(int i=count-1;i>=0;i--)stream.WriteByte((byte)(value>>(i*8)));}
 static void Variable(Stream stream,long value){if(value<0||value>0x0fffffff)throw new ArgumentOutOfRangeException("value");byte[] data=new byte[4];int index=3;data[index]=(byte)(value&127);while((value>>=7)>0)data[--index]=(byte)((value&127)|128);stream.Write(data,index,4-index);}
 public static void Save(string path,BasicNote[] notes,double length,double tempo,int channel){
  if(channel<1||channel>16||tempo<20||tempo>999||double.IsNaN(tempo)||length<=0||length>256)throw new ArgumentOutOfRangeException();const int resolution=960;var events=new List<Item>();int us=(int)Math.Round(60000000/tempo);
  events.Add(new Item{Tick=0,Order=-2,Bytes=new byte[]{255,81,3,(byte)(us>>16),(byte)(us>>8),(byte)us}});
  events.Add(new Item{Tick=0,Order=-1,Bytes=new byte[]{255,88,4,4,2,24,8}});
  foreach(var note in notes){if(note.Pitch<0||note.Pitch>127||note.Velocity<1||note.Velocity>127||note.Start<0||note.Start>=length||note.Length<=0)throw new ArgumentException("Invalid pattern note");long start=(long)Math.Round(note.Start*resolution),finish=Math.Max(start+1,(long)Math.Round(Math.Min(length,note.Start+note.Length)*resolution));events.Add(new Item{Tick=start,Order=1,Bytes=new[]{(byte)(0x90+channel-1),(byte)note.Pitch,(byte)note.Velocity}});events.Add(new Item{Tick=finish,Order=0,Bytes=new[]{(byte)(0x80+channel-1),(byte)note.Pitch,(byte)0}});}
  events.Sort((a,b)=>a.Tick!=b.Tick?a.Tick.CompareTo(b.Tick):a.Order.CompareTo(b.Order));
  using(var track=new MemoryStream())using(var output=new MemoryStream()){
   long previous=0;foreach(var item in events){Variable(track,item.Tick-previous);track.Write(item.Bytes,0,item.Bytes.Length);previous=item.Tick;}Variable(track,Math.Max(previous,(long)Math.Round(length*resolution))-previous);track.Write(new byte[]{255,47,0},0,3);
   var header=Encoding.ASCII.GetBytes("MThd");output.Write(header,0,4);Number(output,6,4);Number(output,0,2);Number(output,1,2);Number(output,resolution,2);header=Encoding.ASCII.GetBytes("MTrk");output.Write(header,0,4);Number(output,(int)track.Length,4);track.Position=0;track.CopyTo(output);
   string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(temporary,output.ToArray());if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
 }
}
