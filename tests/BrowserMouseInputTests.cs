using System;

static class BrowserMouseInputTests {
 sealed class Mouse {
  public short State;
  public short Read(){short value=State;State=(short)(State&0x8000);return value;}
  public void Press(){State=unchecked((short)0x8001);}
  public void Release(){State=(short)(State&1);}
 }
 static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Main(){
  var mouse=new Mouse();var clicks=new BrowserMouseInput(mouse.Read);
  mouse.Press();mouse.Release();clicks.Begin();
  Check(!clicks.Poll(),"click before Browse does not dismiss it");
  mouse.Press();mouse.Release();Check(clicks.Poll(),"new short click after Browse is observed");
  Check(!clicks.Poll(),"historical click is consumed once");
  mouse.Press();clicks.Begin();Check(!clicks.Poll(),"button held at entry is ignored");
  mouse.Release();Check(!clicks.Poll(),"release of inherited button does not dismiss Browse");
  mouse.Press();Check(clicks.Poll(),"new press after inherited release is observed");
  mouse.Release();clicks.Poll();mouse.Press();mouse.Release();clicks.Begin();
  Check(!clicks.Poll(),"each Browse entry establishes a fresh baseline");
  Console.WriteLine("PASS Browse mouse baseline, historical clicks, short clicks, held entry and re-entry");
 }
}
