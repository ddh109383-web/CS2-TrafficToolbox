using System;

namespace RoadLaneLock.Core
{
 public enum HandoffAction { FollowVerifiedExit, WaitForWholeVehicle, RequestOriginalDestinationPath, WaitForPath, Complete, Invalidated }
 // Tracks the handoff, not vehicle movement. The runtime must supply a verified
 // exit route and observe all articulated parts before requesting a new path.
 public sealed class ExitHandoff
 {
  readonly long roadRevision;
  readonly int[] route;
  int progress;
  bool requested,invalidated;
  public ExitHandoff(long revision,int[] verifiedRoute)
  {
   if(verifiedRoute==null||verifiedRoute.Length<2)throw new ArgumentException("A verified exit route is required");
   roadRevision=revision;route=(int[])verifiedRoute.Clone();
  }
  public HandoffAction Observe(long revision,int currentLane,bool wholeVehicleOutside,bool freshPathAccepted)
  {
   if(invalidated||revision!=roadRevision){invalidated=true;return HandoffAction.Invalidated;}
   if(requested)return freshPathAccepted?HandoffAction.Complete:HandoffAction.WaitForPath;
   int found=Array.IndexOf(route,currentLane,progress);
   if(found<0){invalidated=true;return HandoffAction.Invalidated;}
   progress=found;
   if(progress<route.Length-1)return HandoffAction.FollowVerifiedExit;
   if(!wholeVehicleOutside)return HandoffAction.WaitForWholeVehicle;
   requested=true;return HandoffAction.RequestOriginalDestinationPath;
  }
 }
}
