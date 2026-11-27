"""Replay captured format-2 frames through format-3 delta semantics and verify lossless reconstruction.
This measures storage semantics, not game CPU time or frame rate.
"""
import json, pathlib, sys, copy
sys.path.insert(0, 'work')
import stall_analysis

def key(r):
    e=r['Entity'];return e['Index'],e['Version']

class DeltaReplay:
    def __init__(self):
        self.paths={};self.lanes={};self.decoded_paths={};self.decoded_lanes={}
        self.old_bytes=0;self.new_bytes=0;self.vehicles=0;self.lane_updates=0;self.path_updates=0
    def emit(self,r):
        self.new_bytes+=len(json.dumps(r,separators=(',',':')).encode())+1
        if r['Kind']=='path':self.decoded_paths[key(r)]=(r['Revision'],r['Elements'])
        elif r['Kind']=='lane':self.decoded_lanes[key(r)]={k:v for k,v in r.items() if k!='Sequence'}
    def frame(self,rows):
        for r in rows:
            self.old_bytes+=len(json.dumps(r,separators=(',',':')).encode())+1
            if r['Kind']=='vehicle':
                identity=key(r);elements=r['PathElements'];prior=self.paths.get(identity)
                if prior is None or prior[1]!=elements:
                    prior=(r['Sequence'],elements);self.paths[identity]=prior;self.path_updates+=1
                    self.emit(dict(Kind='path',Sequence=r['Sequence'],Entity=r['Entity'],Revision=prior[0],Elements=elements))
                v={k:x for k,x in r.items() if k!='PathElements'};v['PathRevision']=prior[0];self.emit(v)
                revision,path=self.decoded_paths[identity]
                assert revision==v['PathRevision']
                rebuilt={k:x for k,x in v.items() if k!='PathRevision'};rebuilt['PathElements']=path
                assert rebuilt==r
                self.vehicles+=1
            elif r['Kind']=='lane':
                state={k:v for k,v in r.items() if k!='Sequence'};identity=key(r)
                if self.lanes.get(identity)!=state:self.lanes[identity]=state;self.emit(r);self.lane_updates+=1
                assert self.decoded_lanes[identity]==state
            else:self.emit(r)

def synthetic():
    d=DeltaReplay();e={'Index':1,'Version':1}
    v={'Kind':'vehicle','Entity':e,'Sequence':1,'PathElements':[{'Target':e,'Flags':'0','Delta':[0,1]}]}
    lane={'Kind':'lane','Entity':e,'Sequence':1,'Owner':e,'Start':'a','Middle':'b','End':'c','Flags':'0','Curve':{'Length':1}}
    d.frame([v,lane]);d.frame([dict(v,Sequence=2),dict(lane,Sequence=2)])
    assert d.path_updates==1 and d.lane_updates==1
    changed=copy.deepcopy(v);changed['Sequence']=3;changed['PathElements'][0]['Delta']=[.25,1];d.frame([changed])
    changed['PathElements']=[];changed['Sequence']=4;d.frame([changed])
    d.frame([dict(lane,Sequence=4,Flags='Forbidden')]);d.frame([dict(lane,Sequence=5,Curve={'Length':2})])
    d.frame([dict(v,Sequence=6,Entity={'Index':1,'Version':2})])
    assert d.path_updates==4 and d.lane_updates==3

if __name__=='__main__':
    synthetic();stall_analysis.p=pathlib.Path(sys.argv[1]);d=DeltaReplay()
    sequences=list(range(1,65))+list(range(3650,3830))+list(range(10960,11024))
    for seq in sequences:d.frame(stall_analysis.snapshot(seq))
    result=dict(Status='PASS',Frames=len(sequences),VehicleSnapshots=d.vehicles,PathDefinitions=d.path_updates,LaneDefinitions=d.lane_updates,
        OriginalBytes=d.old_bytes,DeltaBytes=d.new_bytes,ReductionPercent=round(100*(1-d.new_bytes/d.old_bytes),2),
        Scope='Lossless offline format replay; excludes new signal fields, not a live runtime benchmark')
    pathlib.Path('outputs/RoadLaneLock/verification/recording-replay-result.json').write_text(json.dumps(result,indent=2))
    print(json.dumps(result))
