using System.Collections.Generic;
namespace RoadLaneLock.Core
{
 public sealed class RecordingWindow<T>
 {
  private readonly Dictionary<T,double> lastSeen=new Dictionary<T,double>();
  public bool Include(T id,bool inArea,double now)
  {
   if(inArea){lastSeen[id]=now;return true;}
   if(!lastSeen.TryGetValue(id,out var last))return false;
   if(now-last<=120)return true;
   lastSeen.Remove(id);return false;
  }
  public void Prune(double now)
  {
   var remove=new List<T>();
   foreach(var p in lastSeen)if(now-p.Value>120)remove.Add(p.Key);
   foreach(var id in remove)lastSeen.Remove(id);
  }
  public void Clear()=>lastSeen.Clear();
 }
}
