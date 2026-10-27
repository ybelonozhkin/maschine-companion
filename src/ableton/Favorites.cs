using System;
using System.Collections.Generic;

// Live stores Collections in XMP metadata. Read it; assignments go through Live's
// own number-key command, so the browser and its database remain the authority.
static class LiveFavorites {
 static DateTime nextRead;static string folder;static readonly HashSet<string> names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 public static bool Contains(string name){
  if(DateTime.UtcNow>=nextRead){nextRead=DateTime.UtcNow.AddSeconds(1);Refresh();}
  return names.Contains(name);
 }
 public static IEnumerable<string> RedNames(System.Xml.XmlDocument document){
  foreach(System.Xml.XmlNode item in document.SelectNodes("//*[local-name()='filePath']")){
   var colors=item.ParentNode.SelectNodes("*[local-name()='colors']//*[local-name()='li']");
   foreach(System.Xml.XmlNode color in colors)if(color.InnerText=="1"){yield return System.IO.Path.GetFileName(item.InnerText.Replace('/',System.IO.Path.DirectorySeparatorChar));break;}
  }
 }
 static void Refresh(){
  try{
   if(folder==null){
    string root=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Ableton");Version latest=new Version(0,0);string library=null;
    if(System.IO.Directory.Exists(root))foreach(string dir in System.IO.Directory.GetDirectories(root,"Live *")){
     Version version;if(!Version.TryParse(System.IO.Path.GetFileName(dir).Substring(5),out version) || version<=latest)continue;
     string config=System.IO.Path.Combine(dir,"Preferences","Library.cfg");if(!System.IO.File.Exists(config))continue;
     var doc=new System.Xml.XmlDocument();doc.XmlResolver=null;doc.Load(config);var project=doc.SelectSingleNode("//UserLibrary/LibraryProject");if(project==null)continue;
     var path=project.SelectSingleNode("ProjectPath/@Value");var name=project.SelectSingleNode("ProjectName/@Value");if(path==null||name==null)continue;
     library=System.IO.Path.Combine(path.Value,name.Value);latest=version;
    }
    if(library==null)return;folder=System.IO.Path.Combine(library,"Ableton Folder Info");
   }
   if(!System.IO.Directory.Exists(folder))return;
   var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(string file in System.IO.Directory.GetFiles(folder,"*.xmp",System.IO.SearchOption.AllDirectories)){
    var document=new System.Xml.XmlDocument();document.XmlResolver=null;
    using(var stream=new System.IO.FileStream(file,System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.ReadWrite|System.IO.FileShare.Delete))document.Load(stream);
    foreach(string name in RedNames(document))found.Add(name);
   }
   names.Clear();names.UnionWith(found);
  }catch(System.IO.IOException){}catch(System.Xml.XmlException){}catch(UnauthorizedAccessException){}
 }
}
