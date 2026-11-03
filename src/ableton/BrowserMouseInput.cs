using System;

// Read-and-clear the historical press bit at entry; an earlier click must not
// dismiss a newly opened browser. A button already held belongs to that entry.
sealed class BrowserMouseInput {
 readonly Func<short> read;bool started,waitForRelease;
 public BrowserMouseInput(Func<short> readState){read=readState;}
 public void Begin(){waitForRelease=(read()&0x8000)!=0;started=true;}
 public bool Poll(){
  if(!started){Begin();return false;}
  int state=read();bool down=(state&0x8000)!=0;
  if(waitForRelease){waitForRelease=down;return false;}
  return (state&0x8001)!=0;
 }
}
