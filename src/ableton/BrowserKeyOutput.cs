using System;
using System.Collections.Generic;
using System.Threading.Tasks;

sealed class BrowserKeyOutput : IDisposable {
 readonly Queue<int> pending=new Queue<int>();readonly Queue<int> results=new Queue<int>();readonly Func<bool> ready;readonly Action<string> log;Task worker;bool disposed,closing;int generation;
 public bool Enabled;public volatile bool Favorite;
 public BrowserKeyOutput(Func<bool> canSend,Action<string> logger){ready=canSend;log=logger;}
 public void Post(int key){lock(pending){if(disposed || pending.Count>=32)return;pending.Enqueue(key);if(worker==null || worker.IsCompleted)worker=Task.Factory.StartNew(Run);}}
 bool Ready(){lock(pending)return !disposed && !closing && ready();}
 public void NewSession(){lock(pending){generation++;pending.Clear();results.Clear();Favorite=false;closing=false;}}
 public bool TryResult(out int result){lock(pending){result=0;if(results.Count==0)return false;result=results.Dequeue();return true;}}
 void Run(){for(;;){int key,version;lock(pending){if(disposed || pending.Count==0){worker=null;return;}key=pending.Dequeue();version=generation;}Func<bool> valid=()=>Ready() && version==generation;
  try{if(valid())LiveBrowserKeys.Send(key,log,valid,()=>{lock(pending){if(valid() && results.Count==0){results.Enqueue(1);closing=true;pending.Clear();}}},value=>{if(valid())Favorite=value;});}catch(Exception error){log("Browser keyboard unavailable: "+error.Message);}}}
 public void Dispose(){Task task;lock(pending){disposed=true;pending.Clear();task=worker;}if(task!=null)task.Wait(500);}
}
