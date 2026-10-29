using System;

sealed partial class AbletonIntegration {
 string identificationSerial;
 public bool IdentificationActive {get{return identificationSerial!=null;}}
 public bool CanDetect {get{return ProfilesEnabled&&BasicDeviceSettings.ValidSerial(deviceSerial)&&OwnsController;}}

 public bool Detect(){
  if(!CanDetect)return false;
  identificationSerial=deviceSerial;lastFrame="";
  if(basicActive)RenderBasicPanel();else RenderPanel();
  return true;
 }

 void EndIdentification(){if(identificationSerial==null)return;identificationSerial=null;lastFrame="";}
 string IdentificationLabel(string normal){return identificationSerial==null?normal:BankScreen.SerialLabel(identificationSerial);}
}
