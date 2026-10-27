using System;
using System.Globalization;
using System.Windows.Automation;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

static class LiveBrowserKeys {
 static void Report(string reason,Action<string> logger){if(reason!=lastReason){lastReason=reason;logger(reason);}}
 [StructLayout(LayoutKind.Sequential)]public struct Keyboard {public ushort key,scan;public uint flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Sequential)]public struct Mouse {public int x,y;public uint data,flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Explicit)]public struct Payload {[FieldOffset(0)]public Keyboard keyboard;[FieldOffset(0)]public Mouse mouse;}
 [StructLayout(LayoutKind.Sequential)]public struct Input {public uint type;public Payload data;}
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
 public enum BrowserFocus {None,Outside,Contents,Sidebar,Search}
 public static BrowserFocus FocusTarget(BrowserFocus area,int key){
  if(key==256)return BrowserFocus.Contents;
  if(key==37)return BrowserFocus.Sidebar;
  if(key==13 && area!=BrowserFocus.Contents)return BrowserFocus.Contents;
  return area==BrowserFocus.Contents || area==BrowserFocus.Sidebar || area==BrowserFocus.Outside?BrowserFocus.None:BrowserFocus.Contents;
 }
 static BrowserFocus FocusArea(AutomationElement element){
  BrowserFocus area=BrowserFocus.Outside;
  for(int i=0;element!=null && i<64;i++,element=TreeWalker.RawViewWalker.GetParent(element)){
   var current=element.Current;
   if(current.AutomationId.EndsWith("BrowserToolbar.SearchField",StringComparison.Ordinal))return BrowserFocus.Search;
   if(current.AutomationId.Contains(".SidebarSplitter.ContentPane"))return BrowserFocus.Contents;
   if(current.ControlType==ControlType.Tree)area=current.Name=="Browser Sidebar"?BrowserFocus.Sidebar:BrowserFocus.Contents;
   if(current.AutomationId=="ContentBrowser")return area;
  }return BrowserFocus.Outside;
 }
 static AutomationElement FindTree(AutomationElement browser,BrowserFocus area){
  var trees=browser.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Tree));
  foreach(AutomationElement tree in trees){bool sidebar=tree.Current.Name=="Browser Sidebar";if(sidebar==(area==BrowserFocus.Sidebar))return tree;}return null;
 }
 public static bool TargetAllowed(string processName,IntPtr mainWindow,IntPtr window){return window!=IntPtr.Zero && mainWindow==window && processName.StartsWith("Ableton Live ",StringComparison.OrdinalIgnoreCase);}
 public static bool IsJump(int key){return key>=961 && key<=1087 && key!=1024;}
 public static int JumpTarget(int selected,int count,int delta){return count<=0?-1:Math.Max(0,Math.Min(count-1,(selected<0?(delta>0?-1:count):selected)+delta));}
 public static bool Allowed(int key){return key==256 || key==13 || key==49 || key>=37 && key<=40 || IsJump(key);}
 [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 static AutomationElement Selected(AutomationElement tree){
  if(tree==null)return null;object pattern;
  if(tree.TryGetCurrentPattern(SelectionPattern.Pattern,out pattern)){var items=((SelectionPattern)pattern).Current.GetSelection();if(items.Length==1)return items[0];}
  return tree.FindFirst(TreeScope.Descendants,new PropertyCondition(SelectionItemPattern.IsSelectedProperty,true));
 }
 public enum ChildProbe {Cancelled,Empty,Populated}
 public static ChildProbe WaitForChildren(Func<bool> hasChildren,Func<bool> valid,Func<long> milliseconds,Action<int> delay){
  long started=milliseconds();
  for(;;){
   if(!valid())return ChildProbe.Cancelled;
   bool children=hasChildren();
   if(!valid())return ChildProbe.Cancelled;
   if(children)return ChildProbe.Populated;
   long remaining=500-(milliseconds()-started);
   if(remaining<=0)return ChildProbe.Empty;
   delay((int)Math.Min(25,remaining));
  }
 }
 static readonly Condition BrowserRow=new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.DataItem),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TreeItem),new PropertyCondition(AutomationElement.IsSelectionItemPatternAvailableProperty,true));
 static bool HasChildRows(AutomationElement row){return row.FindFirst(TreeScope.Children,BrowserRow)!=null;}
 static bool InExpandedBranch(AutomationElement tree,AutomationElement row){
  for(var parent=TreeWalker.ControlViewWalker.GetParent(row);parent!=null;parent=TreeWalker.ControlViewWalker.GetParent(parent)){
   if(Automation.Compare(parent,tree))return true;
   object pattern;if(parent.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out pattern) && ((ExpandCollapsePattern)pattern).Current.ExpandCollapseState==ExpandCollapseState.Collapsed)return false;
  }return false;
 }
 static void JumpSelected(AutomationElement tree,int delta,Func<bool> ready,Action<string> logger){
  if(tree==null || !ready())return;
  var selected=Selected(tree);var rows=new List<AutomationElement>();int index=-1;
  foreach(AutomationElement row in tree.FindAll(TreeScope.Descendants,BrowserRow)){
   object selectable;if(!row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selectable) || !InExpandedBranch(tree,row))continue;
   if(selected!=null && Automation.Compare(row,selected))index=rows.Count;
   rows.Add(row);
  }
  // A selected row missing from the current snapshot is a stale tree, not the start of the list.
  if(selected!=null && index<0)return;
  int target=JumpTarget(index,rows.Count,delta);if(target<0 || target==index || !ready())return;
  var current=Selected(tree);
  if(selected!=null && (current==null || !Automation.Compare(selected,current)))return;
  object pattern;if(!rows[target].TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern))return;
  ((SelectionItemPattern)pattern).Select();
  if(ready() && rows[target].TryGetCurrentPattern(ScrollItemPattern.Pattern,out pattern))((ScrollItemPattern)pattern).ScrollIntoView();
  // SelectionItem.Select does not move keyboard focus. Right/Enter must act on this row too.
  if(ready())rows[target].SetFocus();
  logger("Browser jump: "+delta+" rows; selected "+(target+1)+"/"+rows.Count);
 }
 public static bool EmptyPluginCanLoad(string treeName,int parentRows){
  // In Live's grouped Plug-Ins list: creator -> device -> preset.
  // Do not apply this rule to Places, arbitrary folders or unknown layouts.
  return treeName!=null && treeName.StartsWith("Plug-Ins List",StringComparison.Ordinal) && parentRows==1;
 }
 static bool EmptyPluginCanLoad(AutomationElement tree,AutomationElement row){
  int parents=0;var parent=TreeWalker.ControlViewWalker.GetParent(row);
  for(int depth=0;parent!=null && depth<64;depth++,parent=TreeWalker.ControlViewWalker.GetParent(parent)){
   if(Automation.Compare(parent,tree))return EmptyPluginCanLoad(tree.Current.Name,parents);
   var type=parent.Current.ControlType;object selection;
   if(type==ControlType.DataItem || type==ControlType.TreeItem || parent.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection))parents++;
  }return false;
 }
 static string RowPath(AutomationElement tree,AutomationElement row){
  var names=new List<string>();
  for(int depth=0;row!=null && depth<64;depth++,row=TreeWalker.ControlViewWalker.GetParent(row)){
   if(Automation.Compare(row,tree)){names.Reverse();return string.Join("\u001f",names.ToArray());}
   object selection;var type=row.Current.ControlType;
   if(type==ControlType.DataItem || type==ControlType.TreeItem || row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection))names.Add(row.Current.Name);
  }return null;
 }
 sealed class RowAddress {
  public string Path;public int Occurrence;
  public static RowAddress Capture(AutomationElement tree,AutomationElement row){
   string path=RowPath(tree,row);if(path==null)return null;int occurrence=0;
   foreach(AutomationElement candidate in tree.FindAll(TreeScope.Descendants,BrowserRow)){
    if(RowPath(tree,candidate)!=path)continue;
    if(Automation.Compare(candidate,row))return new RowAddress{Path=path,Occurrence=occurrence};
    occurrence++;
   }return null;
  }
  public AutomationElement Resolve(AutomationElement tree){
   int occurrence=0;foreach(AutomationElement candidate in tree.FindAll(TreeScope.Descendants,BrowserRow))
    if(RowPath(tree,candidate)==Path && occurrence++==Occurrence)return candidate;
   return null;
  }
 }
 static bool PreserveExpandedSelection(AutomationElement tree,RowAddress address,Func<bool> ready,Action<string> logger){
  if(address==null || !ready())return false;
  var row=address.Resolve(tree);if(row==null)return false;
  var selected=Selected(tree);
  if(selected==null || !Automation.Compare(selected,row)){
   string previous=selected==null?"none":selected.Current.Name;object pattern;
   if(!ready() || !row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern))return false;
   ((SelectionItemPattern)pattern).Select();
   if(!ready())return false;row.SetFocus();
   logger("Browser expansion kept selection: "+previous+" -> "+row.Current.Name);
  }
  return true;
 }
 static void TraceBrowserSelection(AutomationElement tree,string stage,Action<string> logger){
  var row=Selected(tree);logger("Browser "+stage+": "+(row==null?"none":row.Current.Name+"; screen Y="+row.Current.BoundingRectangle.Y.ToString("0",CultureInfo.InvariantCulture)));
 }
 static bool ExpandSelected(AutomationElement tree,Func<bool> ready,Action<string> logger){
  var row=Selected(tree);if(row==null)return false;object pattern;
  Func<bool> valid=()=>{if(!ready())return false;var selected=Selected(tree);return selected!=null && Automation.Compare(row,selected);};
  if(row.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out pattern)){
   var expand=(ExpandCollapsePattern)pattern;var state=expand.Current.ExpandCollapseState;
   if(state==ExpandCollapseState.LeafNode)return false;
   if(!valid())return true;
   bool canLoadEmpty=EmptyPluginCanLoad(tree,row);
   var address=canLoadEmpty?RowAddress.Capture(tree,row):null;
   if(state==ExpandCollapseState.Expanded && HasChildRows(row)){SendKey(37);return true;}
   if(state!=ExpandCollapseState.Expanded){
    TraceBrowserSelection(tree,"before expand",logger);
    // Use Live's native tree navigation. The accessibility Expand() action changed
    // selection/scrolling in the tested Live version, unlike keyboard Right.
    if(!SendKey(39))return true;
    if(canLoadEmpty){
     var expandedAt=Stopwatch.StartNew();
     var populated=WaitForChildren(()=>{var current=address==null?null:address.Resolve(tree);return current!=null && HasChildRows(current);},ready,()=>expandedAt.ElapsedMilliseconds,System.Threading.Thread.Sleep);
     if(populated==ChildProbe.Cancelled || !PreserveExpandedSelection(tree,address,ready,logger))return true;
     logger("Browser expanded plugin: "+(address==null?"unknown":address.Path)+"; "+populated+"; elapsed ms="+expandedAt.ElapsedMilliseconds);
     TraceBrowserSelection(tree,"after expand",logger);
     return populated!=ChildProbe.Empty;
    }
    return true;
   }
   if(!canLoadEmpty)return true;
   var watch=Stopwatch.StartNew();
   var result=WaitForChildren(()=>HasChildRows(row),valid,()=>watch.ElapsedMilliseconds,System.Threading.Thread.Sleep);
   logger("Browser preset branch: "+row.Current.Name+"; "+result+"; elapsed ms="+watch.ElapsedMilliseconds);
   return result!=ChildProbe.Empty;
  }
  // Live versions without ExpandCollapsePattern still implement standard tree keys.
  if(EmptyPluginCanLoad(tree,row)){
   if(!valid())return true;
   if(HasChildRows(row)){SendKey(37);return true;}
   var address=RowAddress.Capture(tree,row);
   if(address==null)return true;
   if(!SendKey(39))return true;
   var watch=Stopwatch.StartNew();
   var result=WaitForChildren(()=>{var current=address==null?null:address.Resolve(tree);return current!=null && HasChildRows(current);},ready,()=>watch.ElapsedMilliseconds,System.Threading.Thread.Sleep);
   if(!PreserveExpandedSelection(tree,address,ready,logger))return true;
   logger("Browser preset branch: "+row.Current.Name+"; "+result+"; elapsed ms="+watch.ElapsedMilliseconds);
   return result!=ChildProbe.Empty;
  }
  int count=tree.FindAll(TreeScope.Descendants,Condition.TrueCondition).Count;
  if(!ready())return true;SendKey(39);
  System.Threading.Thread.Sleep(40);
  if(!ready())return true;
  var after=Selected(tree);
  if(after!=null && !Automation.Compare(row,after)){SendKey(37);SendKey(37);return true;}
  return tree.FindAll(TreeScope.Descendants,Condition.TrueCondition).Count!=count;
 }
 static bool SendKey(int key){
  var events=new Input[2];for(int i=0;i<2;i++){events[i].type=1;events[i].data.keyboard.key=(ushort)key;events[i].data.keyboard.flags=(uint)(i==0?0:2);}
  return SendInput(2,events,Marshal.SizeOf(typeof(Input)))==2;
 }
 static string lastReason="";static BrowserFocus lastArea=BrowserFocus.None;static DateTime nextInspect;
 public static void Send(int key,Action<string> logger,Func<bool> ready,Action exit,Action<bool> favorite){
  if(!Allowed(key) && key!=257)return;
  IntPtr window=GetForegroundWindow();uint pid;
  if(window==IntPtr.Zero)return;
  bool clicked=key==257 && (GetAsyncKeyState(1)&0x8001)!=0;
  if(key==257 && !clicked && DateTime.UtcNow<nextInspect)return;
  if(key==257)nextInspect=DateTime.UtcNow.AddMilliseconds(500);
  GetWindowThreadProcessId(window,out pid);
  try{using(var process=Process.GetProcessById((int)pid)){
   if(!TargetAllowed(process.ProcessName,process.MainWindowHandle,window)){if(clicked && ready())exit();else if(key!=257)Report("Browser keys ignored: Live main window is not in front",logger);return;}
  }}catch(ArgumentException){return;}catch(System.ComponentModel.Win32Exception){return;}
  foreach(int modifier in new[]{16,17,18,91,92})if((GetAsyncKeyState(modifier)&0x8000)!=0)return;
  // Recheck focus after inspecting the target process.
  if(GetForegroundWindow()!=window)return;
  try {
   var root=AutomationElement.FromHandle(window);
   var browser=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"ContentBrowser"));
   if(browser==null){if(key==257)exit();else Report("Browser focus unavailable: browser is hidden",logger);return;}
   if(key==257){
    Point cursor;if(clicked && GetCursorPos(out cursor) && !browser.Current.BoundingRectangle.Contains(cursor.X,cursor.Y)){if(ready())exit();return;}
    var selection=Selected(FindTree(browser,BrowserFocus.Contents));favorite(selection!=null && LiveFavorites.Contains(selection.Current.Name));return;
   }
   var focused=AutomationElement.FocusedElement;
   BrowserFocus area=FocusArea(focused);
   if(area==BrowserFocus.Outside){
    var candidates=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.HasKeyboardFocusProperty,true));
    foreach(AutomationElement candidate in candidates){BrowserFocus found=FocusArea(candidate);if(found!=BrowserFocus.Outside){focused=candidate;area=found;}}
   }
   if(area!=lastArea){lastArea=area;logger("Browser focus detected: "+area+" ["+(focused==null?"none":focused.Current.Name+"; "+focused.Current.AutomationId)+"]");}
   BrowserFocus target=FocusTarget(area,key);
   if(area==BrowserFocus.Outside && key!=256){if(ready())exit();return;}
   if(target!=BrowserFocus.None){
    AutomationElement tree=FindTree(browser,target);
    if(tree==null || !tree.Current.IsKeyboardFocusable){Report("Browser focus unavailable: "+target,logger);return;}
    if(!ready() || GetForegroundWindow()!=window)return;
    tree.SetFocus();
   }
   if(key==256 || key==37 || key==13 && area!=BrowserFocus.Contents){Report("Browser focus: "+area+" -> "+target,logger);return;}
   if(!ready() || GetForegroundWindow()!=window)return;
   if(IsJump(key)){JumpSelected(FindTree(browser,area),key-1024,()=>ready() && GetForegroundWindow()==window,logger);return;}
   var content=FindTree(browser,BrowserFocus.Contents);
   if(key==13){
    var selected=Selected(content);if(selected==null)return;
    // Fast jumps already align focus when they select a row. Re-focusing it here
    // can scroll Live's virtualized tree again even when selection has not changed.
    TraceBrowserSelection(content,"before activation",logger);
    if(!ready() || GetForegroundWindow()!=window)return;
    var current=Selected(content);if(current==null || !Automation.Compare(selected,current))return;
    if(ExpandSelected(content,()=>ready() && GetForegroundWindow()==window,logger))return;
   }
   if(key==49 && (area!=BrowserFocus.Contents || Selected(content)==null))return;
  } catch(ElementNotAvailableException){Report("Browser focus unavailable: element was replaced",logger);return;}
    catch(InvalidOperationException error){Report("Browser focus unavailable: "+error.Message,logger);return;}
    catch(System.Runtime.InteropServices.COMException error){Report("Browser focus unavailable: "+error.Message,logger);return;}
  if(!ready() || GetForegroundWindow()!=window)return;
  bool sent=SendKey(key);
  Report(sent?"Browser keyboard input active":"Browser SendInput failed: "+Marshal.GetLastWin32Error(),logger);
  if(sent && key==13 && ready())exit();
 }
}
