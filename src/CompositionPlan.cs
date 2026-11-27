using System;
using System.Collections.Generic;
using Game.Prefabs;

namespace RoadLaneLock
{
 // Pure transformation: never modifies a shared road prefab or reindexes real lanes.
 public static class CompositionPlan
 {
  public static NetCompositionLane[] Create(NetCompositionLane[] input)
  {
   var result=new List<NetCompositionLane>();int roads=0;
   foreach(var original in input)
   {
    var lane=original;
    if((lane.m_Flags&LaneFlags.Road)!=0)
    {
     if((lane.m_Flags&(LaneFlags.Track|LaneFlags.BicyclesOnly))!=0)
      throw new InvalidOperationException("首版暂不支持电车混行或自行车专用车道");
     if((lane.m_Flags&LaneFlags.Master)!=0)continue;
     lane.m_Flags&=~LaneFlags.Slave;
     roads++;
    }
    result.Add(lane);
   }
   if(roads<2)throw new InvalidOperationException("请选择含多条机动车道的道路段");
   return result.ToArray();
  }
 }
}
