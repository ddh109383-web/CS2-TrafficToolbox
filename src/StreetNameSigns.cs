using System;
using Colossal.Serialization.Entities;
using Game;
using Unity.Entities;
namespace RoadLaneLock
{
 // The street-name sign feature was removed. This stub only keeps older city saves loadable:
 // it reads and discards the corner list they contain and writes an empty one.
 public sealed class StreetNameSigns:GameSystemBase,ISerializable,IDefaultSerializable
 {
  protected override void OnUpdate(){}
  public void Serialize<W>(W writer) where W:IWriter{writer.Write(1);writer.Write(0);}
  public void Deserialize<R>(R reader) where R:IReader
  {
   reader.Read(out int version);if(version!=1)return;
   reader.Read(out int count);if(count<0||count>100000)return;
   for(int i=0;i<count;i++){reader.Read(out Entity node);reader.Read(out Entity a);reader.Read(out Entity b);}
  }
  public void SetDefaults(Context context){}
 }
}
