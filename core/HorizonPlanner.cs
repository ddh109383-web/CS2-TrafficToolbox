using System;
using System.Collections.Generic;

namespace RoadLaneLock.Core
{
 // A route horizon contains candidate physical lanes for each consecutive slot.
 // The caller supplies legal transitions (including vehicle restrictions). Costs
 // rank legal alternatives only; they can never turn a forbidden move into one.
 public sealed class HorizonCandidate
 {
  public int Lane;
  public double Cost;
 }
 public sealed class HorizonPlan
 {
  public bool Reachable;
  public int[] Lanes = Array.Empty<int>();
  public double Cost;
 }
 public static class HorizonPlanner
 {
  public static HorizonPlan Solve(IReadOnlyList<HorizonCandidate[]> slots,
   Func<int,int,bool> mayTransition, Func<int,bool> mayEnter)
  {
   if(slots == null || mayTransition == null || mayEnter == null)throw new ArgumentNullException();
   if(slots.Count == 0 || slots.Count > 32)return new HorizonPlan();
   var costs=new double[slots.Count][];
   var next=new int[slots.Count][];
   for(int i=slots.Count-1;i>=0;i--)
   {
    var row=slots[i];
    if(row == null || row.Length == 0 || row.Length > 128)return new HorizonPlan();
    costs[i]=new double[row.Length];next[i]=new int[row.Length];
    for(int j=0;j<row.Length;j++)
    {
     costs[i][j]=double.PositiveInfinity;next[i][j]=-1;
     var candidate=row[j];
     if(candidate == null || double.IsNaN(candidate.Cost) || double.IsInfinity(candidate.Cost) || candidate.Cost<0)continue;
     if(i == slots.Count-1){costs[i][j]=candidate.Cost;continue;}
     for(int k=0;k<slots[i+1].Length;k++)
     {
      if(double.IsInfinity(costs[i+1][k]) || !mayTransition(candidate.Lane,slots[i+1][k].Lane))continue;
      double cost=candidate.Cost+costs[i+1][k];
      if(cost<costs[i][j] || (cost==costs[i][j] && next[i][j]>=0 && slots[i+1][k].Lane<slots[i+1][next[i][j]].Lane))
      {costs[i][j]=cost;next[i][j]=k;}
     }
    }
   }
   int selected=-1;double best=double.PositiveInfinity;
   for(int i=0;i<slots[0].Length;i++)
   {
    if(double.IsInfinity(costs[0][i]) || !mayEnter(slots[0][i].Lane))continue;
    if(costs[0][i]<best || (costs[0][i]==best && selected>=0 && slots[0][i].Lane<slots[0][selected].Lane))
    {selected=i;best=costs[0][i];}
   }
   if(selected<0)return new HorizonPlan();
   var path=new int[slots.Count];
   for(int i=0;i<slots.Count;i++){path[i]=slots[i][selected].Lane;selected=next[i][selected];}
   return new HorizonPlan{Reachable=true,Lanes=path,Cost=best};
  }
 }
}
