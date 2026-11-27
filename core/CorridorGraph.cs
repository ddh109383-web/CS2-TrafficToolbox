using System;
using System.Collections.Generic;

namespace RoadLaneLock.Core
{
 // Pure policy. No game entities are mutated by this module.
 public sealed class LaneLink
 {
  public int Id, Owner;
  public string Start, End;
  public bool IsMaster, Unsupported;
 }
 public sealed class CorridorGraph
 {
  readonly Dictionary<int,LaneLink> lanes=new Dictionary<int,LaneLink>();
  readonly Dictionary<string,List<LaneLink>> starts=new Dictionary<string,List<LaneLink>>(StringComparer.Ordinal);
  readonly HashSet<int> owners;
  public CorridorGraph(IEnumerable<LaneLink> source,IEnumerable<int> protectedOwners)
  {
   owners=new HashSet<int>(protectedOwners);
   foreach(var lane in source)
   {
    if(lane.IsMaster)continue; // Abstract group routes cannot prove lane continuity.
    if(lane.Start==null||lane.End==null)throw new ArgumentException("Missing lane endpoints");
    if(lanes.ContainsKey(lane.Id))throw new ArgumentException("Duplicate lane identity");
    lanes.Add(lane.Id,lane);
    if(!starts.TryGetValue(lane.Start,out var list))starts.Add(lane.Start,list=new List<LaneLink>());
    list.Add(lane);
   }
  }
  public bool CanChangeLaterally(int currentLane,int targetLane)
  {
   if(!lanes.TryGetValue(currentLane,out var current)||!lanes.TryGetValue(targetLane,out var target))return false;
   if(current.Unsupported||target.Unsupported)return false;
   if(currentLane==targetLane)return true;
   // Call only for lateral transitions, never for normal end-to-start turn connections.
   return !owners.Contains(current.Owner)&&!owners.Contains(target.Owner);
  }
  public List<int[]> ExitPaths(int entryLane)
  {
   var result=new List<int[]>();
   if(!lanes.TryGetValue(entryLane,out var entry)||!owners.Contains(entry.Owner)||entry.Unsupported)return result;
   var queue=new Queue<int[]>();queue.Enqueue(new[]{entryLane});
   var visited=new HashSet<int>{entryLane};
   while(queue.Count>0)
   {
    var path=queue.Dequeue();var tail=lanes[path[path.Length-1]];
    if(!starts.TryGetValue(tail.End,out var next))continue;
    foreach(var lane in next)
    {
     if(lane.Unsupported||visited.Contains(lane.Id))continue;
     visited.Add(lane.Id);
     var extended=new int[path.Length+1];Array.Copy(path,extended,path.Length);extended[path.Length]=lane.Id;
     if(!owners.Contains(lane.Owner))result.Add(extended);else queue.Enqueue(extended);
    }
   }
   return result;
  }
  public Decision Decide(int entryLane,int desiredExitOwner)
  {
   var paths=ExitPaths(entryLane);
   foreach(var path in paths)if(lanes[path[path.Length-1]].Owner==desiredExitOwner)
    return new Decision{Kind="Continue",Path=path};
   // Remain on a legal connected route; downstream global re-routing is a separate runtime operation.
   // Deterministic fallback avoids jitter. Runtime may rank ONLY these already legal options.
   paths.Sort((a,b)=>a[a.Length-1].CompareTo(b[b.Length-1]));
   return paths.Count>0?new Decision{Kind="ExitThenReroute",Path=paths[0]}:new Decision{Kind="NoVerifiedExit",Path=Array.Empty<int>()};
  }
  public List<int> EligibleEntries(IEnumerable<int> entries,int desiredExitOwner)
  {
   var result=new List<int>();
   foreach(var entry in entries)if(Decide(entry,desiredExitOwner).Kind=="Continue")result.Add(entry);
   return result;
  }
 }
 public sealed class Decision {public string Kind;public int[] Path;}
}
