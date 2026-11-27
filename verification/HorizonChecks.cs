using System;
using System.Collections.Generic;
using System.Linq;
using RoadLaneLock.Core;
static class HorizonChecks
{
 static HorizonCandidate C(int lane,double cost=0)=>new HorizonCandidate{Lane=lane,Cost=cost};
 public static void Run(Action<bool,string> check)
 {
  var handoff=new ExitHandoff(7,new[]{1,2,3});
  check(handoff.Observe(7,1,false,false)==HandoffAction.FollowVerifiedExit,"do not replan inside protected corridor");
  check(handoff.Observe(7,3,false,false)==HandoffAction.WaitForWholeVehicle,"truck front exiting does not release trailer");
  check(handoff.Observe(7,3,true,false)==HandoffAction.RequestOriginalDestinationPath,"repath after entire vehicle exits");
  check(handoff.Observe(7,3,true,false)==HandoffAction.WaitForPath,"request emitted once only");
  check(handoff.Observe(7,3,true,true)==HandoffAction.Complete,"completion requires fresh accepted path");
  check(handoff.Observe(8,3,true,true)==HandoffAction.Invalidated,"road rebuild invalidates old handoff");
  check(handoff.Observe(7,3,true,true)==HandoffAction.Invalidated,"invalidated route cannot silently reactivate");
  var unexpected=new ExitHandoff(7,new[]{1,2,3});
  check(unexpected.Observe(7,9,true,false)==HandoffAction.Invalidated,"unrelated exit not mistaken for successful detour");
  // Three incoming lanes; only the inside lane continues into the requested turn.
  var slots=new[]{new[]{C(1),C(2),C(3,900000000)},new[]{C(11),C(12),C(13)},new[]{C(23)},new[]{C(33)}};
  var edges=new HashSet<(int,int)>{(1,11),(2,12),(3,13),(13,23),(23,33)};
  var p=HorizonPlanner.Solve(slots,(a,b)=>edges.Contains((a,b)),a=>true);
  check(p.Reachable&&p.Lanes.SequenceEqual(new[]{3,13,23,33}),"advance choice propagates from exit despite extreme congestion");
  p=HorizonPlanner.Solve(slots,(a,b)=>edges.Contains((a,b)),a=>a==1);
  check(!p.Reachable&&p.Lanes.Length==0,"wrong lane inside protected road cannot select forbidden turn");
  p=HorizonPlanner.Solve(new[]{new[]{C(1)},new[]{C(11)},new[]{C(21)}},(a,b)=>(a==1&&b==11)||(a==11&&b==21),a=>a==1);
  check(p.Reachable,"legal alternative exit can be planned separately");
  p=HorizonPlanner.Solve(new[]{new[]{C(1,double.NaN),C(2,double.PositiveInfinity),C(3,-1)}},(a,b)=>true,a=>true);
  check(!p.Reachable,"invalid costs cannot create path");
  p=HorizonPlanner.Solve(new[]{new[]{C(2),C(1)},new[]{C(4),C(3)}},(a,b)=>true,a=>true);
  check(p.Lanes.SequenceEqual(new[]{1,3}),"equal cost chooses deterministic physical lane IDs");
  check(!HorizonPlanner.Solve(new HorizonCandidate[0][],(a,b)=>true,a=>true).Reachable,"empty horizon is not success");
  check(!HorizonPlanner.Solve(new[]{new[]{C(1)},Array.Empty<HorizonCandidate>()},(a,b)=>true,a=>true).Reachable,"empty downstream group is unreachable");
  // Independent exhaustive oracle across randomized small graphs. Includes disconnected
  // graphs and allowed entry subsets; checks minimum cost and every chosen transition.
  var random=new Random(41728);
  for(int test=0;test<300;test++)
  {
   int depth=random.Next(1,6);var rows=new HorizonCandidate[depth][];
   var legal=new HashSet<(int,int)>();var entries=new HashSet<int>();
   for(int i=0;i<depth;i++)
   {
    rows[i]=Enumerable.Range(0,random.Next(1,5)).Select(j=>C(i*10+j,random.Next(0,25))).ToArray();
    foreach(var c in rows[i])if(i==0&&random.Next(2)==0)entries.Add(c.Lane);
    if(i>0)foreach(var a in rows[i-1])foreach(var b in rows[i])if(random.Next(3)>0)legal.Add((a.Lane,b.Lane));
   }
   double best=double.PositiveInfinity;
   void Walk(int level,int previous,double cost)
   {
    if(level==depth){best=Math.Min(best,cost);return;}
    foreach(var c in rows[level])if(level==0?entries.Contains(c.Lane):legal.Contains((previous,c.Lane)))Walk(level+1,c.Lane,cost+c.Cost);
   }
   Walk(0,0,0);
   var result=HorizonPlanner.Solve(rows,(a,b)=>legal.Contains((a,b)),a=>entries.Contains(a));
   check(result.Reachable==!double.IsInfinity(best),"oracle reachability "+test);
   if(result.Reachable)
   {
    check(result.Cost==best,"oracle cost "+test);
    check(entries.Contains(result.Lanes[0])&&Enumerable.Range(1,result.Lanes.Length-1).All(i=>legal.Contains((result.Lanes[i-1],result.Lanes[i]))),"no forbidden transition "+test);
   }
  }
 }
}
