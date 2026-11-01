using System;

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
