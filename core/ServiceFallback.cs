namespace RoadLaneLock.Core
{
 public struct ServiceFallback
 {
  public byte Failures;
  public bool Counted, Waiting, Native;
  // Pending -> Updated belongs to a single attempt. Persistent failure flags
  // cannot increment the counter again until a new attempt is observed.
  public void ObserveAttempt(bool waiting)
  {
   if(waiting&&!Waiting)Counted=false;
   Waiting=waiting;
  }
  public void Fail()
  {
   if(Native||Counted)return;
   Counted=true;if(Failures<2)Failures++;
   Native=Failures>=2;
  }
  public void Accept()
  {
   if(Native)return;
   Failures=0;Counted=false;
  }
 }
}
