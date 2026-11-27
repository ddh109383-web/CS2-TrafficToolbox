import assert from 'node:assert/strict';
const events=[];
const R={Fragment:'Fragment',createElement:(type,props,...children)=>({type,props:props||{},children}),useEffect:()=>{},useState:initial=>[initial,()=>{}],useRef:()=>({current:null})};
globalThis.window={React:R,'cs2/ui':{},'cs2/api':{bindValue:(g,k)=>k,useValue:()=>'{}',trigger:(...x)=>events.push(x)}};
const mod=await import('../ui/RoadLaneLock.mjs');
for(const bad of ['', '#fff','12345G','NaN','1234567'])assert.equal(mod.normalize(bad),null);
for(const [,hex] of mod.presets)assert.equal(mod.toHex(mod.toColor(hex)),hex);
assert.equal(mod.toHex({r:NaN,g:0,b:0}),null);
// The palette now lives in the transit panel only; the vanilla colour section is left untouched.
const touched=[];
mod.default({append:(k)=>touched.push('append:'+k),get:()=>{throw new Error('must not read native modules');},override:()=>{throw new Error('must not override native modules');}});
assert.deepEqual(touched,['append:GameTopLeft','append:Game']);
assert.equal(mod.badgeInk('#FFFFFF'),'#17201f');assert.equal(mod.badgeInk('#1D50A2'),'#ffffff');
assert.ok(mod.colorDistance('#FF0000','#FF0000')===0&&mod.colorDistance('#FF0000','#00FF00')>300);
const lines=[{Id:'1',Mode:'Bus',Color:'#2BB673',Name:'甲'},{Id:'2',Mode:'Bus',Color:'#2CB774',Name:'乙'},{Id:'3',Mode:'Bus',Color:'#000000',Name:'丙'}];
const picks=mod.recommendColors(lines[2],lines);
assert.equal(picks.length,5);assert.ok(!picks.includes('#2BB673'),'skips colours already used nearby');
assert.deepEqual(mod.similarLines(lines[0],lines),['乙']);
assert.equal(mod.formatOnboard({Mode:'Bus',Load:12}),'12 人');assert.equal(mod.formatOnboard({Mode:'Cargo',Load:800}),'800 千克');assert.equal(mod.formatOnboard({Mode:'Cargo',Load:12345}),'12.3 吨');
const generic=mod.genericStopNames([{Stops:[{Id:'a',Name:'顶棚'},{Id:'b',Name:'顶棚'},{Id:'c',Name:'顶棚'},{Id:'d',Name:'北门'},{Id:'e',Name:'北门'}]}]);
assert.equal(generic('顶棚'),true);assert.equal(generic('北门'),false);assert.equal(generic(''),true);
assert.equal(mod.ringArcs(1).length,0);assert.equal(mod.ringArcs(5).length,5);assert.ok(mod.ringArcs(2)[0].startsWith('M'));
const gaps=mod.fleetGaps([{Id:'a',At:0,Boarding:true},{Id:'b',At:3,Boarding:false},{Id:'c',At:-1}],6);assert.equal(gaps.get('a'),2.5);assert.equal(gaps.get('b'),3.5);assert.equal(gaps.has('c'),false);
assert.equal(mod.ringPath(0),null);assert.ok(mod.ringPath(50).includes(' 0 0 1 '));assert.ok(mod.ringPath(75).includes(' 0 1 1 '));assert.ok(mod.ringPath(100).startsWith('M'));
for(const [,cities] of mod.presetGroups)for(const [,colors] of cities)for(const [label,hex] of colors){assert.ok(label,'label');assert.equal(mod.normalize(hex),hex,hex);}
assert.deepEqual(mod.presetGroups.map(g=>g[0]),['日本','美国','英国','法国','德国','中国','韩国']);
console.log('PASS: HEX roundtrips, no native injection, badge ink, colour recommendation, onboard format, generic stop names');
