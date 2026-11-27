import assert from 'node:assert/strict';
let state={ToolActive:false,Valid:false,Session:'one',Index:17,Version:3},cursor=0;
let pose={};
const shelter='美式公交站顶棚（自行车停放架）';
const transit=[{Id:'10/1',Mode:'Bus',Kind:'Bus',Code:'B01',Name:'环城线',Color:'#fff',Vehicles:2,Load:112,Capacity:400,Ticket:0,Visible:true,Issues:[{Kind:'backlog',Level:2,Label:'积压',Detail:'北门 候车 80 人'}],Fleet:[{Id:'90/1',Code:'#90',State:'Moving',At:1,Boarding:false,Load:40,Capacity:110},{Id:'91/1',Code:'#91',State:'Full',At:3,Boarding:true,Load:105,Capacity:110},{Id:'92/1',Code:'#92',State:'Returning',At:-1,Boarding:false,Load:0,Capacity:110}],Stops:[{Id:'1/1',Name:'北门',Waiting:3},{Id:'2/1',Name:shelter,Waiting:0},{Id:'3/1',Name:shelter,Waiting:9},{Id:'4/1',Name:shelter,Waiting:null}]},
 {Id:'11/1',Mode:'Cargo',Kind:'CargoShip',Code:'CS',Name:'货船路线工具',Color:'#0096D6',Vehicles:1,Load:2500,Capacity:5000,Visible:true,Issues:[],Stops:[{Id:'1/1',Name:'北门'}]}];
const meta={TicketReady:true,TicketMin:0,TicketMax:100,TicketStep:1,Auto:true,Prefix:{Bus:'B',CargoShip:'CS'},Preview:[{Id:'11/1',From:'货船路线工具',To:'CS01'}]};
// Hook state is keyed per component instance (name + render occurrence), so adding a component with state elsewhere doesn't shift slots.
// The root (Panel) keeps numeric keys: values[0] is its first state.
const values={},components={},events=[];let frames=[{key:'',i:0}],occurrences={};
globalThis.window={innerWidth:1280,innerHeight:720,React:{createElement:(type,props,...children)=>{if(typeof type!=='function')return {type,props:props||{},children};const n=type.name||'anon';occurrences[n]=(occurrences[n]||0)+1;frames.push({key:n+'#'+occurrences[n],i:0});try{return type(props);}finally{frames.pop();}},
 useState:init=>{const f=frames[frames.length-1],n=f.i++;const i=f.key?f.key+':'+n:n;cursor++;if(!(i in values))values[i]=init;return [values[i],v=>values[i]=typeof v==='function'?v(values[i]):v];},
 useRef:()=>({current:null}),useEffect:()=>{},useLayoutEffect:()=>{}},
 'cs2/api':{bindValue:(_,name)=>name,useValue:name=>JSON.stringify(name==='pose'?pose:name==='transit'?transit:name==='transitMeta'?meta:name==='data'?{Recent:['#112233'],Favorites:[]}:name==='status'?'':state),trigger:(...args)=>events.push(args)},'cs2/ui':{FloatingButton:'FloatingButton',Tooltip:'Tooltip'}};
const mod=await import('../ui/RoadLaneLock.mjs');mod.default({append:(key,value)=>components[key]=value});
const render=()=>{cursor=0;frames=[{key:'',i:0}];occurrences={};return components.Game();};
const buttons=n=>!n||typeof n!=='object'?[]:[...(n.type==='button'?[n]:[]),...n.children.flatMap(buttons)];
const text=n=>n==null?'':typeof n==='object'?n.children.map(text).join(''):String(n);
const clickRow=name=>{const rows=n=>!n||typeof n!=='object'?[]:[...(n.props?.className==='rll-tr'&&n.props.onClick&&text(n).includes(name)?[n]:[]),...n.children.flatMap(rows)];const r=rows(render())[0];assert.ok(r,'row '+name);r.props.onClick();};
const click=label=>{const b=buttons(render()).find(b=>text(b)===label||b.props['aria-label']===label||text(b).replace(/[⌄⌃]$/,'')===label);assert.ok(b,label);b.props.onClick({stopPropagation(){}});};
assert.equal(render(),null);
components.GameTopLeft().children[0].props.onSelect();assert.equal(events.at(-1)[1],'showVehicles');
state.ToolActive=true;state.ViewingVehicles=true;state.VehicleId='568940/7';
// 交通信息: city traffic overview (replaces the old per-vehicle recorder).
assert.match(text(render()),/正在统计全城交通/);assert.doesNotMatch(text(render()),/录制/);
state.Traffic={Moving:3482,Parked:900,Flow:72,Bottlenecks:2,Kinds:[{Name:'私家车',Count:2000},{Name:'出租',Count:500},{Name:'货车',Count:400},{Name:'公交',Count:300},{Name:'市政/紧急',Count:200},{Name:'其他',Count:82}],
 Spots:[{Id:'5/1',Name:'溪谷街',Flow:18,Bottleneck:true,Monitored:true,Queue:11},{Id:'6/2',Name:'银座街 × 江心大道 路口',Flow:34,Bottleneck:true,Monitored:false,Queue:0},{Id:'7/1',Name:'沙洲南路',Flow:47,Bottleneck:false,Monitored:false,Queue:0}],FlowTrend:[80,76,72],BottleneckTrend:[1,2,2],Jams:3,LiveCars:100,Standing:25,FlowHourAgo:70,Managed:3,ManagedRed:1,
 History:{Points:[{H:-2,F:80,J:1},{H:-1,F:60,J:2},{H:0,F:66,J:2}],Markers:[{H:-1.5,Text:'溪谷街 · 拥堵疏导 开'}]}};
state.Traffic.Spots[0].Usage={Total:40,Kinds:[{Name:'私家车',Pct:50},{Name:'货车',Pct:30}],Known:38,Near:30,Far:70,FarTrucks:80,Suggest:'路过的货车占这条路货车的 80% · 建议：分道禁货车，让它们走干道',CanApply:true};
{const t=text(render());assert.match(t,/交通信息/);assert.match(t,/实时 · 每 5 秒/);assert.match(t,/3,482|3482/);assert.match(t,/72%/);assert.match(t,/2 处/);assert.match(t,/私家车 57%/);
 assert.match(t,/流速 18% · 排队 11 辆/);assert.match(t,/原版堵点/);assert.match(t,/监测中/);assert.match(t,/缓行/);
 assert.match(t,/行驶 75% · 停着 25%/);assert.match(t,/1 小时前 70% →/);assert.match(t,/疏导路段 3 段 · 1 段红/);assert.match(t,/谁在用这条路/);assert.doesNotMatch(t,/只是路过/);}
{const find=n=>!n||typeof n!=='object'?[]:[...(n.props?.role==='button'&&text(n)==='谁在用这条路 ›'?[n]:[]),...(n.children||[]).flatMap(find)];const b=find(render())[0];assert.ok(b,'usage toggle');b.props.onClick({stopPropagation(){}});}{const t=text(render());assert.match(t,/近 10 分钟 · 40 辆/);assert.match(t,/只是路过70%/);assert.match(t,/建议：分道禁货车/);}
{const b=buttons(render()).find(b=>b.props['aria-label']==='定位 溪谷街');b.props.onMouseEnter();assert.deepEqual(events.at(-1),['RoadLaneLock','hoverTraffic','5/1']);b.props.onMouseLeave();assert.deepEqual(events.at(-1),['RoadLaneLock','hoverTraffic','']);}
click('定位 溪谷街');assert.deepEqual(events.at(-1),['RoadLaneLock','focusTraffic','5/1']);
assert.doesNotMatch(text(render()),/车辆 ID 568940\/7/);click('车辆明细与趋势');{const t=text(render());assert.match(t,/车辆 ID 568940\/7/);assert.match(t,/停着的车900 辆/);assert.match(t,/本局拥堵疏导 3 次/);assert.match(t,/流速走势/);assert.match(t,/溪谷街 · 拥堵疏导 开/);}click('车辆明细与趋势');
state.Traffic={Moving:10,Parked:0,Flow:100,Bottlenecks:0,Kinds:[],Spots:[]};assert.match(text(render()),/全城通畅/);state.Traffic=null;
state.ViewingVehicles=false;assert.ok(!text(render()).includes('车辆 ID 568940/7'));
state.ViewingVehicles=true;values[0]=null;const anchored=render().props.style;assert.equal(anchored.left,'10px');assert.equal(anchored.right,undefined);assert.equal(anchored.position,'fixed');values[0]={x:24,y:90};
// Run layout effects on each transition to catch module-specific coordinate resets.
window.React.useLayoutEffect=effect=>effect();
for(const position of [null,{x:315,y:190}]){
 values[0]=position;
 for(const view of ['pick','road','transit','record']){ // record = 交通信息
  state.Picking=view==='pick';state.TransitOpen=view==='transit';state.ViewingVehicles=view==='record';state.SelectionRevision=(state.SelectionRevision||0)+1;
  const tree=render(),dialog=tree.props.role==='dialog'?tree:tree.children[1];
  assert.deepEqual(values[0],position);
  assert.equal(dialog.props.style.width,anchored.width);
  assert.equal(dialog.props.style.top,position?'190px':anchored.top);
  assert.equal(dialog.props.style.left,position?'315px':anchored.left);
  assert.equal(dialog.props.style.right,position?undefined:anchored.right);
 }
 state.ToolActive=false;render();state.ToolActive=true;render();assert.deepEqual(values[0],position);
}
window.React.useLayoutEffect=()=>{};
state.ToolActive=false;state.Recording=true;assert.equal(render(),null);
state.VehicleId=null;assert.equal(render(),null);
state.Recording=false;state.ToolActive=true;click('车道规则');assert.equal(events.at(-1)[1],'pick');
state.ViewingVehicles=false;
state.ToolActive=true;state.Picking=true;assert.match(text(render()),/选择路段/);
assert.match(text(render()),/还没有设置规则的路段/);
state.RuleRoads=[{Index:40,Version:2,Name:'江心大道',Mask:3,Directions:3,Detours:2,Speed:50},{Index:41,Version:1,Name:'沙洲南路',Mask:1,Directions:3,Detours:0},{Index:42,Version:1,Name:'乔木园街',Mask:0,Directions:3,Detours:0,Yellow:true,BannedLanes:2,Whole:1}];state.SavedCount=2;state.Detours=2;
{const t=text(render());assert.match(t,/已设 3 段/);assert.match(t,/禁变道 2/);assert.match(t,/限速 1 段/);assert.match(t,/绕行 2 辆/);assert.match(t,/B 禁变道/);assert.doesNotMatch(t,/可变道/);
 assert.match(t,/限速 50/);assert.match(t,/双黄线/);assert.match(t,/整段禁私家车/);assert.match(t,/选一段路，设置变道、限速、双黄线、分道/);}
click('江心大道');assert.deepEqual(JSON.parse(events.at(-1)[2]),{Session:'one',Index:40,Version:2});assert.equal(events.at(-1)[1],'focusRuleRoad');
state.RuleRoads=null;click('取消选择');assert.equal(events.at(-1)[1],'cancelPick');
state.Picking=false;state.Valid=true;state.Description={Start:1,End:2,RealLanes:6};
assert.equal(render().children[1].props.role,'dialog');assert.doesNotMatch(text(render()),/清除全部/);
state.Controls=[{Direction:1,On:true,X:.5,Y:.5,Angle:90,Visible:true},{Direction:2,On:false,X:.51,Y:.52,Angle:-90,Visible:true}];
pose={Session:'one',Index:17,Version:3,First:{X:.5,Y:.5,Angle:90},Second:{X:.51,Y:.52,Angle:-90}};
const switches=buttons(render()).filter(b=>b.props.role==='switch');
assert.equal(switches.length,4);
assert.equal(buttons(render().children[1]).filter(b=>b.props.role==="switch").length,2);
assert.notEqual(switches[0].props.style.top,switches[1].props.style.top);
pose.First.X=.65;
assert.notEqual(buttons(render())[0].props.style.left,switches[0].props.style.left);
pose.Version=2;assert.equal(buttons(render()).filter(b=>b.props.role==='switch').length,2);pose.Version=3;
click('路面方向 2');assert.deepEqual(JSON.parse(events.at(-1)[2]),{Session:'one',Index:17,Version:3,Direction:2,Enable:true});
click('切换方向 1');assert.equal(JSON.parse(events.at(-1)[2]).Enable,false);
state.RoadExtras={Length:182,Toward1:'江心桥',Toward2:null};state.Requested=true;{const t=text(render());assert.match(t,/驶向江心桥/);assert.doesNotMatch(t,/A 方向/);assert.match(t,/长 182 米/);assert.match(t,/6 车道/);assert.match(t,/规则生效/);}
click('两向都禁止');assert.equal(events.at(-1)[1],'requestBoth');assert.equal(JSON.parse(events.at(-1)[2]).Enable,true);
click('定位此路段');assert.equal(events.at(-1)[1],'focusSelectedRoad');
pose.Second=null;state.Controls=[null,{...state.Controls[1],Visible:false}];assert.equal(buttons(render()).filter(b=>b.props.role==='switch').length,1);
state.PendingCount=3;assert.ok(!buttons(render()).some(b=>/录制/.test(b.props['aria-label']||'')));
state.ViewingVehicles=false;click('图例与诊断');state.Index=18;state.Version=9;
click('记录此路段及相邻路口');assert.deepEqual(JSON.parse(events.at(-1)[2]),{Session:'one',Index:18,Version:9});
state.Enabled=true;click('清除全部规则并恢复原版导航');assert.deepEqual(events.at(-1),['RoadLaneLock','resetAll','one']);
click('选择另一条道路 ›');assert.equal(events.at(-1)[1],'deselectRoad');
click('关闭');assert.equal(events.at(-1)[1],'cancelPick');assert.notEqual(events.at(-1)[1],'recordVehicles');
state.Recording=false;state.Valid=false;state.Picking=true;state.PendingCount=2;state.ViewingVehicles=true;
assert.ok(!events.some(e=>e[1]==='recordVehicles'||e[1]==='pickRecordRoad'));
// Rail: every module is laid out top-down; the selected tile's tongue sits at 4+42*index rem.
{const rail=['traffic','pick','junction','monitor','parking','transit'];
 rail.forEach((view,i)=>{Object.assign(state,{ToolActive:true,Picking:view==='pick',TransitOpen:view==='transit',MonitorOpen:view==='monitor',ParkingOpen:view==='parking',ViewingVehicles:view==='traffic',JunctionOpen:view==='junction'});
  const find=n=>!n||typeof n!=='object'?null:n.props?.className==='rll-tongue'?n:n.children.map(find).find(Boolean);
  const tongue=find(render());assert.ok(tongue,view);
  assert.equal(tongue.props.style.top,i===0?0:(4+i*42)+'rem',view);assert.equal(tongue.props.style.bottom,undefined,view);
  const pressed=buttons(render()).filter(b=>b.props.className==='rll-module'&&b.props['aria-pressed']);assert.equal(pressed.length,1,view);});
 Object.assign(state,{MonitorOpen:false,ParkingOpen:false,TransitOpen:false,JunctionOpen:false,ViewingVehicles:true,Picking:true});}
state.ToolActive=false;assert.equal(render(),null);
assert.equal(components.GameTopLeft().children[0].props.src,'coui://roadlanelock/RoadLaneLock.svg');
assert.deepEqual(mod.clampPosition(1200,700,300,400,1280,720),{x:968,y:308});
assert.deepEqual(mod.clampPosition(-100,-20,300,400,1280,720),{x:12,y:12});
assert.deepEqual(mod.clampPosition(500,500,300,400,320,240),{x:12,y:12});
console.log('PASS: segment popup, all commands, entity version, effective state, recording target, close, screen bounds');

// Exercise the panel's own mouseup, which stops bubbling to the game.
state.ToolActive=true;state.Picking=false;state.Valid=true;state.ViewingVehicles=false;
const listeners=new Map();
window.addEventListener=(name,fn)=>listeners.set(name,fn);
window.removeEventListener=(name)=>listeners.delete(name);
let cleanup;
window.React.useEffect=fn=>{const result=fn();if(result)cleanup=result;};
for(const picking of [true,false]){
state.Picking=picking;values[0]={x:24,y:90};
const popup=picking?render():render().children[1],header=popup.children[0];
assert.equal(popup.props.style.fontFamily,'var(--fontFamily)');
const down={button:0,clientX:40,clientY:100,preventDefault(){},stopPropagation(){}};
const move=(clientX,buttons)=>listeners.get('mousemove')({clientX,clientY:140,buttons});
for(const release of [()=>popup.props.onMouseUp(down),()=>listeners.get('mouseup')(down),()=>listeners.get('blur')()]){
 header.props.onMouseDown(down);move(90,0);
 assert.equal(values[0].x,74);
 release();move(240,0);assert.equal(values[0].x,74);
 move(250,1);assert.equal(values[0].x,74);
}
cleanup();assert.equal(listeners.size,0);
}
console.log('PASS: both panels move with zero mouse button mask; release inside/outside or blur stops drag');

state.TransitOpen=true;
click('公交');assert.deepEqual(events.at(-1),['RoadLaneLock','highlightLine','']);
assert.match(text(render()),/112 人/);assert.match(text(render()),/B01/);assert.match(text(render()),/积压/);
click('总览');assert.match(text(render()),/2.5 吨/);click('公交');
clickRow('环城线');{const t=text(render());assert.match(t,/候车合计 12 人/);assert.match(t,/最多 · 第 3 站 9 人/);assert.match(t,/北门 候车 80 人/);assert.match(t,/末站后返回第 1 站/);assert.match(t,/第 2 站/);assert.match(t,/金边为候车最多/);assert.match(t,/公共交通信息/);}
{const find=n=>!n||typeof n!=='object'?[]:[...(n.props?.className==='rll-loop-crowd'?[n]:[]),...n.children.flatMap(find)];assert.equal(find(render()).length,1,'one crowded stop gets the amber outer ring');}
{const m=mod.loopLayout(11,460);assert.equal(m.points.length,11);assert.equal(m.points[0].y,m.points[5].y);assert.ok(m.points[6].y>m.points[0].y);assert.ok(m.points[6].x>m.points[10].x,'bottom row runs right to left');assert.ok(m.at(10.5).x<m.cx0,'last stop bends back to the first');}
click('北门');assert.deepEqual(events.at(-1),['RoadLaneLock','focusEntity','1/1']);
{const marks=buttons(render()).filter(b=>b.props.className==='rll-veh-mark');assert.equal(marks.length,2);marks[0].props.onClick();assert.equal(events.at(-1)[1],'focusEntity');}
{const nodes=n=>!n||typeof n!=='object'?[]:[n,...n.children.flatMap(nodes)];assert.equal(nodes(render()).filter(n=>n.type==='path'&&n.props.strokeLinecap==='butt').length,2,'shared stop ring has two segments');}
// The test harness shares hook slots by call order, so open the fold only if it is closed.
if(!buttons(render()).find(b=>b.props['aria-label']==='运行车辆').props['aria-expanded'])click('运行车辆');{const t=text(render());assert.match(t,/运行车辆 · 3 辆/);assert.match(t,/满载/);assert.match(t,/回厂/);assert.match(t,/停靠 /);assert.match(t,/隔 2.5 站|隔 1.5 站/);}
click('聚焦车辆 #92');assert.deepEqual(events.at(-1),['RoadLaneLock','focusEntity','92/1']);
clickRow('环城线');
click('在车排序');assert.equal(mod.sortTransitRows(transit,'onboard',false)[0].Id,'11/1');
click('环城线的线路设置');{const t=text(render());assert.match(t,/票价免费/);assert.match(t,/参考色/);assert.match(t,/韩国/);assert.doesNotMatch(t,/银座线/);assert.match(t,/推荐/);}
click('提高票价');assert.deepEqual(events.at(-1),['RoadLaneLock','setTicketPrice','10/1',1]);
assert.match(text(render()),/运营时段/);click('仅夜间');assert.deepEqual(events.at(-1),['RoadLaneLock','setLineSchedule','10/1',1]);
click('在原版面板中打开 ›');assert.deepEqual(events.at(-1),['RoadLaneLock','openTransitLine','10/1']);
click('删除线路');assert.notEqual(events.at(-1)[1],'deleteLine');assert.match(text(render()),/确认删除 · 不可撤销/);
click('日本');assert.match(text(render()),/大阪 Metro/);{const b=buttons(render()).find(b=>b.props.title==='东京 Metro 银座线 #FF9500');b.props.onClick();assert.deepEqual(events.at(-1),['RoadLaneLock','applyLineColor','10/1','#FF9500']);}
click('环城线的线路设置');
click('线路命名与编号');assert.match(text(render()),/将为 1 条默认名线路命名/);click('批量命名');assert.deepEqual(events.at(-1),['RoadLaneLock','applyNaming']);
click('新建线路自动命名');assert.equal(JSON.parse(events.at(-1)[2]).Auto,false);click('线路命名与编号');
{const rail=buttons(render()).filter(b=>b.props.className==='rll-module').map(b=>b.props['aria-label']);assert.deepEqual(rail,['交通信息','车道规则','路口规则','拥堵疏导','路边停车','公共交通信息']);}
assert.match(text(render()),/2 条 · 3 辆/);
click('隐藏此类线路');assert.deepEqual(events.at(-1),['RoadLaneLock','visibility','Bus']);
click('隐藏环城线');assert.equal(events.at(-1)[2],'10/1');
click('车道规则');assert.equal(events.at(-1)[1],'pick');
console.log('PASS: transit module, station list and independent visibility');
click('拥堵疏导');assert.equal(events.at(-1)[1],'showMonitor');
Object.assign(state,{TransitOpen:false,MonitorOpen:true,Picking:false,Valid:true,RoadName:'北门街',Description:{Start:42,End:43},Controls:[{Direction:1,On:true},{Direction:2,On:false}],MonitorMask:2,Heading1:'西行',Heading2:'东行',MonitorCount:1,JamReady:true,
 MonitorRoads:[{Index:state.Index,Version:state.Version,Name:'北门街',Mask:2,Directions:3,Heading1:'西行',Heading2:'东行'}],
 JamEvents:[{Id:'77/1',Road:'北门街',Dir:'东行',Reason:'等掉头',Trigger:'倒灌到路口',Followers:3,Live:true,Ago:'5 秒前'}]});
assert.match(text(render()),/已监测 1 段/);assert.doesNotMatch(text(render()),/需开启新执行器/);
for(const [dir,label,enable] of [[1,'驶向节点 #42',true],[2,'驶向节点 #43',false]]){
 const control=buttons(render()).find(b=>b.props['aria-label']===label);
 assert.equal(control.props.role,'switch');assert.equal(control.props['aria-checked'],!enable);assert.match(text(control),dir===1?/西行/:/东行/);assert.doesNotMatch(text(control),/头车可改直行/);assert.match(control.props.title,dir===1?/未监测/:/监测中/);
 click(label);assert.equal(events.at(-1)[1],'monitorRule');
 assert.deepEqual(JSON.parse(events.at(-1)[2]),{Session:state.Session,Index:state.Index,Version:state.Version,Direction:dir,Enable:enable});
}
click('北门街 东行 监测中');assert.equal(events.at(-1)[1],'monitorRoadDirection');
assert.deepEqual(JSON.parse(events.at(-1)[2]),{Session:state.Session,Index:state.Index,Version:state.Version,Direction:2,Enable:false});
click('北门街 西行 未监测');assert.equal(JSON.parse(events.at(-1)[2]).Enable,true);
click('移除 北门街');assert.equal(events.at(-1)[1],'removeMonitorRoad');
click('北门街');assert.equal(events.at(-1)[1],'focusMonitorRoad');
{const t=text(render());assert.match(t,/北门街 · 东行/);assert.match(t,/等掉头 · 倒灌到路口/);assert.match(t,/后车解锁 3/);assert.match(t,/直行驶出/);assert.match(t,/车辆 77\/1/);}
click('查看车辆 77/1');assert.deepEqual(events.at(-1),['RoadLaneLock','focusJamVehicle','77/1']);
click('选择另一条道路 ›');assert.equal(events.at(-1)[1],'deselectRoad');
state.JamReady=false;assert.match(text(render()),/需开启新执行器/);state.JamReady=true;
state.JamEvents=[];state.MonitorRoads=[];assert.match(text(render()),/还没有监测路段/);assert.match(text(render()),/监测路段暂无疏导记录/);
console.log('PASS: jam relief page (monitored roads, direction switches, recent releases)');
// Parking module regression: state, independent command, stale map anchors, accessibility.
state={ToolActive:true,ParkingOpen:true,ParkingReady:true,Picking:true,Valid:false,Session:'parking',Index:51,Version:7,ParkingCount:0};
pose={};
assert.match(text(render()),/选择路段/);
assert.equal(buttons(render()).filter(b=>b.props.role==='switch').length,0);
state.Picking=false;state.Valid=true;state.RoadName='银杏街';
pose={Session:'parking',Index:51,Version:7,Parking:{X:.4,Y:.6}};
let parkingSwitches=buttons(render()).filter(b=>b.props.role==='switch');
assert.equal(parkingSwitches.length,2);
assert.equal(parkingSwitches[0].props['aria-checked'],false);
click('禁止路边停车');assert.deepEqual(events.at(-1),['RoadLaneLock','parkingRule',JSON.stringify({Session:'parking',Index:51,Version:7,Enable:true})]);
state.ParkingOn=true;state.ParkingCount=1;
click('路面停车开关');assert.equal(JSON.parse(events.at(-1)[2]).Enable,false);
const nodes=n=>!n||typeof n!=='object'?[]:[n,...n.children.flatMap(nodes)];
assert.equal(nodes(render()).filter(n=>n.type==='path'&&n.props.d==='M12 12L52 52').length,2);
pose.Version=6;assert.equal(buttons(render()).filter(b=>b.props.role==='switch').length,1);
pose.Version=7;state.ParkingOn=false;
assert.equal(nodes(render()).filter(n=>n.type==='path'&&n.props.d==='M12 12L52 52').length,0);
state.ParkingReady=false;assert.ok(buttons(render()).filter(b=>b.props.role==='switch').every(b=>b.props.disabled));
click('选择另一条道路 ›');assert.equal(events.at(-1)[1],'deselectRoad');if(!buttons(render()).find(b=>text(b).startsWith('图例与说明')).props['aria-expanded'])click('图例与说明');assert.match(text(render()),/随城市存档保存/);
click('恢复全部本次停车规则');assert.deepEqual(events.at(-1),['RoadLaneLock','clearParking','parking']);
click('车道规则');assert.equal(events.at(-1)[1],'pick');
click('公共交通信息');assert.equal(events.at(-1)[1],'showTransit');
click('关闭');assert.equal(events.at(-1)[1],'cancelPick');
console.log('PASS: parking state, P/slash icons, independent commands, stale pose rejection, disabled state and session disclosure');



// 车道规则: switcher, one rule at a time, badges for other active rules.
state={ToolActive:true,Picking:false,Valid:true,Session:'rules',Index:61,Version:2,RoadName:'沙洲大道',Controls:[{Direction:1,On:true},{Direction:2,On:false}],SavedCount:1,Description:{RealLanes:5,Start:1,End:2},
 LaneTab:'lane',LaneRules:{Speed:40,DefaultSpeed:60,DoubleYellow:false,TwoWay:true,BannedLanes:1,Bans:[{Direction:1,Toward:'越海路',Lanes:[0,8,0]},{Direction:2,Toward:'溪谷街',Lanes:[0,0]}],Active:{Lane:true,Speed:true,Yellow:false,Truck:true}}};
pose={Session:'rules',Index:61,Version:2,First:{X:.4,Y:.5,Angle:90},Second:{X:.6,Y:.5,Angle:-90},Mid:{X:.5,Y:.5}};
{const tabs=buttons(render()).filter(b=>b.props.role==='tab'&&['变道','限速','车种','双黄线','分道'].includes(text(b)));assert.equal(tabs.length,4);
 assert.deepEqual(tabs.map(b=>b.props['data-active']),['1','1','0','1']);}
assert.match(text(render()),/5 车道/);
{const badges=buttons(render()).filter(b=>b.props.className==='rll-badge2').map(b=>b.props['data-kind']);assert.deepEqual(badges,['speed','truck']);}
click('限速');assert.deepEqual(events.at(-1),['RoadLaneLock','laneTab','speed']);
state.LaneTab='speed';{const t=text(render());assert.match(t,/原版 60/);assert.match(t,/限速 40/);assert.match(t,/40 km\/h/);assert.match(t,/车辆在这段路按设定速度行驶/);}
click('提高');assert.equal(events.at(-1)[1],'setSpeed');assert.equal(JSON.parse(events.at(-1)[2]).Value,50);
click('降低');assert.equal(JSON.parse(events.at(-1)[2]).Value,30);
{const badges=buttons(render()).filter(b=>b.props.className==='rll-badge2').map(b=>b.props['data-kind']);assert.deepEqual(badges,['lane','truck']);}
state.LaneTab='yellow';assert.match(text(render()),/未设双黄线/);buttons(render()).find(b=>b.props.role==='switch'&&b.props['aria-label']==='双黄线'&&b.props.className==='rll-dircap').props.onClick({stopPropagation(){}});assert.equal(events.at(-1)[1],'setDoubleYellow');
state.LaneRules.TwoWay=false;assert.match(text(render()),/单行道没有对向车道/);state.LaneRules.TwoWay=true;
// 分道: a row of vehicle buttons on each lane of the map; a click bans or allows that kind on that lane.
// Each kind has its own spot on the lane (K), projected separately.
const spotsAt=(x,y)=>Array.from({length:7},(_,k)=>({X:x+k*.03,Y:y}));
state.LaneTab='truck';pose.Lanes=[{D:1,I:0,P:{X:.45,Y:.40,Angle:0},K:spotsAt(.3,.40)},{D:1,I:1,P:{X:.45,Y:.52,Angle:0},K:spotsAt(.3,.52)},{D:2,I:0,P:{X:.6,Y:.5,Angle:90},K:spotsAt(.3,.64)}];
{const t=text(render());assert.match(t,/1 条车道有限制/);assert.match(t,/自行车/);
 const kinds=buttons(render()).filter(b=>b.props.className==='rll-kindbtn rll-kindspot');assert.equal(kinds.length,21);assert.equal(kinds.filter(b=>b.props['data-banned']==='1').length,1);}
click('货车 · 已禁止');{const e=JSON.parse(events.at(-1)[2]);assert.equal(events.at(-1)[1],'setLaneBan');assert.equal(e.Direction,1);assert.equal(e.Lane,1);assert.equal(e.Value,8);assert.equal(e.Enable,false);}
click('自行车 · 可通行');{const e=JSON.parse(events.at(-1)[2]);assert.equal(e.Value,32);assert.equal(e.Enable,true);}
{const saved=pose.Lanes;pose.Lanes=[{D:1,I:0,P:{X:.5,Y:.5},K:spotsAt(.5,.5).map((q,k)=>({X:.5+k*.002,Y:.5}))},{D:1,I:1,P:{X:.5,Y:.51},K:spotsAt(.5,.51)}];click('拉近设置分道 ›');assert.equal(events.at(-1)[1],'zoomSelectedRoad');pose.Lanes=saved;}
state.LaneRules.Notice='每个方向至少留一条车道给货车';assert.match(text(render()),/每个方向至少留一条车道给货车/);delete state.LaneRules.Notice;delete pose.Lanes;
console.log('PASS: lane rules switcher, per-rule cards, speed slider, double yellow, lane bans per vehicle kind and badges');// 路口规则.
state={ToolActive:true,JunctionOpen:true,Picking:true,Valid:false,Session:'jn',Junction:{Selected:false,HoverName:'沙洲大道与越海路路口',List:[]}};
assert.match(text(render()),/沙洲大道与越海路路口/);assert.match(text(render()),/还没有禁止过街的路口/);
state.Picking=false;state.Junction={Selected:true,Index:9,Version:1,Name:'沙洲大道与越海路路口',Roads:4,Crossings:4,Closed:false,Auto:false,Warn:false,List:[{Index:9,Version:1,Name:'沙洲大道与越海路路口'}]};
click('禁止行人过街');assert.deepEqual(JSON.parse(events.at(-1)[2]),{Session:'jn',Index:9,Version:1,Enable:true});assert.equal(events.at(-1)[1],'junctionRule');
state.Junction.Closed=true;state.Junction.Warn=true;assert.match(text(render()),/相邻路口也没有可走的过街点/);
click('移除 沙洲大道与越海路路口');assert.equal(events.at(-1)[1],'removeJunction');
click('选择另一个路口 ›');assert.equal(events.at(-1)[1],'deselectRoad');
click('路口规则');click('车道规则');assert.equal(events.at(-1)[1],'pick');
console.log('PASS: junction rules page');
// Transit row: route diagram and settings popup are mutually exclusive.
state={ToolActive:true,TransitOpen:true,Session:'tr',Index:1,Version:1};
{const open=()=>text(render());
 if(/运营时段/.test(open()))click('环城线的线路设置');
 click('环城线的线路设置');assert.match(open(),/运营时段/);
 clickRow('环城线');assert.doesNotMatch(open(),/运营时段/);if(!/候车合计/.test(open()))clickRow('环城线');assert.match(open(),/候车合计/);
 click('环城线的线路设置');assert.match(open(),/运营时段/);assert.doesNotMatch(open(),/候车合计/);}
// Queue breakdown is shown for a single approach too.
state={ToolActive:true,ViewingVehicles:true,Traffic:{Moving:10,Parked:0,Flow:60,Bottlenecks:1,Kinds:[],Spots:[{Id:'8/1',Name:'江心大道与熙和路路口',Flow:20,Bottleneck:true,Queue:37,QueueParts:'熙和路 35 / 路口内 2',Junction:true}]}};
assert.match(text(render()),/排队 37 辆（熙和路 35 \/ 路口内 2）/);
console.log('PASS: transit row exclusivity and queue breakdown');
// 交通信息: jam cause with a shortcut, rule effects.
state={ToolActive:true,ViewingVehicles:true,Traffic:{Moving:10,Parked:0,Flow:60,Bottlenecks:1,Kinds:[],Spots:[{Id:'8/1',Name:'溪谷街',Flow:20,Bottleneck:true,Queue:6,Cause:'路边停车进出挡道',Suggest:'这段路禁止路边停车'}],
 Effects:[{Name:'沙洲大道',Rule:'禁止变道',On:true,Ready:true,FlowBefore:23,FlowAfter:31,QueueBefore:14,QueueAfter:6},{Name:'越海路',Rule:'限速',On:true,Ready:false,FlowBefore:50,QueueBefore:3}]}};
{const tx=text(render());assert.match(tx,/原因：路边停车进出挡道 · 建议：这段路禁止路边停车/);assert.match(tx,/沙洲大道 · 禁止变道 开启/);assert.match(tx,/之后 流速 23%→31%，排队 14→6 辆/);assert.match(tx,/观察中/);}
{const n=x=>!x||typeof x!=='object'?[]:[x,...x.children.flatMap(n)];const link=n(render()).find(x=>x.props?.['aria-label']==='去设置 溪谷街');link.props.onClick({stopPropagation(){}});assert.deepEqual(events.at(-1),['RoadLaneLock','applyCause','8/1']);}
console.log('PASS: traffic causes and rule effects');
// Game language: the English table translates static text, composed text and " · " parts; names stay.
{const {readFileSync}=await import('node:fs');const en=JSON.parse(readFileSync(new URL('../lang/en-US.json',import.meta.url),'utf8'));
 state={ToolActive:true,ViewingVehicles:true,Table:en,Traffic:{Moving:10,Parked:0,Flow:60,Bottlenecks:1,Kinds:[{Name:'私家车',Count:10}],Spots:[{Id:'8/1',Name:'江心大道与熙和路路口',Flow:18,Bottleneck:true,Queue:11,QueueParts:'熙和路 9 / inside 2'}]}};
 const tx=text(render());
 assert.match(tx,/Traffic info/);assert.match(tx,/Flow 18% · queue 11 \(熙和路 9 \/ inside 2\)/);assert.match(tx,/江心大道 & 熙和路/);assert.match(tx,/Cars 100%/);assert.doesNotMatch(tx,/最堵的地方/);
 const rail=buttons(render()).filter(b=>b.props.className==='rll-module').map(b=>b.props['aria-label']);assert.deepEqual(rail,['Traffic info','Lane rules','Junction rules','Jam relief','Kerb parking','Public transport']);
 const zh=JSON.parse(readFileSync(new URL('../lang/zh-HANT.json',import.meta.url),'utf8'));state.Table=zh;assert.match(text(render()),/交通信息/);assert.match(text(render()),/最堵的地方/);assert.match(text(render()),/江心大道與熙和路路口/);
 state.Table=undefined;}
console.log('PASS: interface follows the game language (English, Traditional Chinese)');
