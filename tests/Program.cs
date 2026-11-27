using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.Prefabs;
using RoadLaneLock;

class Program
{
 static void Main()
 {
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
   var path=Path.Combine(Environment.GetEnvironmentVariable("CSII_MANAGEDPATH",EnvironmentVariableTarget.User),new AssemblyName(e.Name).Name+".dll");
   return File.Exists(path)?Assembly.LoadFrom(path):null;
  };
  Run();
 }
 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 [MethodImpl(MethodImplOptions.NoInlining)] static void Run()
 {
  var input=new[]{
   new NetCompositionLane {m_Index=7,m_Group=2,m_Flags=LaneFlags.Road|LaneFlags.Master},
   new NetCompositionLane {m_Index=8,m_Group=2,m_Flags=LaneFlags.Road|LaneFlags.Slave},
   new NetCompositionLane {m_Index=9,m_Group=2,m_Flags=LaneFlags.Road|LaneFlags.Slave|LaneFlags.Invert},
   new NetCompositionLane {m_Index=10,m_Flags=LaneFlags.Pedestrian},
   new NetCompositionLane {m_Index=11,m_Flags=LaneFlags.Parking}
  };
  var output=CompositionPlan.Create(input);
  Check(output.Length==4,"remove only master placeholder");
  Check(output[0].m_Index==8&&output[1].m_Index==9,"preserve real lane identity");
  Check((output[0].m_Flags&(LaneFlags.Master|LaneFlags.Slave))==0,"individual forward lane");
  Check(output[1].m_Flags==(LaneFlags.Road|LaneFlags.Invert),"preserve reverse direction");
  Check(output[2].m_Flags==LaneFlags.Pedestrian&&output[3].m_Flags==LaneFlags.Parking,"preserve pedestrian and parking lanes");
  Check((input[1].m_Flags&LaneFlags.Slave)!=0,"source untouched");
  var again=CompositionPlan.Create(output);Check(again.Length==output.Length,"idempotent");
  input[1].m_Flags|=LaneFlags.Track;bool refused=false;
  try{CompositionPlan.Create(input);}catch(InvalidOperationException){refused=true;}Check(refused,"refuse shared track");
  Console.WriteLine(checks+" composition transformation checks passed (NOT an in-game traffic test)");
 }
}
