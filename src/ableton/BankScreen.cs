using System;
using System.Globalization;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;

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
