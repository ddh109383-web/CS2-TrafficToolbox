using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RoadLaneLock.Core;
class Program
{
 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static LaneLink L(int id,int owner,string start,string end,bool master=false,bool unsupported=false)=>new LaneLink{Id=id,Owner=owner,Start=start,End=end,IsMaster=master,Unsupported=unsupported};
 static void Main(string[] args)
 {
  HorizonChecks.Run(Check);
  var exits=new CompletedExits<int>();
  Check(exits.Remember(10,20)&&exits.Contains(10,20),"remember the exit physically completed by the vehicle");
  Check(!exits.Remember(10,20)&&exits.Count==1,"duplicate observations do not consume a retry");
  Check(!exits.Contains(20,10)&&!exits.Contains(11,20),"a different directed connection remains eligible");
  Check(exits.Remember(11,20)&&exits.Remember(12,21),"two distinct alternatives can be tried");
  Check(!exits.Remember(13,22)&&exits.Count==3&&exits.Contains(10,20),"three completed exits retain the first failure");
  Check(NavigationSafety.ExtendsQueue(28f,12f,21f),"a slow vehicle within seven metres extends the queue");
  Check(!NavigationSafety.ExtendsQueue(28f,3f,11f),"isolated upstream traffic is not spillback");
  Check(!NavigationSafety.ExtendsQueue(16f,19f,27f),"vehicle ahead cannot extend rear queue");
  Check(!NavigationSafety.QueueAtEntrance(false,2f),"a lone turning vehicle does not trigger spillback");
  Check(!NavigationSafety.QueueAtEntrance(true,12f),"short queue still leaves upstream room");
  Check(NavigationSafety.QueueAtEntrance(true,6f),"continuous queue occupies entrance");
  Check(!NavigationSafety.WaitedAtTurn(true,2.49),"short queue does not reroute a feasible turn");
  Check(NavigationSafety.WaitedAtTurn(true,2.5),"persistent physical blocker enables turn recovery");
  Check(!NavigationSafety.WaitedAtTurn(false,1424),"elapsed time alone cannot reroute a moving vehicle");
  for(int mask=0;mask<=3;mask++){
   Check(((mask&NavigationSafety.Direction(0.1f,0.9f))!=0)==((mask&2)!=0),"toward end only");
   Check(((mask&NavigationSafety.Direction(1f,0.5f))!=0)==((mask&1)!=0),"toward start only");
   Check((mask&NavigationSafety.Direction(0.5f,0.5f))==0,"stationary delta");
   Check(NavigationSafety.RestoreMask(mask)==(mask==0?3:mask),"legacy and directional saves");
  }
  Check(NavigationSafety.ReleaseStaleAccessDetour(true,false,false),"captured bicycle on side connector outside old detour must return to native access");
  Check(!NavigationSafety.ReleaseStaleAccessDetour(false,false,false),"ordinary off-route vehicle does not receive access exemption");
  Check(!NavigationSafety.ReleaseStaleAccessDetour(true,false,true),"access recovery does not interrupt ongoing lane change");
  Check(!NavigationSafety.ReleaseStaleAccessDetour(true,true,false),"valid stored route is not discarded");
  using(var accessCase=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"access-detour-regression.json"))))
  {
   var evidence=accessCase.RootElement;var vehicle=evidence.GetProperty("Vehicle");var current=vehicle.GetProperty("Lane");
   bool onRoute=evidence.GetProperty("Detour").GetProperty("Lanes").EnumerateArray().Any(l=>l.GetProperty("Index").GetInt32()==current.GetProperty("Index").GetInt32()&&l.GetProperty("Version").GetInt32()==current.GetProperty("Version").GetInt32());
   Check(evidence.GetProperty("StationarySeconds").GetDouble()>2400&&vehicle.GetProperty("Navigation").GetArrayLength()==0,"captured prolonged empty-navigation stall fixture");
   Check(NavigationSafety.ReleaseStaleAccessDetour(evidence.GetProperty("Lane").GetProperty("Flags").GetString().Contains("SideConnection"),onRoute,vehicle.GetProperty("ChangeLane").GetProperty("Index").GetInt32()!=0),"recorded off-route side connector takes native access recovery");
  }
  var fallback=new ServiceFallback();
  for(int i=0;i<100;i++)fallback.ObserveAttempt(false);
  Check(!fallback.Native&&fallback.Failures==0,"waiting in traffic does not count as failure");
  fallback.Fail();
  for(int i=0;i<100;i++){fallback.ObserveAttempt(false);fallback.Fail();}
  Check(fallback.Failures==1&&!fallback.Native,"persistent failed result counts once");
  fallback.ObserveAttempt(true);fallback.ObserveAttempt(true);fallback.ObserveAttempt(false);fallback.Fail();
  Check(fallback.Native&&fallback.Failures==2,"second independent failure enables fallback");
  fallback.Accept();Check(fallback.Native,"native success cannot immediately revoke fallback");
  fallback=default;fallback.Fail();fallback.ObserveAttempt(true);fallback.ObserveAttempt(false);fallback.Accept();
  Check(fallback.Failures==0&&!fallback.Native,"accepted constrained path resets consecutive failures");
  fallback.ObserveAttempt(true);fallback.ObserveAttempt(false);fallback.Fail();
  Check(fallback.Failures==1&&!fallback.Native,"fail-success-fail does not enable fallback");
  fallback=default;fallback.ObserveAttempt(true);fallback.Fail();fallback.ObserveAttempt(true);fallback.Fail();
  Check(fallback.Failures==1,"pending and updated belong to one attempt");
  fallback=default;Check(!fallback.Native&&fallback.Failures==0,"new scope starts restricted");
  const uint unsafeBit=1,forbiddenBit=2,publicBit=4;
  Check(NavigationSafety.RecoveryForbiddenMask(7,unsafeBit,true,true)==(forbiddenBit|publicBit),"occupied U-turn can finish but other prohibitions remain");
  Check(NavigationSafety.RecoveryForbiddenMask(7,unsafeBit,false,true)==7,"new unsafe U-turn still rejected");
  Check(NavigationSafety.RecoveryForbiddenMask(7,unsafeBit,true,false)==7,"recovery does not waive unrelated unsafe lanes");
  Check(NavigationSafety.RecoveryForbiddenMask(6,unsafeBit,true,true)==6,"recovery never adds admission flags");
  Check(NavigationSafety.RejectReversal(true,true,false,false,-0.999f),"recorded opposite-carriageway jump is rejected");
  Check(!NavigationSafety.RejectReversal(true,true,false,false,0.999f),"same-direction lane changes outside lock remain allowed");
  Check(!NavigationSafety.RejectReversal(true,false,false,false,-1f),"junction turn on a separate owner is not a midroad reversal");
  Check(!NavigationSafety.RejectReversal(true,true,true,false,-1f),"explicit connected U-turn remains allowed");
  Check(!NavigationSafety.RejectReversal(false,true,false,false,-1f),"unrelated city roads remain unaffected");
  Check(!NavigationSafety.RejectReversal(true,true,false,true,-1f),"ongoing lane change is not abruptly cancelled");
  var serviceExit=new CorridorGraph(new[]{
   L(1390727,1807032,"00215EB900000002","00215EBA00000007"),
   L(123090,2186938,"00215EBA00000007","0002163C00000005"),
   L(123919,215212,"0002163C00000005","000348AC00000207")},new[]{1807032,2186938,136764});
  Check(serviceExit.Decide(1390727,999).Path.SequenceEqual(new[]{1390727,123090,123919}),"captured service exit crosses a node without internal lanes");
  Check(!NavigationSafety.CanHandoff(false,false,true),"trailer must clear before handoff");
  Check(!NavigationSafety.CanHandoff(true,true,true),"recorded first exit retains verified continuation");
  Check(NavigationSafety.CanHandoff(true,false,true),"handoff outside local reversal guard");
  Check(NavigationSafety.CanHandoff(true,true,false),"short route endpoint cannot hold forever");
  Check(!NavigationSafety.CanHandoff(false,true,false),"short route still requires vehicle clearance");
  var recording=new RecordingWindow<int>();
  Check(!recording.Include(1,false,0),"unrelated vehicle excluded");
  Check(recording.Include(1,true,1),"local vehicle admitted");
  Check(recording.Include(1,false,120),"departed vehicle followed");
  Check(!recording.Include(1,false,122),"follow expires");
  Check(recording.Include(1,true,123),"reentry restarts follow");
  Check(!recording.Include(2,false,124),"other identity excluded");
  recording.Clear();Check(!recording.Include(1,false,125),"new session clears tracking");
  recording.Include(1,true,0);recording.Prune(121);Check(!recording.Include(1,false,121),"expired identities pruned");
  var sample=new[]{L(1,10,"a","b"),L(2,10,"c","d"),L(3,20,"b","e"),L(4,20,"d","f"),L(5,30,"e","g"),L(6,40,"f","h"),L(7,50,"x","a"),L(8,50,"y","c"),L(9,10,"b","f",true)};
  var graph=new CorridorGraph(sample,new[]{10,20});
  Check(!graph.CanChangeLaterally(1,2),"protected lateral move forbidden");
  Check(graph.CanChangeLaterally(7,8),"upstream remains changeable");
  Check(!graph.CanChangeLaterally(7,2),"boundary crossing maneuver not admitted");
  Check(graph.Decide(1,30).Kind=="Continue","real turn connection preserved");
  Check(graph.Decide(1,40).Kind=="ExitThenReroute","wrong lane chooses legal exit");
  Check(graph.Decide(1,40).Path.SequenceEqual(new[]{1,3,5}),"fallback does not use master shortcut");
  Check(graph.EligibleEntries(new[]{1,2},40).SequenceEqual(new[]{2}),"advance lane selection constrained by exit");
  Check(graph.Decide(999,40).Kind=="NoVerifiedExit","stale identity does not invent route");
  var cycle=new CorridorGraph(new[]{L(1,10,"a","b"),L(2,10,"b","a")},new[]{10});
  Check(cycle.Decide(1,30).Kind=="NoVerifiedExit","cycles terminate without false exit");
  var unsupported=new CorridorGraph(new[]{L(1,10,"a","b"),L(2,20,"b","c",false,true),L(3,30,"c","d")},new[]{10,20});
  Check(unsupported.Decide(1,30).Kind=="NoVerifiedExit","unsupported side connection is not assumed continuous");
  using var doc=JsonDocument.Parse(File.ReadAllText(args[0]));var root=doc.RootElement;
  int selected=root.GetProperty("Selected").GetProperty("Index").GetInt32();
  int node=root.GetProperty("StartNode").GetProperty("Index").GetInt32();
  var lanes=new List<LaneLink>();var approach=new List<int>();var flags=new Dictionary<int,string>();
  foreach(var owner in root.GetProperty("Owners").EnumerateArray())
  {
   int id=owner.GetProperty("Entity").GetProperty("Index").GetInt32();
   foreach(var lane in owner.GetProperty("Lanes").EnumerateArray())
   {
    var f=lane.GetProperty("Flags").GetString();int laneId=lane.GetProperty("Entity").GetProperty("Index").GetInt32();
    bool master=lane.GetProperty("Master").ValueKind!=JsonValueKind.Null;
    bool unsupportedLane=f.Contains("SideConnection")||lane.GetProperty("Slave").ToString().Contains("MiddleStart")||lane.GetProperty("Slave").ToString().Contains("MiddleEnd");
    lanes.Add(L(laneId,id,lane.GetProperty("Start").GetString(),lane.GetProperty("End").GetString(),master,unsupportedLane));flags[laneId]=f;
    if(id==selected&&!master&&f.Contains("Approach"))approach.Add(laneId);
   }
  }
  var actual=new CorridorGraph(lanes,new[]{selected,node});
  var rows=approach.Select(id=>new {Entry=id,Flags=flags[id],Exits=actual.ExitPaths(id).Select(path=>new {Owner=lanes.Single(l=>l.Id==path.Last()).Owner,Path=path}).ToArray()}).ToArray();
  Check(rows.Length==6,"captured approach has six real lane pieces");
  Check(rows.All(r=>r.Exits.Length==1),"every approach piece has one exact connected exit");
  foreach(var row in rows)
  {
   int intended=row.Flags.Contains("TurnLeft")?1731129:1730798;
   Check(row.Exits[0].Owner==intended,"snapshot straight/left connection remains unchanged");
   int other=intended==1731129?1730798:1731129;
   Check(actual.Decide(row.Entry,other).Kind=="ExitThenReroute","snapshot wrong-lane fallback does not cut across lanes");
  }
  Check(actual.EligibleEntries(approach,1731129).Count==2,"only the two pieces of left-turn lane qualify for left exit");
  string json=JsonSerializer.Serialize(new {Status="OFFLINE_POLICY_ONLY_NOT_INSTALLED_ENFORCEMENT",Checks=checks,Selected=selected,ProtectedNode=node,Rows=rows},new JsonSerializerOptions{WriteIndented=true});
  File.WriteAllText(args[1],json);Console.WriteLine($"PASS {checks} policy checks, including captured Traffic graph. NOT a live vehicle test.");
 }
}
