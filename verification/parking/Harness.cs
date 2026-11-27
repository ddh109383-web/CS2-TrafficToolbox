// Executes the production parking system against an in-memory ECS substitute.
// Tests rule/ownership semantics only, not Unity jobs or native pathfinding.
using System;
using System.Collections.Generic;
using Unity.Entities;
using Game.Net;
using Game.Common;
using Game.Prefabs;
using RoadLaneLock;
class Program {
 static int checks; static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
 static void Main(){
  var p=new ParkingSystem();var em=p.EntityManager;Entity road=new Entity(1),lane=new Entity(2),prefab=new Entity(3);
  em.SetComponentData(road,new Road());em.SetComponentData(lane,new Owner{m_Owner=road});em.SetComponentData(lane,new ParkingLane());em.SetComponentData(lane,new PrefabRef{m_Prefab=prefab});em.SetComponentData(prefab,new ParkingLaneData{m_RoadTypes=RoadTypes.Car});
  var subs=em.GetBuffer<SubLane>(road);subs.Add(new SubLane{m_SubLane=lane});
  bool Disabled()=> (em.GetComponentData<ParkingLane>(lane).m_Flags&ParkingLaneFlags.ParkingDisabled)!=0;
  void NativeFlags(ParkingLaneFlags flags)=>em.SetComponentData(lane,new ParkingLane{m_Flags=flags});
  Check(p.Eligible(lane,road),"Street parking must not require CarLane");
  p.SetRule(road,true);p.Tick();Check(Disabled()&&p.Count==1,"enable");
  p.SetRule(road,true);p.Tick();Check(p.Count==1,"idempotent request");
  NativeFlags(ParkingLaneFlags.AllowExit);p.Tick();Check(Disabled(),"reapply after native recalculation");
  p.BeforeSave();Check(!Disabled(),"serialized state must exclude temporary restriction");p.Tick();Check(!Disabled(),"no reapply while saving");p.AfterSave();Check(Disabled(),"restore live restriction after save");
  p.SetRule(road,false);p.Tick();Check(!Disabled(),"restore owned flag");Check((em.GetComponentData<ParkingLane>(lane).m_Flags&ParkingLaneFlags.AllowExit)!=0,"preserve unrelated bits");
  NativeFlags(ParkingLaneFlags.ParkingDisabled);p.SetRule(road,true);p.Tick();p.SetRule(road,false);p.Tick();Check(Disabled(),"preserve native preexisting ban");
  NativeFlags(ParkingLaneFlags.VirtualLane);Check(!p.Eligible(lane,road),"virtual excluded");
  NativeFlags(ParkingLaneFlags.SpecialVehicles);Check(!p.Eligible(lane,road),"special excluded");
  NativeFlags(0);em.SetComponentData(lane,new Owner{m_Owner=new Entity(99)});Check(!p.Eligible(lane,road),"building/garage ownership excluded");em.SetComponentData(lane,new Owner{m_Owner=road});
  p.SetRule(road,true);p.Tick();subs.Clear();p.Tick();Check(!Disabled(),"release replaced sublane");
  subs.Add(new SubLane{m_SubLane=lane});p.Tick();Check(Disabled(),"reapply to current sublane");
  em.AddComponent<Deleted>(road);p.Tick();Check(p.Count==0&&!Disabled(),"deleted road clears rule and ownership");
  em.Remove<Deleted>(road);p.SetRule(road,true);p.Tick();p.Shutdown();Check(!Disabled()&&p.Count==0&&!p.Enabled,"shutdown restores");
  p.Enabled=true;p.SetRule(road,true);p.Tick();
  var tape=new Tape();p.BeforeSave();p.Serialize(tape);p.AfterSave();Check(Disabled(),"live ban restored after save");
  var loaded=new ParkingSystem();loaded.EntityManager=em;NativeFlags(0);loaded.Deserialize(tape);loaded.Load();Check(loaded.Count==1&&loaded.Contains(road),"chosen road survives save/load");
  loaded.Tick();Check(Disabled(),"ban reapplied after load");
  loaded.SetDefaults(default);Check(loaded.Count==0,"save without parking data starts empty");loaded.Shutdown();
  p.Shutdown();p.Enabled=true;Mod.SupportedGame=false;p.SetRule(road,true);Check(p.Count==0,"unsupported game guarded");
  Console.WriteLine("PASS: "+checks+" production-system ownership/lifecycle checks (ECS substitute; live simulation pending)");
 }
}
namespace Unity.Entities {
 public readonly record struct Entity(int Index,int Version=1){public static readonly Entity Null=new(0,0);}
 public sealed class EntityManager {
  readonly Dictionary<(Entity,Type),object> data=new();
  public bool HasComponent<T>(Entity e)=>data.ContainsKey((e,typeof(T)));
  public T GetComponentData<T>(Entity e)=>(T)data[(e,typeof(T))];
  public void SetComponentData<T>(Entity e,T value)=>data[(e,typeof(T))]=value;
  public void AddComponent<T>(Entity e)where T:new()=>SetComponentData(e,new T());
  public void Remove<T>(Entity e)=>data.Remove((e,typeof(T)));
  public bool HasBuffer<T>(Entity e)=>HasComponent<List<T>>(e);
  public List<T> GetBuffer<T>(Entity e,bool readOnly=false){if(!HasBuffer<T>(e))SetComponentData(e,new List<T>());return GetComponentData<List<T>>(e);}
 }
 public class World{public T GetExistingSystemManaged<T>()where T:class=>null;}
}
namespace Colossal.Serialization.Entities{public enum Purpose{}public struct Context{}public interface ISerializable{}public interface IDefaultSerializable{}
 public interface IWriter{void Write(int v);void Write(Unity.Entities.Entity e);}public interface IReader{void Read(out int v);void Read(out Unity.Entities.Entity e);}}
class Tape:Colossal.Serialization.Entities.IWriter,Colossal.Serialization.Entities.IReader{readonly Queue<object> q=new();public void Write(int v)=>q.Enqueue(v);public void Write(Entity e)=>q.Enqueue(e);public void Read(out int v)=>v=(int)q.Dequeue();public void Read(out Entity e)=>e=(Entity)q.Dequeue();}
namespace Game {
 public enum GameMode{}
 public abstract class GameSystemBase {
  public EntityManager EntityManager=new();public World World=new();public bool Enabled=true;
  protected abstract void OnUpdate();public void Tick()=>OnUpdate();
  protected virtual void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose p,GameMode m){}
  public void Load()=>OnGameLoadingComplete(default,default);
 }
}
namespace Game.Common {public struct Owner{public Entity m_Owner;}public struct Deleted{}public struct Updated{}public struct Created{}public struct PathfindUpdated{}}
namespace Game.Tools {public struct Temp{}}
namespace Game.Net {
 public struct Road{}public struct ParkingLane{public ParkingLaneFlags m_Flags;}public struct SubLane{public Entity m_SubLane;}
 [Flags]public enum ParkingLaneFlags {VirtualLane=0x200,ParkingDisabled=0x2000,AllowExit=0x8000,SpecialVehicles=0x10000}
 [Flags]public enum RoadTypes{Car=1}
}
namespace Game.Prefabs {public struct PrefabRef{public Entity m_Prefab;}public struct ParkingLaneData{public RoadTypes m_RoadTypes;}}
namespace RoadLaneLock {
 static class RoadInspector{public static bool IsRoad(EntityManager em,Entity e)=>em.HasComponent<Road>(e)&&!em.HasComponent<Deleted>(e);}
 static class Mod{public static bool SupportedGame=true;public static Logger Log=new();}
 class Logger{public void Info(string text){}}
 static class Diagnostics{public static void Event(string k,object d=null){}}
}

