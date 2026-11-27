namespace RoadLaneLock.Core
{
 public struct CompletedExits<T> where T:unmanaged,System.IEquatable<T>
 {
  private T from1,to1,from2,to2,from3,to3;
  public int Count {get;private set;}
  public bool Contains(T from,T to)=>
   (Count>=1&&from.Equals(from1)&&to.Equals(to1))||
   (Count>=2&&from.Equals(from2)&&to.Equals(to2))||
   (Count>=3&&from.Equals(from3)&&to.Equals(to3));
  public bool Remember(T from,T to)
  {
   if(Count>=3||Contains(from,to))return false;
   switch(Count){
    case 0:from1=from;to1=to;break;
    case 1:from2=from;to2=to;break;
    default:from3=from;to3=to;break;
   }
   Count++;return true;
  }
 }
 public static class NavigationSafety
 {
  public static bool ExtendsQueue(float tail,float low,float high)=>low<tail&&high>=tail-7f;
  public static bool QueueAtEntrance(bool hasFollower,float distance)=>hasFollower&&distance<=8f;
  public static bool WaitedAtTurn(bool stoppedBehindVehicle,double activeSeconds)=>stoppedBehindVehicle&&activeSeconds>=2.5;
  public static byte Direction(float start,float end)=>(byte)(end>start?2:end<start?1:0);
  public static byte RestoreMask(int stored)=>(byte)(stored==0?3:stored);
  public static bool ReleaseStaleAccessDetour(bool sideConnection,bool onRoute,bool changing)
   =>sideConnection&&!onRoute&&!changing;
  // Keep an existing verified route through the local reversal guard's area.
  // A short route must still release at its end instead of holding forever.
  public static bool CanHandoff(bool wholeVehicleCleared,bool nearProtectedJunction,bool hasContinuation)
   =>wholeVehicleCleared&&(!nearProtectedJunction||!hasContinuation);
  public static uint RecoveryForbiddenMask(uint forbidden,uint unsafeFlag,bool occupied,bool explicitUTurn)
   =>occupied&&explicitUTurn?forbidden&~unsafeFlag:forbidden;
  // A direct jump between opposite carriageways is not a junction turn.
  public static bool RejectReversal(bool nearby,bool sameRoad,bool connected,bool changing,float directionDot)
   =>nearby&&sameRoad&&!connected&&!changing&&directionDot < -0.5f;
 }
}
