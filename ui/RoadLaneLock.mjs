const React=window.React;
const {bindValue,useValue,trigger}=window['cs2/api'];
const {FloatingButton,Tooltip}=window['cs2/ui'];
const state$=bindValue('RoadLaneLock','state','{}');
// Interface text follows the game language. Source strings are the Simplified Chinese written here (and
// sent by the game side); the game pushes a table for the active language (built-in en / zh-Hant, or a
// player's lang/<locale>.json). Every text child and title/aria-label/placeholder/tooltip passes through
// tx() when an element is created: an exact entry wins, then entries with {0} placeholders (composed text),
// then " · " separated parts one by one. Anything not in the table stays as it is (road and line names).
const strings$=bindValue('RoadLaneLock','strings','{}');
let dict={},dictJson='',templates=[],translated=new Map(),hasTable=false;
const cjk=/[㐀-鿿　-〿＀-￯]/;
function compileTemplate(k){
 const order=[];
 const src='^'+k.replace(/\{(\d+)\}|([.*+?^${}()|[\]\\])/g,(m,n,ch)=>{if(n!==undefined){order.push(+n);return '([\\s\\S]+?)';}return '\\'+ch;})+'$';
 return {k,re:new RegExp(src),order,lit:k.replace(/\{\d+\}/g,'').length,dot:k.includes(' · ')};
}
function setTable(json){
 if(json===dictJson)return;
 dictJson=json;translated=new Map();
 try{dict=JSON.parse(json)?.Table||{};}catch{dict={};}
 templates=Object.keys(dict).filter(k=>/\{\d+\}/.test(k)&&dict[k]).map(compileTemplate).sort((a,b)=>b.lit-a.lit);
 hasTable=Object.keys(dict).length>0;
}
function readStrings(){setTable(useValue(strings$)||'{}');}
function fill(text,args){return text.replace(/\{(\d+)\}/g,(m,i)=>args[+i]??'');}
export function tx(s){
 if(typeof s!=='string'||!s||!hasTable||!cjk.test(s))return s;
 const hit=translated.get(s);if(hit!==undefined)return hit;
 let r=dict[s]||undefined;
 const tryTemplates=dotOnly=>{for(const tp of templates){if(dotOnly&&!tp.dot)continue;const m=tp.re.exec(s);if(m){const args=[];tp.order.forEach((n,i)=>args[n]=tx(m[i+1]));return fill(dict[tp.k],args);}}return undefined;};
 if(r===undefined&&s.includes(' · ')){r=tryTemplates(true);if(r===undefined)r=s.split(' · ').map(tx).join(' · ');}
 if(r===undefined)r=tryTemplates(false);
 if(r===undefined)r=s;
 if(translated.size>4000)translated=new Map();
 translated.set(s,r);return r;
}
export function t(s,...a){const r=(dict&&dict[s])||s;return a.length?fill(r,a):r;}
const createElement=React.createElement;
const textProps=['title','aria-label','placeholder','tooltip','alt'];
function h(type,props,...children){
 if(hasTable){
  if(props)for(const k of textProps)if(typeof props[k]==='string'&&cjk.test(props[k])){props={...props,[k]:tx(props[k])};}
  for(let i=0;i<children.length;i++)if(typeof children[i]==='string')children[i]=tx(children[i]);
 }
 return createElement(type,props,...children);
}
// Same as t, for scopes where a local variable is called t.
const tr=(s,...a)=>t(s,...a);
function readState(){readStrings();try{return JSON.parse(useValue(state$))||{};}catch{return {};}}
const pose$=bindValue('RoadLaneLock','pose','{}');
const palette={panel:'rgba(30,33,37,0.96)',ink:'#F1F3EF',secondary:'#ADBBC1',selected:'#70D4C4',warning:'#F1BE72'};
const railStyles=`
.rll-rail{position:relative;display:flex;flex-direction:column;width:46rem;flex-shrink:0}
.rll-rail-gap{flex:1;min-height:16rem}
.rll-module{position:relative;z-index:1;display:flex;align-items:center;justify-content:center;width:38rem;height:38rem;margin-top:4rem;padding:0;border:0;border-radius:10rem;background:transparent;cursor:pointer;}
.rll-tongue{position:absolute;left:0;right:0;height:38rem;background:#262b31;border-radius:10rem 0 0 10rem;pointer-events:none;transition:top 160ms ease}
.rll-tongue>i{position:absolute;right:0;width:12rem;height:12rem;background:#262b31}.rll-tongue>i:first-child{top:-12rem}.rll-tongue>i:last-child{bottom:-12rem}
.rll-tongue>i>b{display:block;width:100%;height:100%;background:#161a1e}.rll-tongue>i:first-child>b{border-bottom-right-radius:12rem}.rll-tongue>i:last-child>b{border-top-right-radius:12rem}
.rll-tongue[data-edge="top"]>i:first-child,.rll-tongue[data-edge="bottom"]>i:last-child{display:none}
.rll-main{flex:1;min-width:0;overflow-y:auto;padding:12rem 14rem;background:#262b31;border-radius:12rem}
.rll-module img{opacity:.75;transition:transform 110ms ease-out,opacity 110ms ease-out;}
.rll-module[aria-pressed="true"]{background:transparent;}
.rll-module[aria-pressed="true"] img{opacity:1;}
.rll-module[aria-pressed="false"]:hover,.rll-module[aria-pressed="false"]:focus-visible{background:#262c33;}
.rll-module:hover img,.rll-module:focus-visible img{transform:translateY(-2rem);opacity:1;}
.rll-module:active img{transform:translateY(0);}
.rll-record-light{position:absolute;top:12rem;right:9rem;width:5rem;height:5rem;background:#ff665e;border-radius:50%;pointer-events:none;}
.rll-module:hover .rll-record-light{transform:translateY(-2rem);}
.rll-module:active .rll-record-light{transform:translateY(0);}
@media(prefers-reduced-motion:reduce){.rll-tongue{transition:none}.rll-module img,.rll-module:hover img,.rll-module:focus-visible img,.rll-module:hover .rll-record-light{transition:none;transform:none;}}
`;
const panelStyles=`
.rll-shell h2{font-size:23rem;line-height:1.15;margin:2rem 0 12rem;font-weight:700}
.rll-shell small,.rll-legend{font-size:12rem;color:#a9b0b9}
.rll-shell button{font-family:var(--fontFamily);color:#ecf3f1;cursor:pointer}
.rll-heading,.rll-bar,.rll-tabs,.rll-actions,.rll-tr,.rll-name,.rll-rate,.rll-hero,.rll-metrics,.rll-legend,.rll-fold{display:flex;align-items:center}
.rll-heading,.rll-fold{justify-content:space-between}
.rll-heading{margin:0 0 5rem}.rll-heading h2{margin-bottom:4rem}
.rll-hero,.rll-metrics{overflow:hidden;margin:9rem 0 5rem;border:1rem solid #3a434d;border-top-color:#5d6a78;border-radius:12rem;background:#2e343b}
.rll-count{padding:1rem 9rem;border:1rem solid #3d4f66;border-radius:999rem;background:#24303d;font-size:12rem;color:#5393db!important}
.rll-artbox{position:relative;width:51%;height:104rem;flex-shrink:0}
.rll-art,.rll-shape,.rll-trace{width:100%;height:100%;object-fit:cover}
.rll-trace{position:absolute;left:0;top:0;pointer-events:none;fill:none;stroke:#ffcf83;stroke-width:1.2;stroke-dasharray:1;animation:rll-trace 950ms ease-out both}
.rll-hero-text{padding:10rem 14rem;display:flex;flex-direction:column}.rll-hero-text strong{font-size:19rem}.rll-hero small{margin-top:5rem}
.rll-metrics>div{width:30%;padding:12rem}.rll-metrics strong{display:block;font-size:27rem;animation:rll-arrive 180ms ease-out}
.rll-metrics>.rll-art,.rll-metrics>.rll-shape{width:40%;height:80rem}
.rll-actions{margin:8rem 0}.rll-actions button{width:auto!important;margin-right:7rem;padding:8rem 16rem!important;border-radius:999rem!important}.rll-actions button:first-child{background:#79deb7!important;color:#112338!important}
.rll-live{font-size:12rem}.rll-live:before{content:'●';color:#fa9e90;margin-right:5rem;animation:rll-breathe 2s ease-in-out infinite}
.rll-fold{margin-top:11rem;border:0;border-top:1rem solid #545b64;background:transparent;padding:10rem 2rem 3rem;width:100%;font-size:13rem;text-align:left}
.rll-reveal{padding:8rem 5rem;font-size:13rem;color:#bfc4ca;overflow-wrap:break-word;animation:rll-arrive 170ms ease-out}.rll-legend{flex-wrap:wrap}.rll-legend span{margin-right:10rem}
.rll-direction,.rll-shell button{transition:background-color 140ms,border-color 140ms,transform 120ms}.rll-direction:active,.rll-shell button:active{transform:scale(.97)}.rll-direction:hover,.rll-direction:focus-visible{border-color:#6aa6ea!important}
.rll-tabs{flex:0 1 auto;overflow-x:auto;min-width:0;padding:3rem;border-radius:999rem;background:#1d2126}.rll-tabs button,.rll-eye,.rll-line-name{border:0;background:transparent;font-size:14rem}
.rll-tabs{position:relative}.rll-tab-indicator{position:absolute;top:3rem;bottom:3rem;width:52rem;border-radius:999rem;background:#5393db;transition:left 160ms ease}.rll-tabs button{position:relative;width:52rem;padding:5rem 2rem;flex-shrink:0;border:0;border-radius:999rem}.rll-tabs button[aria-selected=true]{color:#112338;font-weight:600}
.rll-bar{margin:4rem 0 6rem}.rll-bar>.rll-eye{margin-left:auto}.rll-tr{min-height:37rem;text-align:center}
.rll-tr>span,.rll-tr>div{width:10%;flex-shrink:0}.rll-tr>:first-child{width:41%;text-align:left}.rll-tr>:nth-child(4){width:29%}.rll-tr>:last-child{margin-left:auto}
.rll-sort{width:100%;display:flex;justify-content:center;align-items:center;padding:5rem 0;background:transparent;border:0;font-size:12rem;color:inherit!important}.rll-sort svg{width:10rem;height:10rem;flex-shrink:0}.rll-colnames [aria-sort=ascending],.rll-colnames [aria-sort=descending],.rll-sort:hover{color:#5393db!important}.rll-colnames{color:#a6adb5;font-size:12rem}.rll-line{margin-top:6rem;padding:0 6rem;border:1rem solid #3a434d;border-top-color:#56616e;border-radius:12rem;background:#2e343b}.rll-line:hover{background:#333a43}.rll-line[data-open="1"]{border-color:#3d4f66;border-top-color:#5393db}
.rll-swatch{width:12rem;height:22rem;flex-shrink:0;padding:0;border-radius:4rem;border:1rem solid #868d95;margin-right:7rem}
.rll-line-name{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;text-align:left;padding:4rem 0}.rll-eye{padding:5rem;color:#bac1c8}
.rll-rate{font-size:13rem;justify-content:center}.rll-rate span{width:36rem;text-align:right;margin-right:6rem}.rll-rate svg{width:22rem;height:22rem;flex-shrink:0}.rll-rate i{display:flex;flex:1;height:5rem;border-radius:4rem;background:#535960;overflow:hidden}.rll-rate b{height:100%;background:#79deb7;transition:width 230ms ease}
.rll-track{display:flex;align-items:center;justify-content:flex-start;width:34rem;height:20rem;padding:3rem;box-sizing:border-box;border-radius:10rem;background:#626b72;transition:background-color 150ms ease}.rll-track[data-on="1"]{justify-content:flex-end;background:#5393db}.rll-track>span{width:14rem;height:14rem;border-radius:7rem;background:#fff}.rll-track[data-on="1"]>span{background:#18263a}
.rll-card{transition:border-color 150ms ease,background-color 150ms ease}
.rll-pills{display:flex;flex-wrap:wrap;flex-shrink:0;align-items:center;align-content:flex-start;margin:2rem -3rem 4rem}.rll-pills.rll-oneline{flex-wrap:nowrap;overflow:hidden}.rll-rangego{display:block;margin:6rem 0 2rem}.rll-range{margin-top:6rem;padding:10rem 12rem;border:1rem solid #3a434d;border-radius:12rem;background:#2e343b}.rll-rangesteps{display:flex;flex-direction:column}.rll-rangesteps>span{padding:4rem 0;font-size:13rem;color:#a6adb5}.rll-rangesteps>span[data-on="1"]{color:#5393db}.rll-rangesteps>span[data-done="1"]{color:#e8ecf0}.rll-rangeinfo{display:flex;flex-direction:column;margin:6rem 0 4rem}.rll-rangeinfo>strong{font-size:14rem}.rll-rangeinfo>small{font-size:12rem;color:#a6adb5}.rll-rangebtns{display:flex;margin-top:8rem}.rll-rangebtns>button{height:26rem;margin-right:6rem;padding:0 13rem;border:0;border-radius:999rem;background:#343a40;color:#e8ecf0;font-size:12rem;cursor:pointer}.rll-rangebtns>button:hover{background:#3d444d;color:#5393db}.rll-headstats{display:flex;align-items:center;flex-shrink:0}.rll-headstats>small{font-size:11rem;color:#a6adb5;white-space:nowrap}.rll-headstats>small[data-tone=amber]{color:#f1be72;margin-right:4rem}.rll-pills>*{margin:3rem}
.rll-pill{flex-shrink:0;padding:1rem 9rem;border:1rem solid #4a525c;border-radius:999rem;font-size:11rem;color:#dfe3e8;white-space:nowrap}
.rll-pill[data-tone="mint"]{border-color:#3d4f66;background:#24303d;color:#5393db}.rll-pill[data-tone="amber"]{border-color:#8a6d3f;background:#3a3123;color:#f1be72}
.rll-roadcard{margin:8rem 0;padding:10rem 12rem;border:1rem solid #3d4f66;border-top-color:#5393db;border-radius:12rem;background:#2e343b}
.rll-roadcard>div:first-child{display:flex;align-items:center;justify-content:space-between}.rll-roadcard strong{font-size:16rem}
.rll-dircard{display:flex;align-items:center;width:100%;margin-top:8rem;padding:9rem 12rem;box-sizing:border-box;border:1rem solid #3a434d;border-top-color:#5d6a78;border-radius:12rem;background:#2e343b;text-align:left;cursor:pointer}
.rll-dircard[data-on="1"]{border-color:#3d4f66;border-top-color:#5393db}.rll-dircard[data-focus="1"],.rll-dircard:hover{background:#333a43}
.rll-dirtext{flex:1;min-width:0;display:flex;flex-direction:column;margin:0 10rem}.rll-dirtext strong{font-size:14rem;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.rll-dirtext small{margin-top:2rem;font-size:12rem;color:#a9b0b9!important}.rll-dircard[data-on="1"] .rll-dirtext small{color:#5393db!important}
.rll-quick{display:flex;flex-wrap:wrap;margin:8rem -3rem 0}.rll-quick button{display:flex;align-items:center;margin:3rem;padding:5rem 13rem;border:0;border-radius:999rem;background:#343a40;font-size:12rem}.rll-quick button:hover{background:#3d444b}.rll-quick svg{margin-right:5rem}
.rll-listhead{display:flex;align-items:baseline;justify-content:space-between;margin:12rem 0 0;font-size:13rem;color:#a9b0b9}
.rll-rulecard{display:flex;align-items:center;margin-top:6rem;padding:6rem 8rem 6rem 12rem;border:1rem solid #3a434d;border-top-color:#56616e;border-radius:12rem;background:#2e343b}.rll-rulecard:hover{background:#333a43}.rll-rulecard>.rll-pill{margin-left:5rem}
.rll-rulename{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;padding:3rem 0;border:0;background:transparent;font-size:13rem;text-align:left}.rll-rulename:hover{color:#5393db!important}
.rll-iconbtn{display:flex;align-items:center;justify-content:center;width:26rem;height:26rem;margin-left:5rem;padding:0;border:0;border-radius:999rem;background:transparent;color:#a9b0b9!important}.rll-iconbtn:hover{background:#3d444b;color:#5393db!important}
.rll-empty{margin:12rem 0 4rem;font-size:12rem;color:#a9b0b9}
.rll-tiles{display:flex;margin:10rem -3rem 8rem}.rll-tile{flex:1;min-width:0;display:flex;align-items:center;margin:0 3rem;padding:8rem;border:1rem solid #3a434d;border-top-color:#5d6a78;border-radius:12rem;background:#2e343b}
.rll-tile[data-tone="amber"]{border-color:#8a6d3f;border-top-color:#f1be72}.rll-tile svg{width:30rem;height:30rem;flex-shrink:0;margin-right:7rem}.rll-tile small{display:block;font-size:11rem}.rll-tile strong{display:block;font-size:17rem;animation:rll-arrive 180ms ease-out}.rll-tile[data-tone="amber"] strong{color:#f1be72}
.rll-mix{display:flex;height:7rem;margin:2rem 0 4rem;border-radius:4rem;overflow:hidden;background:#3d434a}.rll-mix>span{height:100%;transition:width 230ms ease}
.rll-mix-labels{display:flex;flex-wrap:wrap;font-size:11rem;color:#a9b0b9}.rll-mix-labels span{margin-right:10rem}.rll-mix-labels i{display:inline-block;width:8rem;height:8rem;margin-right:4rem;border-radius:2rem}
.rll-spot{display:flex;flex-direction:column;align-items:stretch;width:100%;margin-top:6rem;padding:8rem 10rem;box-sizing:border-box;border:1rem solid #3a434d;border-top-color:#56616e;border-radius:12rem;background:#2e343b;text-align:left}
.rll-spot:hover{border-color:#3d4f66;border-top-color:#5393db;background:#333a43}.rll-spot-top{display:flex;align-items:center}.rll-spot-top strong{flex:1;min-width:0;font-size:13rem;font-weight:500;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.rll-spot-top .rll-pill{margin-left:5rem}
.rll-spot-flow{display:flex;align-items:center;margin-top:5rem}.rll-spot-flow i{flex:1;height:5rem;border-radius:3rem;background:#535960;overflow:hidden;display:flex}.rll-spot-flow b{display:block;height:100%}.rll-spot-flow small{margin-left:8rem;font-size:11rem;color:#bfc4ca!important}
.rll-pill[data-tone="red"]{border-color:#8c4c46;background:#3d2826;color:#fa9e90}
.rll-tile em{display:block;font-style:normal;font-size:10rem;color:#9fa7b0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.rll-tile svg{width:34rem;height:34rem}
.rll-trendbox{margin-top:8rem}.rll-range button{margin-left:4rem;padding:1rem 7rem;border:1rem solid #3a434d;border-radius:999rem;background:transparent;color:#9fa7b0;font-size:11rem}.rll-range button[data-on="1"]{border-color:#4a86c8;color:#5393db}
.rll-trendchart{position:relative;height:84rem;margin-top:22rem}.rll-trendchart svg{width:100%;height:84rem;display:block}
.rll-trendmark{position:absolute;transform:translate(-50%,-100%);margin-top:-2rem;max-width:110rem;padding:0 5rem;border-radius:999rem;background:#3d3620;color:#f0d37a;font-size:10rem;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.rll-trendaxis,.rll-trendlegend{display:flex;justify-content:space-between;font-size:10rem;color:#9fa7b0;margin-top:3rem}.rll-trendlegend{justify-content:flex-start}.rll-trendlegend span{margin-right:10rem}
.rll-usage-toggle{margin-top:5rem;align-self:flex-start}
.rll-usage{display:block;margin-top:6rem;padding-top:6rem;border-top:1rem solid #3a4047;cursor:default}.rll-usage small{display:block;font-size:11rem;color:#9fa7b0}
.rll-usage-cols{display:flex;margin-top:4rem}.rll-usage-cols>div{flex:1;min-width:0;margin-right:8rem}
.rll-usage-row{display:flex;align-items:center;font-size:11rem;margin-top:3rem}.rll-usage-row span{width:58rem;flex-shrink:0}.rll-usage-row i{flex:1;height:5rem;border-radius:3rem;background:#3a4047;overflow:hidden;display:flex}.rll-usage-row b{display:block;height:100%}.rll-usage-row em{font-style:normal;margin-left:6rem;min-width:28rem;text-align:right}
.rll-usage-split{display:flex;height:7rem;border-radius:4rem;overflow:hidden;background:#3a4047;margin-top:3rem}.rll-usage-split b{display:block;height:100%}
.rll-usage-tip{display:flex;justify-content:space-between;align-items:center;margin-top:6rem;padding:5rem 8rem;border:1rem solid #6b5a33;border-radius:8rem;background:#2e2a1f;color:#f0d37a;font-size:11rem}
.rll-trend{display:flex;align-items:flex-end;height:28rem;margin:4rem 0}.rll-trend span{flex:1;margin-right:2rem;border-radius:2rem 2rem 0 0;background:#79deb7;min-height:2rem}
.rll-kindrow{display:flex;justify-content:space-between;padding:3rem 0;border-top:1rem solid #3a4047;font-size:12rem}
.rll-pillbtn{background:transparent;cursor:pointer;font-family:var(--fontFamily)}.rll-pillbtn:hover{border-color:#5393db}.rll-rulecard>.rll-pillbtn{margin-left:5rem}
.rll-jamroad .rll-artbox{width:38%;height:96rem}.rll-jamroad .rll-hero-text{flex:1;min-width:0}.rll-jamroad .rll-pills{margin:6rem -3rem 0}
.rll-link{align-self:flex-start;margin-top:4rem;padding:2rem 0;border:0;background:transparent;font-size:12rem;color:#5393db!important}.rll-link:hover{text-decoration:underline}
.rll-jamcard{display:flex;flex-direction:column;align-items:stretch;width:100%;margin-top:6rem;padding:8rem 12rem;box-sizing:border-box;border:1rem solid #3a434d;border-top-color:#56616e;border-radius:12rem;background:#2e343b;text-align:left}
.rll-jamcard:hover{background:#333a43}.rll-jamcard[data-live="1"]{border-color:#3d4f66;border-top-color:#5393db}
.rll-jamtop{display:flex;align-items:center}.rll-jamtop strong{flex:1;min-width:0;font-size:14rem;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.rll-jamtop .rll-pill{margin-left:6rem}
.rll-jamdot{width:7rem;height:7rem;margin-left:6rem;border-radius:50%;background:#f1be72;animation:rll-breathe 1.6s ease-in-out infinite}
.rll-jamcard small{margin-top:3rem}.rll-jamcard .rll-pills{margin:4rem -3rem 0}
.rll-modhero{position:relative}.rll-modhero .rll-artbox{width:42%;min-height:118rem;height:auto;align-self:stretch;overflow:hidden}.rll-modhero .rll-artbox>.rll-art{position:absolute;left:0;top:0}.rll-modhero .rll-hero-text{flex:1;min-width:0}.rll-modhero[data-picking="0"] .rll-hero-text{padding-bottom:46rem}
.rll-modhero .rll-pills{margin:6rem -3rem 0}.rll-desc{display:block;line-height:1.55}.rll-hint{display:block;color:#5393db!important}
.rll-cta{position:absolute;right:10rem;bottom:10rem;padding:5rem 14rem;border:1rem solid #5393db;border-radius:999rem;background:#1f2c3b;font-size:12rem;color:#5393db!important}.rll-cta:hover{background:#283c55}
.rll-paint{position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none}.rll-paint span{position:absolute;left:41%;transform:translateY(-50%);font-size:9rem;line-height:1;color:#eef0ea;white-space:nowrap;letter-spacing:1rem}
.rll-rowstat{flex-shrink:0;margin:0 6rem;font-size:11rem;color:#5393db!important;white-space:nowrap}
.rll-loop-focus{position:absolute;width:29rem;height:29rem;box-sizing:border-box;border:2rem solid #f1be72;border-radius:50%;transform:translate(-50%,-50%);pointer-events:none;z-index:2}
.rll-veh-mark[data-focus="1"]{box-shadow:0 0 0 2rem #f1be72}.rll-focusline{margin:4rem 0 2rem;font-size:12rem;color:#f1be72}
.rll-namewrap{flex:1;min-width:0;display:flex;align-items:center}.rll-namewrap .rll-rulename{flex:0 1 auto}
.rll-chome{flex-shrink:0;margin-left:6rem;padding:1rem 8rem;border-radius:999rem;background:#3a4149;font-size:11rem;font-weight:400;color:#adb6c1;white-space:nowrap}
.rll-dup{flex-shrink:0;margin-left:6rem;padding:0 8rem;border:1rem dashed #5b6571;border-radius:999rem;font-size:11rem;color:#adb6c1;white-space:nowrap}
.rll-street{display:flex;flex-direction:column;min-width:0}.rll-roadcard>div.rll-street{display:flex;flex-direction:column}
.rll-street-top{display:flex;align-items:center;min-width:0}.rll-street-top strong{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.rll-grow{flex:1}
.rll-rename{display:flex;align-items:center;flex-shrink:0;margin-left:8rem;padding:2rem 0;border:0;background:transparent;font-size:12rem;color:#5393db!important}.rll-rename svg{margin-right:3rem}.rll-rename:hover{text-decoration:underline}
.rll-feedback{margin-top:4rem;font-size:12rem;color:#5393db}
.rll-rename-box{margin-top:8rem;padding:10rem;border:1rem solid #3a434d;border-top-color:#56616e;border-radius:10rem;background:#262c33}
.rll-rename-row{display:flex;align-items:center}.rll-input{flex:1;min-width:0;padding:6rem 10rem;border:1rem solid #5393db;border-radius:8rem;background:#161a1e;color:#f1f3ef;font-family:var(--fontFamily);font-size:13rem}
.rll-apply{flex-shrink:0;margin-left:8rem;padding:6rem 14rem;border:0;border-radius:8rem;background:#5ce6df;color:#0e2440!important;font-size:13rem;font-weight:500}.rll-apply[aria-disabled="true"]{opacity:.45}
.rll-cands{display:flex;flex-wrap:wrap;margin:6rem -3rem 0}.rll-cand{margin:3rem;padding:3rem 10rem;border:1rem solid #3a434e;border-radius:999rem;background:transparent;font-size:12rem;color:#f1f3ef!important}
.rll-cand:hover{border-color:#56616e}.rll-cand[data-on="1"]{border-color:#5393db;background:#1f2c3b;color:#5393db!important}
.rll-note{display:block;margin-top:6rem;font-size:11rem;color:#adb6c1}
.rll-monpin{position:absolute;display:flex;align-items:center;transform:translate(-50%,-50%);padding:4px 10px 4px 7px;border:2px solid #7d848c;border-radius:999px;background:rgba(43,49,51,.97);color:#c9cdd2;font-family:var(--fontFamily);font-size:13px;pointer-events:auto;cursor:pointer;box-shadow:0 2px 6px rgba(0,0,0,.6)}
.rll-monpin span{margin-left:4px}.rll-monpin[data-on="1"]{border-color:#5393db;background:rgba(36,48,61,.97);color:#5393db}
.rll-dircaps{display:flex;margin:8rem -4rem 0}
.rll-dircap{flex:1;min-width:0;display:flex;align-items:center;margin:0 4rem;padding:6rem 10rem 6rem 6rem;box-sizing:border-box;border:1rem solid #3a424b;border-radius:999rem;background:#232b33;text-align:left;cursor:pointer;transition:background-color 250ms,border-color 250ms,transform 120ms}
.rll-dircap:hover{border-color:#5d6a78}.rll-dircap:active{transform:scale(.97)}
.rll-dircap[data-on="1"]{background:#1e3a5c;border-color:#5393db}.rll-dircap[data-focus="1"]{border-color:#5393db}
.rll-dircap-ic{flex-shrink:0;display:flex;align-items:center;justify-content:center;width:40rem;height:40rem;border-radius:50%;background:#2f363f;transition:background-color 250ms}
.rll-dircap-tx{flex:1;min-width:0;display:flex;flex-direction:column;margin:0 8rem}.rll-dircap-tx strong{font-size:13rem;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.rll-dircap-tx small{margin-top:2rem;font-size:11rem;color:#9fa7b0!important;transition:color 250ms}.rll-dircap[data-on="1"] .rll-dircap-tx small{color:#5393db!important}
.rll-dircap .rll-track{flex-shrink:0}
.rll-xmark{stroke-dasharray:1;stroke-dashoffset:1;transition:stroke-dashoffset 300ms ease 80ms}[data-on="1"] .rll-xmark,.rll-xmark[data-on="1"]{stroke-dashoffset:0}
.rll-ruletabs{display:flex;margin:8rem 0 6rem;padding:3rem;border-radius:999rem;background:#1d2126}
.rll-ruletabs button{position:relative;flex:1;padding:5rem 2rem;border:0;border-radius:999rem;background:transparent;font-size:13rem;color:#c9ced4!important;transition:background-color 160ms,color 160ms}
.rll-ruletabs button[aria-selected=true]{background:#5393db;color:#112338!important;font-weight:600}
.rll-ruletabs button[data-active="1"]:after{content:'';position:absolute;top:5rem;right:8rem;width:5rem;height:5rem;border-radius:50%;background:#5393db}
.rll-ruletabs button[aria-selected=true][data-active="1"]:after{background:#112338}
.rll-seg2{display:flex;flex-wrap:wrap;margin:6rem -3rem}.rll-seg2 button{margin:3rem;padding:6rem 12rem;border:1rem solid #3a424b;border-radius:999rem;background:#232b33;font-size:13rem}
.rll-seg2 button[aria-pressed=true]{background:#1e3a5c;border-color:#5393db;color:#e8eff6!important}
.rll-note{display:block;margin:6rem 2rem;font-size:12rem;color:#9fa7b0!important}
.rll-lanebox{flex:1;min-width:0;margin:0 4rem;padding:8rem 10rem;border:1rem solid #3a424b;border-radius:16rem;background:#232b33}
.rll-lanebox strong{display:block;font-size:13rem;margin-bottom:6rem}
.rll-lanecells{display:flex}.rll-lanecells button{flex:1;min-width:0;margin:0 2rem;padding:6rem 0;border:1rem solid #3a424b;border-radius:8rem;background:#2f363f;font-size:12rem}
.rll-lanecells button[aria-pressed=true]{background:#4a2a28;border-color:#e5534b;color:#f6c3bf!important}
.rll-badges{position:fixed;display:flex;transform:translate(-50%,-100%);pointer-events:auto}
.rll-badge2{display:flex;align-items:center;justify-content:center;width:28rem;height:28rem;margin:0 3rem;padding:0;border:2rem solid #5d6a78;border-radius:50%;background:#1e2125;font-size:11rem;font-weight:700;color:#e8ecf0!important;cursor:pointer}
.rll-badge2[data-kind=speed]{border-color:#e5534b;background:#f4f4f0;color:#1e2125!important}
.rll-rulepin{position:fixed;display:flex;align-items:center;padding:5rem 10rem 5rem 5rem;transform:translate(-50%,-50%);border:2rem solid #3a424b;border-radius:999rem;background:#1e2125;pointer-events:auto;font-size:12rem}
.rll-rulecard[data-go="1"]{cursor:pointer}.rll-rulecard[data-go="1"]:hover{border-color:#4a86c8;background:#2f363d}
.rll-rulepin[data-on="1"]{border-color:#5393db;background:#1e3a5c}.rll-rulepin>span{margin-left:6rem}
.rll-kindrow{position:fixed;display:flex;transform:translate(-50%,-50%);padding:2rem;border-radius:999rem;background:rgba(20,24,26,.72);pointer-events:auto}
.rll-kindbtn{display:flex;align-items:center;justify-content:center;width:32rem;height:32rem;margin:0 1rem;padding:0;border:1.5rem solid #5f666d;border-radius:50%;background:#1e2125}
.rll-kindbtn:hover{border-color:#5393db}.rll-caret{display:inline-block}.rll-sweep{transform-origin:18px 18px;animation:rll-sweep 4s linear infinite}@keyframes rll-sweep{from{transform:rotate(0deg)}to{transform:rotate(360deg)}}.rll-kindspot{position:fixed;margin:0;pointer-events:auto;z-index:3}.rll-kindbtn[data-banned="1"]{border-color:#e5484d;background:#2a1e20}
.rll-kindlegend{display:flex;flex-wrap:wrap;margin:6rem 0 2rem}.rll-kindlegend>span{display:flex;align-items:center;margin:0 10rem 4rem 0;font-size:12rem;color:#c9ced3}.rll-kindlegend svg{margin-right:3rem}
.rll-warnline{margin:4rem 0;padding:6rem 10rem;border:1rem solid #8a4a44;border-radius:10rem;background:#3a2729;color:#ffc5b5;font-size:12rem}
.rll-mapnotice{position:fixed;transform:translate(-50%,-100%);padding:5rem 10rem;border:1rem solid #8a4a44;border-radius:10rem;background:#3a2729;color:#ffc5b5;font-size:12rem;pointer-events:none;white-space:nowrap}
.rll-shake{animation:rll-shake .45s ease-in-out 1}
@keyframes rll-shake{0%,100%{margin-left:0}20%{margin-left:-6rem}40%{margin-left:6rem}60%{margin-left:-4rem}80%{margin-left:4rem}}
.rll-speed{margin-top:6rem;padding:10rem 12rem;border:1rem solid #3a434d;border-radius:12rem;background:#2e343b}
.rll-speed-head{display:flex;justify-content:space-between;align-items:baseline;margin-bottom:8rem;font-size:13rem}.rll-speed-head strong{color:#5393db;font-size:15rem}
.rll-speed-row{display:flex;align-items:center}
.rll-speed-step{width:24rem;height:24rem;padding:0;border:1rem solid #4a5057;border-radius:50%;background:#1e2125;color:#e8ecf0;font-size:15rem;line-height:20rem}
.rll-speed-track{position:relative;flex:1;height:22rem;margin:0 10rem;cursor:pointer}
.rll-speed-rail{position:absolute;left:0;right:0;top:8rem;height:6rem;border-radius:3rem;background:#464c52}
.rll-speed-fill{position:absolute;left:0;top:8rem;height:6rem;border-radius:3rem;background:#4a86c8}
.rll-speed-home{position:absolute;top:3rem;width:2rem;height:16rem;margin-left:-1rem;background:#f2d48a}.rll-speed-ticks>span[data-home="1"]{color:#f2d48a}.rll-speed-head strong[data-home="1"]{color:#f2d48a}
.rll-tip{position:fixed;transform:translate(-50%,-100%);padding:4rem 9rem;border:1rem solid #4a86c8;border-radius:8rem;background:#0f1216;color:#e6ece9;font-size:12rem;white-space:nowrap;pointer-events:none;z-index:5}
.rll-wholeline{margin:4rem 0;padding:6rem 10rem;border:1rem solid #b98a3e;border-radius:10rem;background:#3a3222;color:#f2d48a;font-size:12rem}
.rll-modhero .rll-pills{flex-wrap:nowrap;overflow:hidden}.rll-modhero .rll-pill{flex-shrink:0}
.rll-speed-knob{position:absolute;top:1rem;width:20rem;height:20rem;margin-left:-10rem;border-radius:50%;background:#5393db}
.rll-speed-ticks{position:relative;height:16rem;margin:4rem 34rem 0}.rll-speed-ticks>span{position:absolute;transform:translateX(-50%);font-size:11rem;color:#8a919a;white-space:nowrap}
.rll-cause{display:flex;align-items:center;justify-content:space-between;margin-top:4rem}.rll-cause small{color:#f1be72!important}
.rll-effect{margin-top:6rem;padding:7rem 10rem;border:1rem solid #3a434d;border-radius:12rem;background:#2e343b}.rll-effect strong{display:block;font-size:13rem}.rll-effect small{display:block;margin-top:2rem}
@keyframes rll-trace{from{stroke-dashoffset:1}to{stroke-dashoffset:0}}
@keyframes rll-arrive{from{opacity:.25;transform:translateY(3rem)}to{opacity:1;transform:translateY(0)}}
@keyframes rll-breathe{50%{opacity:.45}}
@media(prefers-reduced-motion:reduce){.rll-shell *,.rll-shell *:before,.rll-direction{transition:none!important;animation:none!important}}
`;
const pinSize=48;
const pinSpacing=pinSize+4;
// 拥堵疏导 map pins beside each travel direction of the selected road: 监测中 (mint) / 未监测 (grey).
function MonitorPins({s,toggle,preview}){
 const pose=JSON.parse(useValue(pose$)||'{}');
 if(pose.Session!==s.Session||pose.Index!==s.Index||pose.Version!==s.Version)return null;
 const pins=(s.Controls||[]).filter(Boolean).map(c=>({c,p:c.Direction===1?pose.First:pose.Second})).filter(x=>x.p);
 return h('div',{'aria-label':'监测方向标记',style:{position:'absolute',left:0,top:0,width:'100vw',height:'100vh',pointerEvents:'none'}},
  ...pins.map(({c,p})=>{
   const on=!!(s.MonitorMask&c.Direction);
   return h('button',{key:c.Direction,type:'button',className:'rll-direction rll-monpin','data-on':on?'1':'0',role:'switch','aria-checked':on,'aria-label':'路面监测 '+c.Direction,title:on?'监测中，点击关闭':'未监测，点击开启',
    onMouseDown:e=>e.stopPropagation(),onClick:e=>{e?.stopPropagation();toggle(c);},onMouseEnter:()=>preview(c.Direction),onMouseLeave:()=>preview(0),
    style:{left:p.X*window.innerWidth+'px',top:p.Y*window.innerHeight+'px'}},
    roadIcon('jam',on,p.Angle||0,22,'currentColor'),
    h('span',null,on?'监测中':'未监测'));
  }));
}
const trashIcon=h('svg',{'aria-hidden':true,width:15,height:15,viewBox:'0 0 24 24',fill:'none',stroke:'currentColor',strokeWidth:2,strokeLinecap:'round',strokeLinejoin:'round'},h('path',{d:'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3'}));
function RoadPins({state,controls,selected,toggle,hover}){
 const pose=JSON.parse(useValue(pose$)||'{}');
 if(pose.Session!==state.Session||pose.Index!==state.Index||pose.Version!==state.Version)return null;
 const pins=[];
 for(const c of controls){
  const point=c.Direction===1?pose.First:pose.Second;
  if(point)pins.push({c,x:point.X*window.innerWidth,y:point.Y*window.innerHeight,angle:point.Angle});
 }
 if(pins.length===2){
  const [a,b]=pins,dx=b.x-a.x,dy=b.y-a.y;
  const span=Math.max(Math.abs(dx),Math.abs(dy));
  if(span<pinSpacing){
   const offset=(pinSpacing-span)/2;
   const nx=span>0?dx/span:1,ny=span>0?dy/span:0;
   a.x-=nx*offset;a.y-=ny*offset;b.x+=nx*offset;b.y+=ny*offset;
  }
 }
 return h('div',{'aria-label':'路面方向开关',style:{position:'absolute',left:0,top:0,width:'100vw',height:'100vh',pointerEvents:'none'}},
  ...pins.map(({c,x,y,angle})=>directionButton(c,selected,toggle,{x,y,angle},null,hover)));
}
function directionButton(c,selected,toggle,pin,angle,hover){
 const active=selected===c.Direction;
 const style={display:'flex',alignItems:'center',justifyContent:pin?'center':'space-between',fontFamily:'var(--fontFamily)',color:palette.ink,background:'#33383c',border:'1px solid '+(active?palette.selected:'#687380'),borderRadius:'8rem',cursor:'pointer',pointerEvents:'auto',boxSizing:'border-box'};
 if(pin)Object.assign(style,{position:'absolute',left:(pin.x-pinSize/2)+'px',top:(pin.y-pinSize/2)+'px',width:pinSize+'px',height:pinSize+'px',padding:0,borderRadius:'13px',background:'rgba(40,55,66,0.98)',border:'2px solid '+(active?palette.selected:'#BAC6C9'),boxShadow:'0 2px 6px rgba(0,0,0,0.72)'});
 else Object.assign(style,{flex:1,minWidth:0,flexDirection:'column',padding:'9rem',margin:'2rem'});
 const symbol=roadIcon('lane',c.On,pin?.angle??angle??0,pin?34:34,active?palette.selected:palette.ink);
 return h('button',{key:c.Direction,type:'button',className:'rll-direction',title:c.On?'禁止变道':'可变道',onMouseEnter:()=>hover?.(c.Direction),onMouseLeave:()=>hover?.(0),onFocus:()=>hover?.(c.Direction),onBlur:()=>hover?.(0),role:'switch','aria-checked':c.On,'aria-label':(pin?'路面方向 ':'切换方向 ')+c.Direction,onMouseDown:e=>e.stopPropagation(),onClick:e=>{e?.stopPropagation();toggle(c);},style},
  symbol,!pin&&h('span',null,c.On?'禁止变道':'可变道'));
}
// Road operation icons, drawn pointing right and rotated to the travel direction on screen.
// Lane change: a straight arrow with a lane-change arrow splitting off; a red cross over the whole icon when banned.
// Jam relief: a faded turn arrow giving way to the straight arrow.
export function roadIcon(kind,on,angle,size,ink){
 const arrows=kind==='jam'?[h('path',{key:'t',d:'M5 22H14Q19 22 19 17V9M15 13L19 9L23 13',opacity:.4}),h('path',{key:'s',d:'M5 22H27M21 16L27 22L21 28'})]
  :[h('path',{key:'s',d:'M4 20H27M21 14L27 20L21 26'}),h('path',{key:'b',d:'M9 20Q15 20 17 13Q18.5 9 25 9M20.5 5L25 9L20.5 13'})];
 return h('svg',{'aria-hidden':true,width:size,height:size,viewBox:'0 0 32 32',fill:'none',strokeLinecap:'round',strokeLinejoin:'round',style:{flexShrink:0,transform:'rotate('+(angle||0)+'deg)'}},
  h('g',{stroke:ink,strokeWidth:2.4},...arrows),
  kind!=='jam'&&h('path',{className:'rll-xmark','data-on':on?'1':'0',pathLength:1,d:'M6 6L26 26M26 6L6 26',stroke:'#e5534b',strokeWidth:3.2}));
}
// Left-to-right order of the two travel directions as their lanes appear on screen
// (camera and left/right-hand traffic both follow from where the direction anchors are).
export function directionOrder(pose,valid,dirs){
 const a=valid&&pose.First,b=valid&&pose.Second;
 if(!a||!b||dirs.length<2)return dirs;
 const dx=b.X*window.innerWidth-a.X*window.innerWidth,dy=b.Y*window.innerHeight-a.Y*window.innerHeight;
 const firstFirst=Math.abs(dx)>=Math.abs(dy)?dx>=0:dy>=0;
 return firstFirst?[1,2].filter(d=>dirs.includes(d)):[2,1].filter(d=>dirs.includes(d));
}
function DirCapsule({c,kind,on,focus,title,status,tip,angle,label,onToggle,onHover}){
 return h('button',{key:c.Direction,type:'button',className:'rll-dircap','data-on':on?'1':'0','data-focus':focus?'1':'0',title:tip||status,role:'switch','aria-checked':on,'aria-label':label,
  onMouseEnter:()=>onHover?.(c.Direction),onMouseLeave:()=>onHover?.(0),onFocus:()=>onHover?.(c.Direction),onBlur:()=>onHover?.(0),onMouseDown:e=>e.stopPropagation(),onClick:e=>{e?.stopPropagation();onToggle(c);}},
  h('span',{className:'rll-dircap-ic'},roadIcon(kind,on,angle,26,on?'#e8eff6':palette.ink)),
  h('span',{className:'rll-dircap-tx'},h('strong',null,title),status?h('small',null,status):null),
  h('span',{'aria-hidden':true,className:'rll-track','data-on':on?'1':'0'},h('span',null)));
}
function RoadCards({s,controls,selected,toggle,hover}){
 const pose=JSON.parse(useValue(pose$)||'{}');
 const valid=pose.Session===s.Session&&pose.Index===s.Index&&pose.Version===s.Version;
 const order=directionOrder(pose,valid,controls.map(c=>c.Direction));
 return h('div',{className:'rll-dircaps'},...order.map(d=>controls.find(c=>c.Direction===d)).map(c=>{
  const first=c.Direction===1;
  const angle=valid?(first?pose.First:pose.Second)?.Angle??(first?90:-90):(first?90:-90);
  const toward=s.RoadExtras?.[first?'Toward1':'Toward2'];
  return h(DirCapsule,{key:c.Direction,c,kind:'lane',on:c.On,focus:selected===c.Direction,angle,label:'切换方向 '+c.Direction,
   title:toward?'驶向'+toward:'此方向',status:c.On?'禁止变道 · 路面已涂字':'可变道',onToggle:toggle,onHover:hover});
 }));
}
function JamCapsules({s,selected,headingOf,toggle,preview}){
 const pose=JSON.parse(useValue(pose$)||'{}');
 const valid=pose.Session===s.Session&&pose.Index===s.Index&&pose.Version===s.Version;
 const order=directionOrder(pose,valid,selected.map(c=>c.Direction));
 return h('div',{className:'rll-dircaps'},...order.map(d=>selected.find(c=>c.Direction===d)).map(c=>{
  const on=!!(s.MonitorMask&c.Direction),node=c.Direction===1?s.Description?.Start:s.Description?.End;
  const p=valid?(c.Direction===1?pose.First:pose.Second):null;
  return h(DirCapsule,{key:c.Direction,c,kind:'jam',on,angle:p?.Angle??(c.Direction===1?90:-90),label:'驶向节点 #'+node,
   title:headingOf(c.Direction),status:'',tip:on?'监测中 · 头车可改直行':'未监测',onToggle:toggle,onHover:preview});
 }));
}
// Street name tags: chome of a segment along its street, and other streets sharing the name.
export function streetTags(r){
 return [r.Chome&&h('span',{key:'c',className:'rll-chome'},r.Chome),
  r.SameName>0&&h('span',{key:'d',className:'rll-dup',title:'全城另有 '+r.SameName+' 条街也叫这个名字，选中后可改名'},'另有 '+r.SameName+' 条同名街')].filter(Boolean);
}
const nameCell=(r,button)=>h('span',{className:'rll-namewrap'},button,...streetTags(r));
const pencil=h('svg',{'aria-hidden':true,width:12,height:12,viewBox:'0 0 24 24',fill:'none',stroke:'currentColor',strokeWidth:2.2,strokeLinecap:'round',strokeLinejoin:'round'},h('path',{d:'M4 20h4L19 9l-4-4L4 16zM13.5 6.5l4 4'}));
// Selected road's street name with chome, duplicates and renaming of the whole street.
export function StreetName({s,trailing}){
 const st=s.Street;
 const [edit,setEdit]=React.useState(false);
 const [value,setValue]=React.useState('');
 React.useEffect(()=>{setEdit(false);},[s.Index,s.Version]);
 if(!st)return h('div',{className:'rll-street'},h('div',{className:'rll-street-top'},h('strong',null,s.RoadName||'所选道路'),h('span',{className:'rll-grow'}),trailing));
 const send=(event,name)=>trigger('RoadLaneLock',event,JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version,Name:name}));
 const clean=String(value??'').trim();
 const apply=()=>{if(!clean||clean.length>32||clean===st.Name)return;send('renameStreet',clean);setEdit(false);};
 return h('div',{className:'rll-street'},
  h('div',{className:'rll-street-top'},h('strong',null,st.Name||'未命名道路'),
   st.Chome&&h('span',{className:'rll-chome'},st.Chome+' · 共 '+st.Segments+' 段'),
   st.SameName>0&&h('span',{className:'rll-dup',title:'全城另有 '+st.SameName+' 条街也叫这个名字'},'另有 '+st.SameName+' 条同名街'),
   h('span',{className:'rll-grow'}),
   h('button',{type:'button',className:'rll-rename','aria-expanded':edit,onClick:()=>{setValue(st.Name||'');setEdit(!edit);}},pencil,edit?'收起':'改名'),
   trailing),
  st.Feedback&&h('small',{className:'rll-feedback'},st.Feedback),
  edit&&h('div',{className:'rll-rename-box'},
   h('div',{className:'rll-rename-row'},
    h('input',{type:'text',className:'rll-input',value,maxLength:32,'aria-label':'新路名',onChange:e=>setValue(e.target.value),onKeyDown:e=>{if(e.key==='Enter')apply();}}),
    h('button',{type:'button',className:'rll-apply','aria-disabled':!clean||clean===st.Name,onClick:apply},'应用')),
   (st.Suggestions||[]).length>0&&h('div',{className:'rll-cands'},...st.Suggestions.map(n=>h('button',{key:n,type:'button',className:'rll-cand','data-on':n===clean?'1':'0',onClick:()=>setValue(n)},n))),
   h('small',{className:'rll-note'},'改的是整条街'+(st.Segments>1?'（'+st.Segments+' 段）':'')+'，地图上的路名同步更新'),
   st.Custom&&h('button',{type:'button',className:'rll-link',onClick:()=>{send('resetStreetName','');setEdit(false);}},'恢复原名')));
}
// List rows: a click anywhere on the row jumps to the place; buttons inside the row only do their own job.
const rowGo=go=>({onClick:go,'data-go':'1'});
const own=fn=>e=>{e?.stopPropagation();fn();};
// Rule list on the lane-control home page; clicking a row jumps to that road.
function RuleRoadList({s}){
 const roads=s.RuleRoads||[];
 if(!roads.length)return h('p',{className:'rll-empty'},'还没有设置规则的路段。点击地图上的道路开始。');
 const go=r=>trigger('RoadLaneLock','focusRuleRoad',JSON.stringify({Session:s.Session,Index:r.Index,Version:r.Version}));
 return h('div',null,h('div',{className:'rll-listhead'},h('span',null,'已设路段'),h('small',null,'点击定位')),
  ...roads.map(r=>h('div',{key:r.Index+'/'+r.Version,className:'rll-rulecard',...rowGo(()=>go(r))},
   nameCell(r,h('button',{type:'button',className:'rll-rulename',title:'定位到 '+(r.Name||'未命名道路'),onClick:own(()=>go(r))},r.Name||'未命名道路')),
   r.Exits>0&&h('small',{className:'rll-rowstat',title:'按道驶出（本次进游戏起）'},(r.Exits||0).toLocaleString()+' 辆'),
   ...[1,2].filter(d=>r.Mask&d).map(d=>h('span',{key:d,className:'rll-pill','data-tone':'mint'},(d===1?'A ':'B ')+'禁变道')),
   r.Speed>0&&h('span',{className:'rll-pill','data-tone':'mint'},t('限速 {0}',r.Speed)),
   r.Yellow&&h('span',{className:'rll-pill','data-tone':'amber'},t('双黄线')),
   r.Whole>0&&h('span',{className:'rll-pill','data-tone':'red'},t('整段禁私家车')),
   r.BannedLanes>0&&!r.Whole&&h('span',{className:'rll-pill','data-tone':'mint'},t('分道 {0} 条',r.BannedLanes)),
   r.Detours>0&&h('span',{className:'rll-pill','data-tone':'amber'},'绕行 '+r.Detours),
   h('button',{type:'button',className:'rll-iconbtn','aria-label':'定位到 '+(r.Name||'未命名道路'),onClick:own(()=>go(r))},h('svg',{'aria-hidden':true,width:16,height:16,viewBox:'0 0 24 24',fill:'none',stroke:'currentColor',strokeWidth:2,strokeLinecap:'round'},h('circle',{cx:12,cy:12,r:3}),h('path',{d:'M12 3v3M12 18v3M3 12h3M18 12h3'}))))));
}
const transit$=bindValue('RoadLaneLock','transit','[]');
const transitMeta$=bindValue('RoadLaneLock','transitMeta','{}');
const labels={Bus:'公交',Train:'铁路',Subway:'地铁',Tram:'有轨电车',Ferry:'渡轮',Ship:'船运',Airplane:'航空',Taxi:'出租车',Cargo:'货运'};
const kindLabels={Bus:'公交',Subway:'地铁',Train:'铁路',Tram:'有轨电车',Ferry:'渡轮',Ship:'船运',Airplane:'航空',Helicopter:'直升机',CargoTrain:'货运铁路',CargoShip:'货船',CargoAirplane:'货运航空',CargoBus:'货运汽车'};
const transitStyles=`
.rll-tr>:first-child{width:32%}.rll-tr>:nth-child(2),.rll-tr>:nth-child(4){width:7%}.rll-tr>:nth-child(3),.rll-tr>:nth-child(5){width:12%;font-size:13rem}.rll-tr>:nth-child(6){width:20%}.rll-riders{color:#5393db}.rll-ridebox{display:flex;align-items:center;gap:10rem;margin:0 0 6rem;padding:8rem 12rem;border-radius:10rem;background:#2e343b;border:1rem solid #3a434d}.rll-ridebox>div{display:flex;flex-direction:column}.rll-ridebox small{font-size:11rem;color:#a6adb5}.rll-ridebox strong{font-size:18rem}.rll-ridebox>em{margin-left:auto;font-style:normal;font-size:12rem;color:#a6adb5}.rll-ridebox>em[data-up="1"]{color:#5393db}.rll-ridechips{display:flex;flex-wrap:wrap;gap:6rem;margin:0 0 6rem}.rll-ridechips>span{padding:2rem 8rem;border-radius:999rem;background:#2e343b;border:1rem solid #3a434d;font-size:11rem;color:#c9ced4}
.rll-name{padding-right:4rem;box-sizing:border-box}
.rll-badge{flex-shrink:0;display:flex;align-items:center;justify-content:center;min-width:28rem;height:20rem;padding:0 4rem;box-sizing:border-box;margin-right:7rem;border-radius:999rem;font-size:11rem;font-weight:700}
.rll-flag,.rll-issue b{flex-shrink:0;padding:0 6rem;font-size:10rem;line-height:16rem;font-weight:600;border-radius:999rem;color:#f1be72;border:1rem solid #8a6d3f;background:#3a3123}
.rll-flag{margin-left:5rem}.rll-flag[data-level="2"],.rll-issue[data-level="2"] b{color:#fa9e90;border-color:#8c4c46;background:#3d2826}
.rll-gear,.rll-tr>.rll-eye{flex-shrink:0;display:flex;align-items:center;justify-content:center;width:34rem;height:34rem;padding:0;box-sizing:border-box;border:1rem solid #3d444d;border-radius:999rem;background:#1e2125;color:#bcc3cb!important;transition:background-color 160ms,border-color 160ms,color 160ms}
.rll-gear{margin-left:6rem}.rll-name>.rll-line-name{flex:1 1 0;min-width:0;text-overflow:clip}.rll-name>.rll-pill,.rll-name>.rll-flag,.rll-name>.rll-badge{flex-shrink:0}.rll-marq{display:inline-block;white-space:nowrap;transition-property:transform;transition-timing-function:linear}.rll-gear:hover,.rll-tr>.rll-eye:hover{border-color:#5393db;color:#5393db!important}.rll-gear[aria-expanded=true]{background:#2e4560;border-color:#5393db;color:#5393db!important}
.rll-line>.rll-tr{cursor:pointer}.rll-line>.rll-tr:hover{background:#2a3037}.rll-line-name{cursor:pointer}
.rll-seg{display:flex;border:1rem solid #464c53;border-radius:999rem;overflow:hidden}.rll-seg button{flex:1;height:28rem;padding:0;border:0;border-radius:0;background:transparent;font-size:13rem;color:#bcc3cb!important;transition:background-color 160ms,color 160ms}
.rll-seg button+button{border-left:1rem solid #464c53}.rll-seg button[aria-pressed=true]{background:#5393db;color:#112338!important}
.rll-models{display:flex;flex-wrap:wrap;margin:0 -3rem}.rll-model{display:flex;align-items:center;margin:3rem;padding:3rem 10rem 3rem 3rem;border:1rem solid #464c53;border-radius:999rem;background:#2d3339;font-size:12rem;transition:border-color 160ms,background-color 160ms}
.rll-model img{width:30rem;height:22rem;margin-right:6rem;border-radius:999rem;background:#1d2126}.rll-model[aria-pressed=true]{border-color:#5393db;background:#293a52}.rll-model:hover{border-color:#6aa6ea}
.rll-set-foot{display:flex;align-items:center;justify-content:space-between;margin-top:12rem;padding-top:10rem;border-top:1rem solid #464c53}
.rll-link{padding:0;border:0;background:transparent;font-size:12rem;color:#a9b0b9!important;cursor:pointer}.rll-link:hover{color:#5393db!important}
.rll-del{height:28rem;padding:0 14rem;border:1rem solid #8c4c46;border-radius:999rem;background:transparent;font-size:12rem;color:#fa9e90!important;transition:background-color 160ms,color 160ms}
.rll-del:hover{background:#3d2826}.rll-del[data-armed]{background:#e5534b;border-color:#e5534b;color:#fff!important}
.rll-onboard{display:inline-block;padding:0 7rem;border:1rem solid #4a525c;border-radius:999rem;font-size:12rem;line-height:18rem;white-space:nowrap}
.rll-settings{margin:2rem 0 8rem;padding:10rem 12rem;border:1rem solid #343b42;border-top-color:#4f5a66;border-radius:12rem;background:#262c33;animation:rll-arrive 170ms ease-out}
.rll-set-sec+.rll-set-sec{margin-top:10rem;padding-top:10rem;border-top:1rem solid #464c53}
.rll-set-head{display:flex;align-items:center;justify-content:space-between;margin-bottom:6rem;font-size:13rem}.rll-set-head strong{font-size:15rem;color:#5393db}
.rll-current{display:flex;align-items:center;font-size:12rem;color:#a9b0b9}.rll-current i{width:14rem;height:14rem;margin-right:5rem;border-radius:3rem}
.rll-slider{display:flex;align-items:center}
.rll-step{display:flex;align-items:center;justify-content:center;width:28rem;height:24rem;padding:0;border:0;border-radius:999rem;background:#343a40}.rll-step:disabled{opacity:.45}
.rll-slide{position:relative;flex:1;height:24rem;margin:0 9rem;cursor:pointer}
.rll-slide i{position:absolute;left:0;right:0;top:9rem;height:6rem;border-radius:999rem;background:#3d434a}
.rll-slide b{position:absolute;left:0;top:9rem;height:6rem;border-radius:999rem;background:#79deb7}
.rll-slide span{position:absolute;top:4rem;width:16rem;height:16rem;margin-left:-8rem;box-sizing:border-box;border:2rem solid #79deb7;border-radius:50%;background:#ecf3f1}
.rll-sub{margin:9rem 0 3rem;font-size:12rem;color:#a9b0b9}
.rll-swatches{display:flex;flex-wrap:wrap;margin:0 -3rem}
.rll-chip{display:flex;align-items:center;margin:3rem;padding:3rem 9rem 3rem 3rem;border:1rem solid #464c53;border-radius:999rem;background:#2d3339;font-size:12rem}
.rll-chip i{width:16rem;height:16rem;margin-right:5rem;border-radius:50%}
.rll-chip:hover,.rll-dot-chip:hover{border-color:#6aa6ea}.rll-chip[aria-pressed=true]{border-color:#5393db}
.rll-dot-chip{width:24rem;height:24rem;margin:3rem;padding:0;border:2rem solid #2d3339;border-radius:50%}.rll-dot-chip[aria-pressed=true]{border-color:#ecf3f1}
.rll-countries{display:flex;flex-wrap:wrap;margin:0 -3rem}.rll-country{margin:3rem;padding:3rem 11rem;border:1rem solid #464c53;border-radius:999rem;background:#2d3339;font-size:12rem}.rll-country:hover{border-color:#6aa6ea}.rll-country[aria-selected=true]{background:#5393db;border-color:#5393db;color:#112338!important}
.rll-cities{margin-top:4rem;padding:4rem 8rem 8rem;border:1rem solid #343b42;border-radius:12rem;background:#22272d;animation:rll-arrive 170ms ease-out}.rll-city{margin:7rem 0 2rem;font-size:11rem;color:#a9b0b9}
.rll-fav{display:flex;align-items:center}.rll-x{padding:0 5rem 0 0;border:0;background:transparent;font-size:14rem;color:#a9b0b9!important}
.rll-inline{display:flex;align-items:center;margin-top:7rem}
.rll-input{height:26rem;padding:0 8rem;box-sizing:border-box;border:1rem solid #4a525c;border-radius:999rem;background:#1d2126;color:#ecf3f1;font-size:13rem;font-family:var(--fontFamily)}
.rll-btn{flex-shrink:0;height:26rem;margin-left:6rem;padding:0 13rem;border:0;border-radius:999rem;background:#343a40;font-size:13rem}
.rll-btn[data-primary]{background:#79deb7;color:#112338!important}.rll-btn:disabled{opacity:.5}
.rll-warn,.rll-note{display:block;font-size:12rem}.rll-warn{margin-bottom:4rem;color:#f1be72}.rll-note{margin-top:8rem;color:#a9b0b9}
.rll-diagram{padding:4rem 0 6rem}
.rll-diagram-head{display:flex;align-items:baseline;justify-content:space-between;margin-bottom:5rem}.rll-diagram-head strong{font-size:14rem;color:#ecf3f1}.rll-diagram-head span{font-size:12rem;color:#5393db}
.rll-issue{display:flex;align-items:center;margin:3rem 0;font-size:12rem;color:#bfc4ca}.rll-issue b{margin-right:6rem}
.rll-loop{position:relative;margin:4rem 0 2rem}
.rll-loop>svg{position:absolute;left:0;top:0}
.rll-loop .rll-stop-dot{position:absolute;margin:0;transform:translate(-50%,-50%);z-index:1}
.rll-loop-crowd{position:absolute;width:27rem;height:27rem;box-sizing:border-box;border:1.5rem solid #f1be72;border-radius:50%;transform:translate(-50%,-50%);pointer-events:none}
.rll-loop-name{position:absolute;display:flex;align-items:center;justify-content:center;transform:translateX(-50%);white-space:nowrap}
.rll-loop-name .rll-stop-link{font-size:12rem;text-align:center}.rll-loop-name em{margin-left:3rem;padding:0 3rem;font-style:normal;font-size:10rem;color:#f1be72;border:1rem solid #8a6d3f;border-radius:3rem}
.rll-loop-wait{position:absolute;width:4rem;border-radius:2rem;background:#79deb7;transform:translateX(-50%)}
.rll-loop-count{position:absolute;font-size:10rem;color:#bfc4ca;transform:translateX(3rem)}
.rll-loop .rll-veh-mark{margin:-4.5rem 0 0 -7.5rem;align-items:stretch}
.rll-loop-note{display:flex;flex-wrap:wrap;font-size:11rem;color:#a9b0b9}.rll-loop-note span{margin-right:12rem}
.rll-route{position:relative;width:50%;padding-right:8rem;box-sizing:border-box}
.rll-route-line{position:absolute;left:9rem;top:22rem;bottom:12rem;width:3rem;border-radius:2rem;opacity:.85}
.rll-route-tip,.rll-route-end{display:flex;align-items:center;height:20rem;font-size:11rem;color:#a9b0b9}.rll-route-tip{padding-left:4rem}
.rll-route-tip svg,.rll-route-end svg{margin-right:5rem}
.rll-stop{position:relative;display:flex;align-items:center;min-height:28rem;border-radius:5rem}
.rll-stop[data-top="1"]{background:#2f3944}
.rll-stop-dot{position:relative;flex-shrink:0;display:flex;align-items:center;justify-content:center;width:20rem;height:20rem;margin-right:26rem;border-radius:50%;background:#23282d;font-size:10rem;color:#ecf3f1}.rll-stop-dot svg{position:absolute;left:0;top:0;width:20rem;height:20rem}.rll-stop-dot span{position:relative}
.rll-stop-name{flex:1;min-width:0;display:flex;align-items:baseline;overflow:hidden;white-space:nowrap}
.rll-stop-name span{min-width:0;overflow:hidden;text-overflow:ellipsis;font-size:13rem;color:#dfe3e8}
.rll-stop-name small{min-width:0;flex-shrink:2;overflow:hidden;text-overflow:ellipsis;margin-left:5rem;font-size:10rem;color:#7f8790}
.rll-stop-name em{flex-shrink:0;margin-left:5rem;padding:0 4rem;font-style:normal;font-size:10rem;color:#f1be72;border:1rem solid #8a6d3f;border-radius:3rem}
.rll-stop-link{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;padding:0;border:0;background:transparent;font-size:13rem;color:#dfe3e8!important;text-align:left}.rll-stop-link:hover{color:#5393db!important}.rll-stop[data-top="1"] .rll-stop-link{color:#5393db!important}
.rll-veh-mark{position:absolute;z-index:2;width:9rem;height:15rem;padding:0;box-sizing:border-box;border:2rem solid;border-radius:4rem;background:#1e2227;overflow:hidden;display:flex;align-items:flex-end}.rll-veh-mark b{display:block;width:100%}.rll-veh-mark:hover{border-color:#ffffff !important;filter:brightness(1.35)}
.rll-fleet-fold{margin-top:8rem}
.rll-veh{display:flex;align-items:center;width:100%;min-height:28rem;padding:0 4rem;border:0;border-top:1rem solid #3a4047;background:transparent;font-size:12rem;text-align:left}.rll-veh:hover{background:#343a41}
.rll-veh-code{width:58rem;flex-shrink:0;color:#a9b0b9}.rll-veh-state{flex-shrink:0;margin-right:8rem;padding:0 4rem;font-size:10rem;line-height:16rem;border:1rem solid;border-radius:3rem}
.rll-veh-next{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#dfe3e8}
.rll-veh-load{display:flex;align-items:center;width:104rem;flex-shrink:0;margin-left:6rem}.rll-veh-load i{flex:1;height:4rem;border-radius:3rem;background:#3d434a;overflow:hidden}.rll-veh-load b{display:block;height:100%}.rll-veh-load small{width:52rem;text-align:right;font-size:11rem;color:#bfc4ca}
.rll-veh-gap{width:52rem;flex-shrink:0;text-align:right;font-size:11rem;color:#a9b0b9}
.rll-wait{display:flex;align-items:center;flex-shrink:0;width:50rem}.rll-wait i{flex:1;height:4rem;border-radius:3rem;background:#3d434a;overflow:hidden}.rll-wait b{display:block;height:100%;background:#79deb7}
.rll-wait span{width:22rem;text-align:right;font-size:11rem;color:#bfc4ca}
.rll-switch-row{display:flex;align-items:center;justify-content:space-between;width:100%;padding:6rem 0;border:0;background:transparent;font-size:13rem}
.rll-naming-grid{display:flex;flex-wrap:wrap}
.rll-prefix{display:flex;align-items:center;width:50%;margin:3rem 0;font-size:12rem;color:#bfc4ca}.rll-prefix>span{width:64rem}.rll-prefix input{width:52rem}.rll-prefix em{margin-left:7rem;font-style:normal;color:#5393db}
.rll-preview div{display:flex;align-items:center;margin:2rem 0;font-size:12rem}.rll-preview span{max-width:55%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#a9b0b9}.rll-preview b{margin:0 6rem;font-weight:400;color:#7f8790}.rll-preview strong{color:#5393db}
`;
export function sortTransitRows(rows,key,ascending){
 if(!key)return rows;
 const value=line=>key==='stops'?line.Stops.length:key==='vehicles'?line.Vehicles:key==='onboard'?(line.Load||0):key==='riders'?(line.Riders??null):line.Capacity>0?line.Load/line.Capacity:null;
 return rows.slice().sort((a,b)=>{
  const x=value(a),y=value(b);
  if(x===null||y===null)return Number(x===null)-Number(y===null);
  return ascending?x-y:y-x;
 });
}
export function formatOnboard(line){
 const n=Math.max(0,Math.round(line.Load||0));
 if(line.Mode!=='Cargo')return n+' 人';
 return n>=1000?(Math.round(n/100)/10)+' 吨':n+' 千克';
}
export function badgeInk(hex){
 const c=toColor(hex);if(!c)return '#ffffff';
 return 0.2126*c.r+0.7152*c.g+0.0722*c.b>0.6?'#171b20':'#ffffff';
}
export function colorDistance(a,b){
 const x=toColor(a),y=toColor(b);if(!x||!y)return 999;
 const mean=(x.r+y.r)*127.5,dr=(x.r-y.r)*255,dg=(x.g-y.g)*255,db=(x.b-y.b)*255;
 return Math.sqrt((2+mean/256)*dr*dr+4*dg*dg+(2+(255-mean)/256)*db*db);
}
// Candidate colours per transport mode; recommendations skip colours close to lines already drawn.
export const modeColors={
 Bus:['#2BB673','#00A0E9','#F39800','#E4007F','#8FC31F','#00B7CE','#EA5532','#7D5AA6','#C7A900','#E95295','#3A9F8E','#5B7FD3'],
 Subway:['#FF9500','#F62E36','#B5B5AC','#009BBF','#00BB85','#C1A470','#8F76D6','#00AC9B','#9C5E31','#E85298','#6CBB5A','#0079C2'],
 Train:['#F15A22','#0072BC','#009944','#E60012','#8E44AD','#F8B500','#00A7DB','#6E4C1E'],
 Tram:['#D7003A','#00A381','#F6AD3C','#5E6EB5','#C1272D','#009E96','#A0D468','#B07CC6'],
 Ferry:['#0096D6','#00B4C5','#1D50A2','#4FC1E9','#2E8B9A','#0068B7'],
 Ship:['#0096D6','#00B4C5','#1D50A2','#4FC1E9','#2E8B9A','#0068B7'],
 Airplane:['#7B68EE','#4682B4','#9370DB','#5F9EA0','#6A5ACD'],
 Cargo:['#B8860B','#8B5A2B','#A0522D','#6B8E23','#708090','#C08040','#556B2F','#9C7A3C']};
export function recommendColors(line,lines,count=5){
 const others=lines.filter(l=>l.Id!==line.Id).map(l=>l.Color);
 const far=x=>x.d>=120;
 return (modeColors[line.Mode]||modeColors.Bus).map((hex,i)=>({hex,i,d:others.length?Math.min(...others.map(o=>colorDistance(hex,o))):999}))
  .filter(x=>normalize(x.hex)!==normalize(line.Color))
  .sort((a,b)=>far(a)!==far(b)?(far(a)?-1:1):far(a)?a.i-b.i:b.d-a.d).slice(0,count).map(x=>x.hex);
}
export function similarLines(line,lines){return lines.filter(l=>l.Id!==line.Id&&l.Mode===line.Mode&&colorDistance(l.Color,line.Color)<60).map(l=>l.Name||'未命名线路');}
// Names shared by three or more distinct stops are prefab defaults (e.g. shelter models), not real names.
export function genericStopNames(lines){
 const ids=new Map();
 for(const l of lines)for(const s of l.Stops){if(!ids.has(s.Name))ids.set(s.Name,new Set());ids.get(s.Name).add(s.Id);}
 return name=>!name||(ids.get(name)?.size||0)>=3;
}
const icon24=(d,size=12)=>h('svg',{'aria-hidden':true,width:size,height:size,viewBox:'0 0 24 24',fill:'none',stroke:'currentColor',strokeWidth:2.4,strokeLinecap:'round',strokeLinejoin:'round'},...[].concat(d).map((p,i)=>h('path',{key:i,d:p})));
const trackOf=on=>h('span',{'aria-hidden':true,className:'rll-track','data-on':on?'1':'0'},h('span',null));
// Donut segments for a stop dot: one equal arc per line serving the stop (viewBox 0 0 20 20).
export function ringArcs(count,r=8.6,gap=7){
 if(count<=1)return [];
 const point=a=>{const t=a*Math.PI/180;return (10+r*Math.cos(t)).toFixed(2)+' '+(10+r*Math.sin(t)).toFixed(2);};
 return Array.from({length:count},(_,i)=>{
  const a0=-90+i*360/count+gap/2,a1=-90+(i+1)*360/count-gap/2;
  return 'M'+point(a0)+' A'+r+' '+r+' 0 '+(a1-a0>180?1:0)+' 1 '+point(a1);
 });
}
// Position along the loop in stop units: boarding at its stop, otherwise halfway from the previous one.
export function fleetGaps(fleet,n){
 const placed=fleet.filter(v=>v.At>=0&&n>0).map(v=>({v,p:v.Boarding?v.At:((v.At-0.5)%n+n)%n})).sort((a,b)=>a.p-b.p);
 const gaps=new Map();
 if(placed.length<2)return gaps;
 placed.forEach((x,i)=>{const ahead=placed[(i+1)%placed.length];gaps.set(x.v.Id,((ahead.p-x.p)%n+n)%n);});
 return gaps;
}
const vehicleStates={Moving:['行驶','#5393db'],Boarding:['停站','#ecf3f1'],Full:['满载','#f1be72'],Returning:['回厂','#7f8790'],Disabled:['停运','#7f8790']};
// Closed-loop track: top row left to right, right bend, bottom row right to left, left bend back to stop 1.
export function loopLayout(n,width){
 const R=34,y0=46,y1=y0+2*R,cx0=44,cx1=Math.max(cx0+40,width-44);
 const top=Math.ceil(n/2),bottom=n-top;
 const points=[];
 for(let i=0;i<top;i++)points.push({x:top>1?cx0+(cx1-cx0)*i/(top-1):cx0,y:y0,row:0});
 for(let j=0;j<bottom;j++)points.push({x:cx1-(cx1-cx0)*(j+0.5)/bottom,y:y1,row:1});
 // Position along the loop in stop units (p in [0,n)): straight between stops, around the bends at the ends.
 const at=p=>{
  if(n<2)return points[0]||{x:cx0,y:y0};
  const i=Math.floor(((p%n)+n)%n),f=p-Math.floor(p),a=points[i],b=points[(i+1)%n];
  const bend=(cx,from,to)=>{const t=(from+(to-from)*f)*Math.PI/180;return {x:cx+R*Math.cos(t),y:(y0+y1)/2+R*Math.sin(t)};};
  if(i===top-1&&bottom>0)return bend(cx1,-90,90);
  if(i===n-1)return bend(cx0,90,270);
  return {x:a.x+(b.x-a.x)*f,y:a.y+(b.y-a.y)*f};
 };
 return {R,y0,y1,cx0,cx1,height:y1+44,points,at,
  track:'M'+cx0+' '+y0+' H'+cx1+' A'+R+' '+R+' 0 0 1 '+cx1+' '+y1+' H'+cx0+' A'+R+' '+R+' 0 0 1 '+cx0+' '+y0+' Z'};
}
let routeDiagramWidth=460;
function RouteDiagram({line,stations,generic,issues,focus:marked}){
 const stops=line.Stops,cargo=line.Mode==='Cargo',n=stops.length,fleet=line.Fleet||[];
 const [fleetOpen,setFleetOpen]=React.useState(false);
 // Start from the last measured width (the game's layout is not ready on the first frame),
 // then measure again on the next frame so a first open does not wait for the next data push.
 const [width,setWidth]=React.useState(routeDiagramWidth);
 const box=React.useRef(null);
 const measure=()=>{const w=box.current?.getBoundingClientRect?.().width;if(w>0){routeDiagramWidth=w;setWidth(old=>Math.abs(w-old)>2?w:old);}};
 React.useLayoutEffect(measure);
 React.useEffect(()=>{const raf=window.requestAnimationFrame||(f=>setTimeout(f,16));const cancel=window.cancelAnimationFrame||clearTimeout;const id=raf(measure);return()=>cancel(id);},[]);
 const max=Math.max(1,...stops.map(s=>s.Waiting||0));
 const total=stops.reduce((sum,s)=>sum+(s.Waiting||0),0);
 const topIndex=cargo?-1:stops.reduce((best,s,i)=>(s.Waiting||0)>0&&(best<0||s.Waiting>stops[best].Waiting)?i:best,-1);
 const label=(stop,i)=>generic(stop.Name)?'第 '+(i+1)+' 站':stop.Name;
 const turn=i=>n>3&&stops[(i-1+n)%n].Id===stops[(i+1)%n].Id;
 const focus=id=>trigger('RoadLaneLock','focusEntity',id);
 const hoverVehicle=id=>trigger('RoadLaneLock','highlightVehicle',id);
 const loadText=v=>cargo?formatOnboard({Mode:'Cargo',Load:v.Load}):v.Load+'/'+v.Capacity;
 const vehicleTitle=v=>v.Code+' · '+(vehicleStates[v.State]||[v.State])[0]+' · '+(cargo?formatOnboard({Mode:'Cargo',Load:v.Load}):v.Load+'/'+v.Capacity+' 人');
 const L=loopLayout(n,width);
 // Names share the row: a long line shows names on hover only.
 const spacing=n>1?(L.cx1-L.cx0)/Math.max(1,Math.ceil(n/2)-1):200;
 const compact=spacing<46;
 const nameWidth=Math.max(40,Math.min(110,spacing-6));
 const stopItem=(stop,i)=>{
  const pt=L.points[i];
  const serving=[...(stations.get(stop.Id)||new Map())];
  const ordered=[...serving.filter(([id])=>id===line.Id),...serving.filter(([id])=>id!==line.Id)];
  const colors=ordered.length?ordered.map(([,t])=>t.Color):[line.Color];
  const arcs=ringArcs(colors.length);
  const w=stop.Waiting,full=label(stop,i);
  const passing='经过：'+(ordered.map(([,t])=>t.Name).join('、')||line.Name)+(generic(stop.Name)&&stop.Name?'\n站台：'+stop.Name:'');
  const barH=w>0?Math.max(3,Math.round(w*26/max)):0;
  return [
   i===topIndex&&h('span',{key:'c'+i,className:'rll-loop-crowd','aria-hidden':true,style:{left:pt.x+'px',top:pt.y+'px'}}),
   marked&&marked.Stop===stop.Id&&h('span',{key:'f'+i,className:'rll-loop-focus','aria-hidden':true,style:{left:pt.x+'px',top:pt.y+'px'}}),
   h('span',{key:'d'+i,className:'rll-stop-dot',title:passing,style:{left:pt.x+'px',top:pt.y+'px'}},
    h('svg',{'aria-hidden':true,viewBox:'0 0 20 20'},...(arcs.length?arcs.map((d,k)=>h('path',{key:k,d,fill:'none',stroke:colors[k],strokeWidth:2.2,strokeLinecap:'butt'})):[h('circle',{key:0,cx:10,cy:10,r:8.6,fill:'none',stroke:colors[0],strokeWidth:2.2})])),
    h('span',null,i+1)),
   h('div',{key:'n'+i,className:'rll-loop-name',style:{left:pt.x+'px',top:(pt.row===0?pt.y-34:pt.y+15)+'px',maxWidth:nameWidth+'px'}},
    h('button',{type:'button',className:'rll-stop-link',title:'聚焦车站：'+(stop.Name||full),'aria-label':full,onClick:()=>focus(stop.Id),style:{maxWidth:nameWidth+'px'}},compact?'':full),turn(i)&&h('em',null,'折返')),
   !cargo&&barH>0&&h('span',{key:'w'+i,className:'rll-loop-wait',title:'候车 '+w+' 人',style:{left:pt.x+'px',top:(pt.row===0?pt.y+13:pt.y-13-barH)+'px',height:barH+'px'}}),
   !cargo&&w>0&&h('span',{key:'k'+i,className:'rll-loop-count',style:{left:pt.x+'px',top:(pt.row===0?pt.y+13+barH-11:pt.y-24-barH)+'px'}},String(w))];
 };
 // Vehicles on the track: at the stop while boarding, else at their real share of the way from the previous stop.
 // Stacking order is by vehicle id, so a hover (which re-sends the data) never reshuffles the marks.
 const placed=new Map();
 const capsule=v=>{
  if(v.At<0||v.At>=n)return null;
  const p=v.Boarding?v.At:v.At-1+(v.Progress??0.5);
  const key=p.toFixed(1),k=placed.get(key)||0;placed.set(key,k+1);
  const pt=L.at(p),pct=v.Capacity>0?Math.min(100,Math.round(v.Load*100/v.Capacity)):0;
  const out=(pt.y<(L.y0+L.y1)/2?-1:1)*(v.Boarding?11:0)+(pt.y<(L.y0+L.y1)/2?-1:1)*k*8;
  return h('button',{key:v.Id,type:'button',className:'rll-veh-mark','data-state':v.State,'data-focus':marked&&marked.Vehicle===v.Id?'1':'0',title:vehicleTitle(v),'aria-label':'聚焦车辆 '+v.Code,
   style:{left:pt.x+'px',top:(pt.y+out)+'px',width:'15rem',height:'9rem',borderColor:v.State==='Full'?'#f1be72':line.Color},
   onMouseEnter:()=>hoverVehicle(v.Id),onMouseLeave:()=>hoverVehicle(''),onClick:()=>focus(v.Id)},h('b',{style:{width:pct+'%',height:'100%',backgroundColor:line.Color}}));
 };
 const mid=(L.cx0+L.cx1)/2;
 const gaps=fleetGaps(fleet,n);
 const gapText=v=>{if(!gaps.has(v.Id))return '—';const g=Math.round(gaps.get(v.Id)*2)/2;return g===0?'同站':'隔 '+g+' 站';};
 return h('div',{className:'rll-diagram'},
  h('div',{className:'rll-diagram-head'},h('strong',null,cargo?n+' 个站点':'候车合计 '+total+' 人'),topIndex>=0&&h('span',null,'最多 · '+label(stops[topIndex],topIndex)+' '+stops[topIndex].Waiting+' 人')),
  ...issues.map(x=>h('div',{key:x.Kind,className:'rll-issue','data-level':String(x.Level)},h('b',null,x.Label),x.Detail)),
  (()=>{const v=marked&&marked.Vehicle?fleet.find(f=>f.Id===marked.Vehicle):null;
   return v&&h('div',{className:'rll-focusline'},'这辆车：'+(v.At>=0&&v.At<n?(v.Boarding?'停靠 ':'前往 ')+label(stops[v.At],v.At):'不在线路上')+' · '+loadText(v)+(cargo?'':' 人'));})(),
  n===0?h('p',null,'这条线路还没有站点'):h('div',{ref:box,className:'rll-loop',style:{height:L.height+'px'}},
   h('svg',{'aria-hidden':true,width:width,height:L.height,viewBox:'0 0 '+width+' '+L.height},
    h('path',{d:L.track,fill:'none',stroke:'#1d2126',strokeWidth:9}),
    h('path',{d:L.track,fill:'none',stroke:line.Color,strokeWidth:3.5,opacity:.9}),
    h('path',{d:'M'+(mid-4)+' '+(L.y0-5)+' l6 5 -6 5',fill:'none',stroke:'#ecf3f1',strokeWidth:1.6,strokeLinecap:'round'}),
    n>1&&h('path',{d:'M'+(mid+4)+' '+(L.y1-5)+' l-6 5 6 5',fill:'none',stroke:'#ecf3f1',strokeWidth:1.6,strokeLinecap:'round'})),
   ...stops.flatMap((s,i)=>stopItem(s,i)).filter(Boolean),
   ...[...fleet].sort((a,b)=>a.Id<b.Id?-1:a.Id>b.Id?1:0).map(capsule).filter(Boolean)),
  n>0&&h('div',{className:'rll-loop-note'},h('span',null,'环线运行，末站后返回第 1 站'),!cargo&&h('span',null,'竖条为候车人数'),compact&&h('span',null,'站名悬停可见'),topIndex>=0&&h('span',null,'金边为候车最多')),
  fleet.length>0&&h('button',{type:'button',className:'rll-fold rll-fleet-fold','aria-label':'运行车辆','aria-expanded':fleetOpen,onClick:()=>setFleetOpen(!fleetOpen)},'运行车辆 · '+fleet.length+' 辆',actionIcon(false,fleetOpen)),
  fleetOpen&&h('div',{className:'rll-fleet'},...fleet.map(v=>{
   const [name,tone]=vehicleStates[v.State]||[v.State,'#a9b0b9'];
   const pct=v.Capacity>0?Math.min(100,Math.round(v.Load*100/v.Capacity)):0;
   return h('button',{key:v.Id,type:'button',className:'rll-veh','aria-label':'聚焦车辆 '+v.Code,title:'点击聚焦并打开车辆面板',onMouseEnter:()=>hoverVehicle(v.Id),onMouseLeave:()=>hoverVehicle(''),onClick:()=>focus(v.Id)},
    h('span',{className:'rll-veh-code'},v.Code),
    h('span',{className:'rll-veh-state',style:{color:tone,borderColor:tone}},name),
    h('span',{className:'rll-veh-next'},v.At>=0&&v.At<n?(v.Boarding?'停靠 ':'前往 ')+label(stops[v.At],v.At):'—'),
    h('span',{className:'rll-veh-load'},h('i',null,h('b',{style:{width:pct+'%',backgroundColor:v.State==='Full'?'#f1be72':'#79deb7'}})),h('small',null,loadText(v))),
    h('span',{className:'rll-veh-gap'},gapText(v)));
  })));
}
function TicketControl({line,meta}){
 const min=meta.TicketMin||0,max=meta.TicketMax||0,step=meta.TicketStep||1;
 const [value,setValue]=React.useState(line.Ticket);
 React.useEffect(()=>{setValue(line.Ticket);},[line.Ticket]);
 const track=React.useRef(null);
 const commit=v=>{const c=Math.max(min,Math.min(max,Math.round(v/step)*step));if(c===value)return;setValue(c);trigger('RoadLaneLock','setTicketPrice',line.Id,c);};
 const fromMouse=e=>{const r=track.current?.getBoundingClientRect();if(!r||r.width<=0)return;commit(min+(max-min)*Math.max(0,Math.min(1,(e.clientX-r.left)/r.width)));};
 const pct=max>min?(value-min)*100/(max-min):0;
 return h('div',{className:'rll-set-sec'},
  h('div',{className:'rll-set-head'},h('span',null,'票价'),h('strong',null,value>0?'¢'+value:'免费')),
  h('div',{className:'rll-slider'},
   h('button',{type:'button',className:'rll-step','aria-label':'降低票价',disabled:value<=min,onClick:()=>commit(value-step)},icon24('M6 12H18',14)),
   h('div',{ref:track,className:'rll-slide',role:'slider','aria-label':'票价','aria-valuemin':min,'aria-valuemax':max,'aria-valuenow':value,
    onMouseDown:e=>{e.stopPropagation();fromMouse(e);},onMouseMove:e=>{if(e.buttons&1)fromMouse(e);}},
    h('i'),h('b',{style:{width:pct+'%'}}),h('span',{style:{left:pct+'%'}})),
   h('button',{type:'button',className:'rll-step','aria-label':'提高票价',disabled:value>=max,onClick:()=>commit(value+step)},icon24(['M6 12H18','M12 6V18'],14))),
  h('small',{className:'rll-note'},'与原版线路面板的票价同步；拉到最左为免费。'));
}
// One line's settings: everything the vanilla line panel has, in one popup.
// Fixed-width name; on hover it scrolls sideways to show the whole name, and returns on leave.
function Marquee({text,className}){
 const box=React.useRef(null),[shift,setShift]=React.useState(0);
 const enter=()=>{const b=box.current;if(!b)return;const d=b.scrollWidth-b.clientWidth;if(d>1)setShift(d);};
 return h('span',{ref:box,className,title:text,onMouseEnter:enter,onMouseLeave:()=>setShift(0)},
  h('span',{className:'rll-marq',style:{transform:'translateX('+(-shift)+'px)',transitionDuration:(shift?Math.max(1.2,shift/45):.35)+'s',transitionDelay:shift?'.25s':'0s'}},text));
}
function LineSettings({line,meta,lines,close}){
 React.useEffect(()=>{trigger('RoadLaneLock','lineModels',line.Id);return ()=>trigger('RoadLaneLock','lineModels','');},[line.Id]);
 return h('div',{className:'rll-settings'},
  h(LineName,{line}),
  line.Fleetsize&&h(FleetControl,{line}),
  h(ScheduleControl,{line}),
  h(ModelPicker,{line}),
  line.Ticket!=null&&meta.TicketReady&&h(TicketControl,{line,meta}),
  h(Palette,{color:toColor(line.Color),lineId:line.Id,line,lines}),
  h(SettingsFoot,{line,close}));
}
function LineName({line}){
 const [value,setValue]=React.useState(line.Name||'');
 React.useEffect(()=>{setValue(line.Name||'');},[line.Id,line.Name]);
 const clean=String(value??'').trim();
 const apply=()=>{if(clean&&clean!==line.Name)trigger('RoadLaneLock','renameLine',line.Id,clean);};
 return h('div',{className:'rll-set-sec'},
  h('div',{className:'rll-set-head'},h('span',null,'线路名称')),
  h('div',{className:'rll-inline',style:{marginTop:0}},
   h('input',{className:'rll-input',style:{flex:1,minWidth:0},value,maxLength:32,'aria-label':'线路名称',onChange:e=>setValue(e.target.value),onKeyDown:e=>{e.stopPropagation();if(e.key==='Enter')apply();}}),
   h('button',{type:'button',className:'rll-btn','data-primary':true,disabled:!clean||clean===line.Name,onClick:apply},'应用')));
}
function FleetControl({line}){
 const [target,min,max]=line.Fleetsize;
 const [value,setValue]=React.useState(target);
 React.useEffect(()=>{setValue(target);},[line.Id,target]);
 const track=React.useRef(null);
 const commit=v=>{const c=Math.max(min,Math.min(max,Math.round(v)));if(c===value)return;setValue(c);trigger('RoadLaneLock','setLineVehicles',line.Id,c);};
 const fromMouse=e=>{const r=track.current?.getBoundingClientRect();if(!r||r.width<=0)return;commit(min+(max-min)*Math.max(0,Math.min(1,(e.clientX-r.left)/r.width)));};
 const pct=max>min?(value-min)*100/(max-min):0;
 return h('div',{className:'rll-set-sec'},
  h('div',{className:'rll-set-head'},h('span',null,'车辆数量'),h('strong',null,value+' 辆')),
  h('div',{className:'rll-slider'},
   h('button',{type:'button',className:'rll-step','aria-label':'减少车辆',disabled:value<=min,onClick:()=>commit(value-1)},icon24('M6 12H18',14)),
   h('div',{ref:track,className:'rll-slide',role:'slider','aria-label':'车辆数量','aria-valuemin':min,'aria-valuemax':max,'aria-valuenow':value,
    onMouseDown:e=>{e.stopPropagation();fromMouse(e);},onMouseMove:e=>{if(e.buttons&1)fromMouse(e);}},
    h('i'),h('b',{style:{width:pct+'%'}}),h('span',{style:{left:pct+'%'}})),
   h('button',{type:'button',className:'rll-step','aria-label':'增加车辆',disabled:value>=max,onClick:()=>commit(value+1)},icon24(['M6 12H18','M12 6V18'],14))),
  h('small',{className:'rll-note'},'可调范围 '+min+'–'+max+' 辆，当前在跑 '+(line.Vehicles||0)+' 辆；与原版线路面板同步。'));
}
function ScheduleControl({line}){
 const options=[[2,'全天'],[0,'仅白天'],[1,'仅夜间']];
 return h('div',{className:'rll-set-sec'},
  h('div',{className:'rll-set-head'},h('span',null,'运营时段')),
  h('div',{className:'rll-seg',role:'group','aria-label':'运营时段'},...options.map(([v,label])=>h('button',{key:v,type:'button','aria-pressed':line.Schedule===v,onClick:()=>{if(line.Schedule!==v)trigger('RoadLaneLock','setLineSchedule',line.Id,v);}},label))));
}
const transitModels$=bindValue('RoadLaneLock','transitModels','{}');
function ModelPicker({line}){
 let m;try{m=JSON.parse(useValue(transitModels$)||'{}')||{};}catch{m={};}
 if(m.Line!==line.Id||!m.Supported||!(m.Available||[]).length)return null;
 const selected=new Set(m.Selected||[]),all=selected.size===0;
 return h('div',{className:'rll-set-sec'},
  h('div',{className:'rll-set-head'},h('span',null,'车型'),h('small',{className:'rll-current'},all?'全部车型（自动）':'已选 '+selected.size+' 种')),
  h('div',{className:'rll-models'},...m.Available.map(v=>h('button',{key:v.Id,type:'button',className:'rll-model','aria-pressed':selected.has(v.Id),title:v.Name,
   onClick:()=>trigger('RoadLaneLock','setLineModel',line.Id,v.Id,!selected.has(v.Id))},v.Thumb&&h('img',{src:v.Thumb,alt:''}),v.Name))),
  h('small',{className:'rll-note'},'不选任何车型时由游戏自动挑选。'));
}
function SettingsFoot({line,close}){
 const [armed,setArmed]=React.useState(false);
 React.useEffect(()=>{if(!armed)return;const t=setTimeout(()=>setArmed(false),3000);return ()=>clearTimeout(t);},[armed]);
 return h('div',{className:'rll-set-foot'},
  h('button',{type:'button',className:'rll-link',onClick:()=>trigger('RoadLaneLock','openTransitLine',line.Id)},'在原版面板中打开 ›'),
  h('button',{type:'button',className:'rll-del','data-armed':armed?'1':undefined,onClick:()=>{if(!armed){setArmed(true);return;}trigger('RoadLaneLock','deleteLine',line.Id);close();}},armed?'确认删除 · 不可撤销':'删除线路'));
}
function NamingFold({meta,lines,open,setOpen}){
 const prefix=meta.Prefix||{},auto=meta.Auto!==false,preview=meta.Preview||[];
 const kinds=[...new Set(lines.map(l=>l.Kind).filter(Boolean))].sort((a,b)=>Number(a.startsWith('Cargo'))-Number(b.startsWith('Cargo'))||(a<b?-1:a>b?1:0));
 const [draft,setDraft]=React.useState({});
 const save=next=>trigger('RoadLaneLock','namingSettings',JSON.stringify({Auto:auto,Prefix:prefix,...next}));
 const clean=v=>String(v||'').toUpperCase().replace(/[^A-Z]/g,'').slice(0,3);
 const commit=kind=>{const v=clean(draft[kind]);if(v&&v!==prefix[kind])save({Prefix:{...prefix,[kind]:v}});setDraft(d=>{const next={...d};delete next[kind];return next;});};
 return h('div',null,
  h('button',{className:'rll-fold','aria-label':'线路命名与编号','aria-expanded':open,onClick:()=>setOpen(!open)},'线路命名与编号',actionIcon(false,open)),
  open&&h('div',{className:'rll-reveal'},
   h('button',{type:'button',role:'switch','aria-checked':auto,'aria-label':'新建线路自动命名',className:'rll-switch-row',onClick:()=>save({Auto:!auto})},h('span',null,'新建线路自动命名'),trackOf(auto)),
   h('div',{className:'rll-sub'},'编号模板'),
   h('div',{className:'rll-naming-grid'},...kinds.map(k=>h('label',{key:k,className:'rll-prefix'},h('span',null,kindLabels[k]||k),
    h('input',{className:'rll-input',value:draft[k]??prefix[k]??'',maxLength:3,'aria-label':(kindLabels[k]||k)+'编号前缀',onChange:e=>setDraft(d=>({...d,[k]:e.target.value})),onBlur:()=>commit(k),onKeyDown:e=>{e.stopPropagation();if(e.key==='Enter')commit(k);}}),
    h('em',null,(clean(draft[k]??prefix[k])||'?')+'01')))),
   h('div',{className:'rll-sub'},preview.length?'将为 '+preview.length+' 条默认名线路命名':'没有仍用默认名的线路'),
   preview.length>0&&h('div',{className:'rll-preview'},...preview.slice(0,8).map(p=>h('div',{key:p.Id},h('span',null,p.From||'未命名线路'),h('b',null,'→'),h('strong',null,p.To))),preview.length>8&&h('small',null,'等 '+preview.length+' 条')),
   h('div',{className:'rll-inline'},h('button',{type:'button',className:'rll-btn','data-primary':'1',style:{marginLeft:0},disabled:!preview.length,onClick:()=>trigger('RoadLaneLock','applyNaming')},'批量命名')),
   h('small',{className:'rll-note'},'只改仍为游戏默认名的线路，手动起的名字不受影响；编号自动跳过已占用的号码。')));
}
// Load ring: same visual language as the shared-stop donuts.
export function ringPath(pct,r=9,c=12){
 const p=Math.max(0,Math.min(100,pct||0));
 if(p<=0)return null;
 const point=a=>{const t=(a-90)*Math.PI/180;return (c+r*Math.cos(t)).toFixed(2)+' '+(c+r*Math.sin(t)).toFixed(2);};
 if(p>=99.9)return 'M'+point(0)+' A'+r+' '+r+' 0 1 1 '+point(180)+' A'+r+' '+r+' 0 1 1 '+point(359.99);
 const end=360*p/100;
 return 'M'+point(0)+' A'+r+' '+r+' 0 '+(end>180?1:0)+' 1 '+point(end);
}
export function loadRing(pct){
 const d=ringPath(pct),p=Math.max(0,Math.min(100,pct||0));
 return h('svg',{'aria-hidden':true,viewBox:'0 0 24 24'},h('circle',{cx:12,cy:12,r:9,fill:'none',stroke:'#3d434a',strokeWidth:3}),
  ...(d?[h('path',{key:'v',d,fill:'none',stroke:p>=90?'#f1be72':'#79deb7',strokeWidth:3,strokeLinecap:'round'})]:[]));
}
// Citywide passengers this month (the game's own statistics): all modes on 总览, one mode on its tab.
function RidersBox({riders,mode}){
 if(!riders||mode==='Cargo')return null;
 const list=(riders.Modes||[]).filter(m=>m.Month>0||m.Last>0);
 const pick=mode==='*'?riders:list.find(m=>m.Mode===mode);
 if(!pick)return null;
 const month=pick.Month||0,last=pick.Last||0,change=last>0?Math.round((month-last)*100/last):null;
 return h(React.Fragment,null,
  h('div',{className:'rll-ridebox'},h('div',null,h('small',null,'本月运送'),h('strong',null,tr('{0} 人次',month.toLocaleString()))),
   last>0&&h('em',{'data-up':change>=0?'1':'0'},tr('上月 {0}',last.toLocaleString())+(change==null?'':' '+(change>=0?'↑':'↓')+Math.abs(change)+'%'))),
  mode==='*'&&list.length>0&&h('div',{className:'rll-ridechips'},...list.slice().sort((a,b)=>b.Month-a.Month).map(m=>h('span',{key:m.Mode},(labels[m.Mode]||m.Mode)+' '+(m.Month||0).toLocaleString()))));
}
function TransitPanel(){
 const lines=JSON.parse(useValue(transit$)||'[]');
 let meta;try{meta=JSON.parse(useValue(transitMeta$)||'{}')||{};}catch{meta={};}
 const [tab,setTab]=React.useState('*'),[expanded,setExpanded]=React.useState('');
 const [settings,setSettings]=React.useState(''),[naming,setNaming]=React.useState(false);
 const modes=[...new Set(lines.map(l=>l.Mode))].sort();
 const active=modes.includes(tab)?tab:'*';
 const [sort,setSort]=React.useState({key:null,ascending:false});
 const rows=sortTransitRows(lines.filter(l=>active==='*'||l.Mode===active),sort.key,sort.ascending);
 // A vehicle or stop selected in the game: switch to its tab, open its line and scroll to it.
 const focus=readState().TransitFocus||null;
 const handled=React.useRef(0);
 const focusLine=focus?(focus.Line?lines.find(l=>l.Id===focus.Line):focus.Stop?sortTransitRows(lines,sort.key,sort.ascending).find(l=>l.Stops.some(x=>x.Id===focus.Stop)):null):null;
 React.useEffect(()=>{
  if(!focus||!focus.Rev||handled.current===focus.Rev||!focusLine)return;
  handled.current=focus.Rev;setTab(focusLine.Mode);setExpanded(focusLine.Id);setSettings('');
  setTimeout(()=>{try{const el=document.querySelector('[data-line="'+focusLine.Id+'"]');if(el&&el.scrollIntoView)el.scrollIntoView({block:'center'});}catch{}},80);
 },[focus&&focus.Rev,focusLine&&focusLine.Id]);
 const sortHeader=(key,label)=>{
  const chosen=sort.key===key;
  return h('span',{key,role:'columnheader','aria-sort':chosen?(sort.ascending?'ascending':'descending'):'none'},
   h('button',{type:'button',className:'rll-sort',title:label+'：点击按'+(chosen&&!sort.ascending?'升序':'降序')+'排列','aria-label':label+'排序',
    onClick:()=>setSort(previous=>({key,ascending:previous.key===key&&!previous.ascending}))},
   label,chosen&&actionIcon(false,sort.ascending)));
 };
 const change=key=>trigger('RoadLaneLock','visibility',key);
 const highlight=id=>trigger('RoadLaneLock','highlightLine',id);
 React.useEffect(()=>()=>highlight(''),[active]);
 // The map overlay draws only the selected category.
 React.useEffect(()=>{trigger('RoadLaneLock','transitMode',active);},[active]);
 const stations=new Map();
 for(const line of lines)for(const stop of line.Stops){if(!stations.has(stop.Id))stations.set(stop.Id,new Map());stations.get(stop.Id).set(line.Id,{Name:line.Name,Color:line.Color});}
 const generic=genericStopNames(lines);
 const hide=rows.length>0&&rows.every(l=>l.Visible);
 const eye=(label,go,visible)=>h('button',{type:'button',className:'rll-eye',title:label,'aria-label':label,'aria-pressed':visible,onClick:go},h('svg',{width:18,height:18,viewBox:'0 0 24 24',fill:'none',stroke:'currentColor',strokeWidth:1.6},h('path',{d:'M2 12 Q12 -2 22 12 Q12 26 2 12 Z'}),h('circle',{cx:12,cy:12,r:3}),!visible&&h('path',{d:'M3 3L21 21'})));
 const gear=h('svg',{'aria-hidden':true,width:22,height:22,viewBox:'0 0 24 24',fill:'none',stroke:'currentColor',strokeWidth:1.8,strokeLinecap:'round'},h('path',{d:'M4 7H13M19 7H20M4 17H6M12 17H20'}),h('circle',{cx:16,cy:7,r:2.5}),h('circle',{cx:9,cy:17,r:2.5}));
 return h('section',null,h('style',null,transitStyles),h('div',{className:'rll-heading'},h('h2',null,'公共交通信息'),lines.length>0&&h('small',{className:'rll-count'},lines.length+' 条 · '+lines.reduce((n,l)=>n+(l.Vehicles||0),0)+' 辆')),
  h('div',{className:'rll-bar'},h('div',{role:'tablist',className:'rll-tabs'},h('i',{className:'rll-tab-indicator',style:{left:(3+(1+modes.indexOf(active))*52)+'rem'}}),...['*',...modes].map(k=>h('button',{key:k,role:'tab','aria-selected':k===active,onClick:()=>{highlight('');setTab(k);}},k==='*'?'总览':labels[k]||k))),eye((hide?'隐藏':'显示')+(active==='*'?'全部线路':'此类线路'),()=>change(active),hide)),
  h(RidersBox,{riders:meta.Riders,mode:active}),
  h('div',{className:'rll-tr rll-colnames'},h('span',null,'线路'),sortHeader('stops','站点'),sortHeader('onboard','在车'),sortHeader('vehicles','车辆'),sortHeader('riders','本月人次'),sortHeader('load','当前满载率'),h('span',null)),
  lines.length===0&&h('p',null,'当前城市暂无线网数据'),
  ...rows.map(l=>{
   const pct=l.Capacity>0?Math.round(l.Load*100/l.Capacity):null;
   const issues=(l.Issues||[]).slice().sort((a,b)=>b.Level-a.Level);
   const passes=focus&&focus.Stop&&focusLine&&l.Id!==focusLine.Id&&l.Stops.some(x=>x.Id===focus.Stop);
   return h('div',{key:l.Id,'data-line':l.Id,className:'rll-line','data-open':settings===l.Id||expanded===l.Id?'1':'0',onMouseEnter:()=>highlight(l.Id),onMouseLeave:()=>highlight('')},
    h('div',{className:'rll-tr',title:t('点击展开或收起路线图'),onClick:()=>{setSettings('');setExpanded(expanded===l.Id?'':l.Id);}},h('div',{className:'rll-name'},
     h('span',{className:'rll-badge','aria-hidden':true,style:{backgroundColor:l.Color,color:badgeInk(l.Color)}},l.Code||'·'),
     h(Marquee,{className:'rll-line-name',text:l.Name||t('未命名线路')}),
     passes&&h('span',{className:'rll-pill',title:'这条线路也经过所选站点'},'经过此站'),
     issues.length>0&&h('span',{className:'rll-flag','data-level':String(issues[0].Level),title:issues.map(x=>x.Label+'：'+x.Detail).join('\n')},issues[0].Label+(issues.length>1?'+'+(issues.length-1):'')),
     h('button',{type:'button',className:'rll-gear',title:l.Mode==='Cargo'?'线路配色':'票价与线路配色','aria-label':l.Name+'的线路设置','aria-expanded':settings===l.Id,onClick:e=>{e.stopPropagation();setExpanded('');setSettings(settings===l.Id?'':l.Id);}},gear)),
     h('span',null,h('span',{className:'rll-sort','aria-label':l.Name+'共 '+l.Stops.length+' 站','aria-expanded':expanded===l.Id},l.Stops.length,actionIcon(false,expanded===l.Id))),
     h('span',{title:l.Mode==='Cargo'?'当前载货':'当前在车乘客'},h('span',{className:'rll-onboard'},formatOnboard(l))),
     h('span',null,l.Vehicles),
     h('span',{className:'rll-riders',title:l.Riders==null?t('货运线路不统计'):t('本月 {0} 人次 · 上月 {1} 人次',(l.Riders||0).toLocaleString(),(l.RidersLast||0).toLocaleString())},l.Riders==null?'—':(l.Riders||0).toLocaleString()),
     h('div',{className:'rll-rate',title:pct==null?'暂无可用容量':l.Load+' / '+l.Capacity+(l.Mode==='Cargo'?' 千克':' 人')},h('span',null,pct==null?'—':pct+'%'),loadRing(pct)),
     eye((l.Visible?'隐藏':'显示')+l.Name,e=>{e.stopPropagation();change(l.Id);},l.Visible)),
    settings===l.Id&&h(LineSettings,{line:l,meta,lines,close:()=>setSettings('')}),
    expanded===l.Id&&h('div',{className:'rll-reveal'},h(RouteDiagram,{line:l,stations,generic,issues,focus})));
  }),
  lines.length>0&&h(NamingFold,{meta,lines,open:naming,setOpen:setNaming}));
}
const icon='coui://roadlanelock/RoadLaneLock.svg';

export function clampPosition(x,y,width,height,viewWidth,viewHeight){
 return {x:Math.max(12,Math.min(x,viewWidth-width-12)),y:Math.max(12,Math.min(y,viewHeight-height-12))};
}
function Launcher(){const s=readState();return h(Tooltip,{tooltip:tr('交通工具箱')},h(FloatingButton,{src:icon,onSelect:()=>trigger('RoadLaneLock',s.ToolActive?'cancelPick':'showVehicles')}));}
function roadGraphic(kind,path){
 if(path)return h('svg',{'aria-hidden':true,viewBox:'0 0 160 64',className:'rll-shape'},h('path',{d:path,fill:'none',stroke:'#555e68',strokeWidth:10,strokeLinecap:'round'}),h('path',{d:path,fill:'none',stroke:kind==='Monitor'?'#efb25e':'#7fb3ea',strokeWidth:2.5,strokeLinecap:'round'}));
 return h('img',{alt:'',className:'rll-art',src:'coui://roadlanelock/'+kind+'Preview.png'});
}
// Vector heroes for the street-name and parking pages (no font glyphs, so nothing renders as boxes).
function heroSvg(...children){return h('svg',{'aria-hidden':true,viewBox:'0 0 160 104',className:'rll-art',preserveAspectRatio:'xMidYMid slice'},h('rect',{width:160,height:104,fill:'#232b33'}),...children);}
// No parking: kerb painted with a solid yellow line, cars parked before the ban crossed out, the mod's sign.
const parkingArt=heroSvg(
 h('rect',{x:8,y:0,width:104,height:104,fill:'#3b4046'}),h('rect',{x:112,y:0,width:48,height:104,fill:'#555a60'}),
 h('path',{d:'M58 0V104M60.5 0V104',stroke:'#e8c45a',strokeWidth:1.2}),
 h('path',{d:'M84 0V104',stroke:'#cfd2d6',strokeWidth:1,strokeDasharray:'7 7',opacity:.5}),
 h('path',{d:'M110 0V104',stroke:'#f2c230',strokeWidth:3}),
 h('g',{opacity:.35},h('rect',{x:94,y:12,width:12,height:20,rx:3,fill:'#cfd2d6'}),h('rect',{x:94,y:62,width:12,height:20,rx:3,fill:'#cfd2d6'})),
 h('path',{d:'M96 18l8 8M104 18l-8 8M96 68l8 8M104 68l-8 8',stroke:'#ed5448',strokeWidth:1.8}),
 h('path',{d:'M134 38V78',stroke:'#9aa1a8',strokeWidth:2.2}),
 h('circle',{cx:134,cy:28,r:12,fill:'#f1f3ef'}),
 h('path',{d:'M130 35V21h5c6.4 0 6.4 8.8 0 8.8h-2V35z',fill:'#22272c'}),
 h('circle',{cx:134,cy:28,r:10.8,fill:'none',stroke:'#ed393c',strokeWidth:2.4}),
 h('path',{d:'M126.4 20.4L141.6 35.6',stroke:'#ed393c',strokeWidth:2.4}));
// Lane-change control: an approach with solid lines and the road text the mod paints (game language).
// Stretched to the box (no aspect lock) so the HTML text layer lines up with the lanes.
function laneArt(text){
 const lanes=[59.5,70,80.5];
 return h(React.Fragment,null,
  h('svg',{'aria-hidden':true,viewBox:'0 0 160 104',className:'rll-art',preserveAspectRatio:'none'},
   h('rect',{width:160,height:104,fill:'#232b33'}),h('rect',{x:0,y:0,width:30,height:104,fill:'#3b4046'}),h('rect',{x:30,y:18,width:130,height:68,fill:'#3b4046'}),
   h('path',{d:'M34 20V84',stroke:'#eef0ea',strokeWidth:1.8}),
   h('path',{d:'M37 51H160M37 53.2H160',stroke:'#e8c45a',strokeWidth:1.1}),
   h('path',{d:'M40 35H160',stroke:'#cfd2d6',strokeWidth:.9,strokeDasharray:'6 5',opacity:.55}),
   h('path',{d:'M40 64.7H120M40 75.3H120',stroke:'#f4f6f1',strokeWidth:1.5}),
   h('path',{d:'M120 64.7H160M120 75.3H160',stroke:'#cfd2d6',strokeWidth:.9,strokeDasharray:'6 5',opacity:.55}),
   h('path',{d:'M8 18V86M20 18V86',stroke:'#cfd2d6',strokeWidth:.8,strokeDasharray:'4 4',opacity:.3}),
   ...lanes.map((y,i)=>h('path',{key:i,d:'M60 '+y+'H46m4-3-4 3 4 3',stroke:'#f4f6f1',strokeWidth:1.3,fill:'none'}))),
  text&&h('div',{className:'rll-paint','aria-hidden':true},...lanes.map((y,i)=>h('span',{key:i,style:{top:(y/104*100)+'%',fontSize:(text.length>16?6.5:text.length>10?7.5:9)+'rem',letterSpacing:text.length>10?0:'1rem'}},text))));
}
// Jam relief: a head waiting to U-turn while oncoming cars block it; it gives up and goes straight on.
const jamArt=heroSvg(
 h('rect',{x:44,y:0,width:72,height:104,fill:'#3b4046'}),
 h('path',{d:'M45.5 0V104M114.5 0V104',stroke:'#eef0ea',strokeWidth:1.2,opacity:.65}),
 h('path',{d:'M79 0V28M79 62V104M81 0V28M81 62V104',stroke:'#e8c45a',strokeWidth:1.1}),
 h('path',{d:'M62 0V104M98 0V104',stroke:'#cfd2d6',strokeWidth:.9,strokeDasharray:'5 5',opacity:.5}),
 h('rect',{x:66,y:30,width:9,height:14,rx:2.5,fill:'#9aa1a8'}),h('rect',{x:66,y:47,width:9,height:14,rx:2.5,fill:'#9aa1a8'}),h('rect',{x:50,y:38,width:9,height:14,rx:2.5,fill:'#9aa1a8'}),
 h('rect',{x:85.5,y:56,width:9,height:14,rx:2.5,fill:'#cfd2d6',stroke:'#5ce6df',strokeWidth:1.5}),
 h('rect',{x:85.5,y:74,width:9,height:13,rx:2.5,fill:'#9aa1a8'}),h('rect',{x:85.5,y:90,width:9,height:13,rx:2.5,fill:'#9aa1a8'}),
 h('circle',{cx:90,cy:80.5,r:7.5,fill:'none',stroke:'#5ce6df',strokeWidth:.9}),h('circle',{cx:90,cy:96.5,r:7.5,fill:'none',stroke:'#5ce6df',strokeWidth:.9}),
 h('path',{d:'M90 56V44C90 32 70.5 32 70.5 44V58',stroke:'#d8dcda',strokeWidth:1.3,strokeDasharray:'3 3',fill:'none',opacity:.75}),
 h('path',{d:'M76.5 31l7 7M83.5 31l-7 7',stroke:'#ed5448',strokeWidth:2}),
 h('path',{d:'M90 56V5',stroke:'#5ce6df',strokeWidth:1.8,strokeDasharray:'5 3.5'}),h('path',{d:'M85 11l5-8 5 8',stroke:'#5ce6df',strokeWidth:1.8,fill:'none'}));
// Shared module card: display only. Left the picture, right the chosen road and what the module does;
// "选择另一条道路" lets go of the road (same as a right click on the map).
function ModuleHero({s,art,picking,pickTitle,desc,extra}){
 return h('div',{className:'rll-hero rll-modhero','data-picking':picking?'1':'0'},
  h('div',{className:'rll-artbox'},art),
  h('div',{className:'rll-hero-text'},
   picking?h('strong',null,s.HoverName||pickTitle):h(StreetName,{s}),
   !picking&&extra,
   h('small',{className:'rll-desc'},desc),
   picking&&h('small',{className:'rll-hint'},'鼠标悬停道路高亮，左键选中；选中后右键退出')),
  !picking&&h('button',{type:'button',className:'rll-cta',onClick:()=>trigger('RoadLaneLock','deselectRoad')},'选择另一条道路 ›'));
}
const laneDesc='路段内禁止变道；方向不对的车，驶出管制路段后重新寻路。执勤的救护、消防、警车不受限，市政车辆照常靠边作业。';
const rulesDesc='选一段路，设置变道、限速、双黄线、分道。';
// Home picture: one road carrying every rule's paint.
const overviewArt=()=>h('svg',{'aria-hidden':true,viewBox:'0 0 160 104',className:'rll-art',preserveAspectRatio:'none'},
 h('rect',{width:160,height:104,fill:'#232b33'}),h('rect',{x:0,y:18,width:160,height:68,fill:'#3b4046'}),
 h('path',{d:'M0 50.6H160M0 53.4H160',stroke:'#e8c45a',strokeWidth:1.3}),h('path',{d:'M0 35.5H160M0 68.5H160',stroke:'#cfd2d6',strokeWidth:.9,strokeDasharray:'6 5',opacity:.55}),
 h('path',{d:'M10 68.5H70',stroke:'#f4f6f1',strokeWidth:1.5}),h('path',{d:'M30 77H18m4-3-4 3 4 3',stroke:'#f4f6f1',strokeWidth:1.3,fill:'none'}),
 h('text',{x:118,y:31,fill:'#f4f6f1',fontSize:11,textAnchor:'middle',fontWeight:700},'50'),
 h('g',{transform:'translate(64 37) scale(.42)'},kindIcon(8,32,true)),h('g',{transform:'translate(108 70) scale(.42)'},kindIcon(32,32,true)));
const jamDesc='等掉头或转弯的头车堵住身后车流时，让它改为直行驶出，到下一条路再重新寻路。路口等红灯的时间不算。';
const parkDesc=auto=>auto?'开启后这段路边不能再停车；原本停着的车会被直接清走（和原版删除车辆一样）。':'开启后这段路边不能再停车；原本停着的车可自行离开。';
const statPill=(text,tone)=>h('span',{className:'rll-pill','data-tone':tone||''},text);
const fmt=n=>(n||0).toLocaleString();
function PickerHero({s,monitor}){
 const [intro,setIntro]=React.useState(()=>{try{return !window.localStorage.getItem('rll.monitor.intro');}catch{return true;}});
 React.useEffect(()=>{if(!monitor||!intro)return;
 const timer=setTimeout(()=>{setIntro(false);try{window.localStorage.setItem('rll.monitor.intro','1');}catch{}},4800);return ()=>clearTimeout(timer);
 },[monitor,intro]);
 return h('div',{className:'rll-hero'},h('div',{className:'rll-artbox'},roadGraphic(monitor?'Monitor':'Road',s.HoverShape),monitor&&intro&&!s.HoverShape&&h('svg',{className:'rll-trace',viewBox:'0 0 100 100',preserveAspectRatio:'none'},h('path',{pathLength:1,d:'M73 40 Q82 40 82 54 L82 83 Q82 91 72 91 L53 91'}))),
 h('div',{className:'rll-hero-text'},h('strong',null,s.HoverName||(monitor?'选择监测路段':'选择路段')),h('small',null,monitor?'点击道路选择，再分别开两个方向':'点击道路，分别设置两个方向'),monitor&&intro&&h('small',null,'头车堵住后车时让它直行驶出')));
}
function actionIcon(reselect,expanded=false){
 return h('svg',{'aria-hidden':true,width:15,height:15,viewBox:'0 0 24 24',fill:'none',stroke:'currentColor',strokeWidth:2,strokeLinecap:'round',strokeLinejoin:'round',style:{transform:expanded?'rotate(180deg)':'rotate(0deg)',transition:'transform 150ms ease'}},
  h('path',{d:reselect?'M20 10a8 8 0 1 0 0 5M20 4v6h-6':'M5 9l7 7 7-7'}));
}
// Docked placement: just below the top-left button row, bottom no lower than the game's bottom bar.
// Measured from the live layout (UI scale and resolution vary); falls back to rem estimates.
export function measureDock(){
 const vh=window.innerHeight||1080;
 let rem=1;try{rem=parseFloat(getComputedStyle(document.documentElement).fontSize)||1;}catch{}
 const vw=window.innerWidth||1920;
 let top=0,bottom=0;
 // The game's top-right button row (camera, settings…) is always there and never under this panel:
 // probe it and take the bottom of the button-sized elements around the hit point.
 try{
  if(document.elementFromPoint)for(const x of [vw-24,vw-70,vw-120]){
   for(let el=document.elementFromPoint(x,Math.round(vh*.025));el&&el!==document.body;el=el.parentElement){
    if(String(el.className||'').indexOf('rll-')>=0)break;
    const r=el.getBoundingClientRect();
    if(r.top>=0&&r.top<vh*.1&&r.height>vh*.015&&r.height<vh*.15)top=Math.max(top,r.bottom);
   }
  }
 }catch{}
 try{
  let el=document.elementFromPoint?document.elementFromPoint((window.innerWidth||1920)/2,vh-4):null;
  for(;el&&el!==document.body;el=el.parentElement){
   if(String(el.className||'').indexOf('rll-')>=0)break;
   const r=el.getBoundingClientRect();
   if(r.top>vh*.6&&r.width>(window.innerWidth||1920)*.25)bottom=r.top;
  }
 }catch{}
 // Fallback: the button row ends at about 5% of the screen height.
 if(!(top>vh*.02))top=Math.max(60*rem,vh*.055);
 if(!(bottom>top+200))bottom=vh-110*rem;
 return {top:Math.round(top+8),bottom:Math.round(bottom-8)};
}
// ---- 车道规则: one rule edited at a time; the switcher picks it and the map shows only its buttons. ----
const ruleTabNames=[['lane','变道','Lane'],['speed','限速','Speed'],['yellow','双黄线','Yellow'],['truck','分道','Truck']];
function RuleTabs({s}){
 const act=(s.LaneRules||{}).Active||{},tab=s.LaneTab||'lane';
 return h('div',{role:'tablist',className:'rll-ruletabs','aria-label':t('规则')},...ruleTabNames.map(([k,n,a])=>h('button',{key:k,type:'button',role:'tab','aria-selected':tab===k,'data-active':act[a]?'1':'0',
  title:act[a]?t('此路段已生效'):'',onClick:()=>trigger('RoadLaneLock','laneTab',k)},t(n))));
}
// 分道 vehicle kinds (bits as RoadRules.Kinds) and their side-view silhouettes, the same shapes as the road paint.
const laneKinds=[[1,'私家车'],[2,'出租车'],[4,'公交'],[8,'货车'],[16,'市政车'],[64,'摩托车'],[32,'自行车']];
const inkC='#e8ecf0',holeC='#1e2125';
const wheel=(x,y,k)=>h('circle',{key:k,cx:x,cy:y,r:2.6,fill:inkC,stroke:holeC,strokeWidth:1.3});
const kindShapes={
 1:()=>[h('path',{key:'b',d:'M6 19.5L8 15.3L12 12H19.3L22.7 15.3L26 16.3V20H6Z',fill:inkC}),h('path',{key:'w',d:'M12.6 13.3H15.6V15.6H11ZM17 13.3H19L21 15.6H17Z',fill:holeC}),wheel(10.7,20.4,'l'),wheel(21.3,20.4,'r')],
 2:()=>[...kindShapes[1](),h('rect',{key:'t',x:13.4,y:8.6,width:5.3,height:2.4,rx:.6,fill:'#f2c94c'})],
 4:()=>[h('rect',{key:'b',x:4.7,y:9.3,width:22.7,height:10.2,rx:1.2,fill:inkC}),h('path',{key:'w',d:'M6.4 11H9.7V14H6.4ZM11 11H14.3V14H11ZM15.7 11H19V14H15.7ZM20.3 11H23.6V14H20.3ZM24.7 11H26.3V16H24.7Z',fill:holeC}),wheel(9.3,20,'l'),wheel(22.7,20,'r')],
 8:()=>[h('rect',{key:'b',x:4,y:10,width:14.7,height:9.3,fill:inkC}),h('path',{key:'c',d:'M19.3 13.3H24.7L28 16.7V19.3H19.3Z',fill:inkC}),wheel(8.7,20,'l'),wheel(24,20,'r')],
 16:()=>[h('rect',{key:'b',x:4.7,y:12,width:15.3,height:7.3,fill:inkC}),h('path',{key:'c',d:'M20.7 14H24.7L27.3 16.7V19.3H20.7Z',fill:inkC}),h('rect',{key:'l',x:8.7,y:9.3,width:4.7,height:2,fill:'#f2994a'}),wheel(9.3,20,'l'),wheel(23.3,20,'r')],
 64:()=>[h('path',{key:'b',d:'M9 20.5L13.5 14.5H18.5L21.5 17.5H25.5L23.5 20.5Z',fill:inkC}),h('path',{key:'h',d:'M19.5 11.5L21.5 15.5M18.5 11.5H21',stroke:inkC,strokeWidth:1.8,strokeLinecap:'round'}),h('circle',{key:'l',cx:8.5,cy:20.5,r:3.6,fill:inkC,stroke:holeC,strokeWidth:1.3}),h('circle',{key:'r',cx:24,cy:20.5,r:3.6,fill:inkC,stroke:holeC,strokeWidth:1.3})],
 32:()=>[h('g',{key:'b',fill:'none',stroke:inkC,strokeWidth:1.8,strokeLinecap:'round',strokeLinejoin:'round'},h('circle',{cx:10,cy:20,r:4}),h('circle',{cx:22,cy:20,r:4}),h('path',{d:'M10 20L14 12H20L22 20M14 12L16 20H10M18 9H21'}))]
};
// A kind's silhouette; banned: inside a red ring with a slash, like a prohibition sign.
function kindIcon(kind,size,banned){
 return h('svg',{'aria-hidden':true,width:size,height:size,viewBox:'0 0 32 32',style:{flexShrink:0}},
  h('g',{key:'s',transform:'translate(16 16) scale(1.28) translate(-16 -15.5)'},...kindShapes[kind]()),
  banned&&h('circle',{key:'r',cx:16,cy:16,r:13.5,fill:'none',stroke:'#e5484d',strokeWidth:3}),
  banned&&h('path',{key:'x',d:'M6.5 6.5L25.5 25.5',stroke:'#e5484d',strokeWidth:3,strokeLinecap:'round'}));
}
// Small sign drawings for rules (HTML/SVG shapes only; Gameface SVG text is unreliable).
function ruleSign(kind,size,value){
 const slash=h('path',{key:'x',d:'M6 6L26 26',stroke:'#e5534b',strokeWidth:3.2,strokeLinecap:'round'});
 const ring=h('circle',{key:'r',cx:16,cy:16,r:13,fill:'none',stroke:'#e5534b',strokeWidth:3});
 if(kind==='speed')return h('span',{className:'rll-badge2','data-kind':'speed',style:{width:size+'rem',height:size+'rem'}},value||'');
 if(kind==='truck')return kindIcon(8,size,true);
 const art=kind==='yellow'?[h('path',{key:'a',d:'M13 5V27M19 5V27',stroke:'#f0be3c',strokeWidth:2.6})]
  :kind==='walk'?[h('circle',{key:'a',cx:16,cy:8,r:2.4}),h('path',{key:'b',d:'M16 11L14 18L17 21L16 27M14 18L11 26M15 13L19 16M15 13L11 16'})]:[];
 return h('svg',{'aria-hidden':true,width:size,height:size,viewBox:'0 0 32 32',fill:'none',stroke:'#e8ecf0',strokeWidth:1.8,strokeLinecap:'round',strokeLinejoin:'round',style:{flexShrink:0}},
  ...art,kind!=='yellow'&&ring,kind!=='yellow'&&slash);
}
function RuleSwitch({on,title,status,sign,disabled,onToggle,label}){
 return h('div',{className:'rll-dircaps'},h('button',{type:'button',className:'rll-dircap','data-on':on?'1':'0',role:'switch','aria-checked':on,'aria-label':label||title,disabled,onClick:e=>{e?.stopPropagation();if(!disabled)onToggle();}},
  h('span',{className:'rll-dircap-ic'},sign),
  h('span',{className:'rll-dircap-tx'},h('strong',null,title),status?h('small',null,status):null),
  h('span',{'aria-hidden':true,className:'rll-track','data-on':on?'1':'0'},h('span',null))));
}
// Speed limit slider: 20–120 km/h in steps of 10 plus the road's own limit, where it rests when no limit is
// set; moving it back onto the road's own limit clears ours.
function SpeedSlider({value,original,onSet}){
 const track=React.useRef(null);
 const stops=[...new Set([20,30,40,50,60,70,80,90,100,110,120].concat(original>0?[original]:[]))].sort((a,b)=>a-b);
 const home=original>0?stops.indexOf(original):0;
 const [drag,setDrag]=React.useState(null);
 const idx=drag==null?(value>0&&stops.includes(value)?stops.indexOf(value):home):drag;
 const last=stops.length-1;
 const send=i=>{const v=stops[i];const next=v===original?0:v;if(next!==value)onSet(next);};
 const at=x=>{const r=track.current?.getBoundingClientRect();if(!r||!r.width)return idx;return Math.max(0,Math.min(last,Math.round((x-r.left)/r.width*last)));};
 const start=e=>{
  e.stopPropagation();let index=at(e.clientX);setDrag(index);
  const move=m=>{index=at(m.clientX);setDrag(index);};
  const up=()=>{window.removeEventListener('mousemove',move);window.removeEventListener('mouseup',up);setDrag(null);send(index);};
  window.addEventListener('mousemove',move);window.addEventListener('mouseup',up);
 };
 const pos=i=>last>0?i/last*100:0;
 const v=stops[idx],isHome=v===original;
 const step=d=>send(Math.max(0,Math.min(last,idx+d)));
 const ticks=[0,last].concat(original>0?[home]:[]).filter((x,i,a)=>a.indexOf(x)===i);
 return h('div',{className:'rll-speed'},
  h('div',{className:'rll-speed-head'},h('span',null,t('限速')),h('strong',{'data-home':isHome?'1':'0'},v+' km/h'+(isHome?' · '+t('原版'):''))),
  h('div',{className:'rll-speed-row'},
   h('button',{type:'button',className:'rll-speed-step','aria-label':t('降低'),onClick:()=>step(-1)},'−'),
   h('div',{ref:track,className:'rll-speed-track',role:'slider','aria-label':t('限速'),'aria-valuemin':stops[0],'aria-valuemax':stops[last],'aria-valuenow':v,onMouseDown:start},
    h('span',{className:'rll-speed-rail'}),h('span',{className:'rll-speed-fill',style:{width:pos(idx)+'%'}}),
    original>0&&h('span',{className:'rll-speed-home',style:{left:pos(home)+'%'}}),
    h('span',{className:'rll-speed-knob',style:{left:pos(idx)+'%'}})),
   h('button',{type:'button',className:'rll-speed-step','aria-label':t('提高'),onClick:()=>step(1)},'+')),
  h('div',{className:'rll-speed-ticks'},...ticks.map(i=>h('span',{key:i,'data-home':i===home&&original>0?'1':'0',style:{left:pos(i)+'%'}},i===home&&original>0?t('原版 {0}',stops[i]):String(stops[i])))));
}
// G37: pick a start and an end junction; every road between them gets the same speed limit.
function RangePanel({s}){
 const g=s.SpeedRange||{};
 const step=!g.Start?1:!g.End?2:3;
 const send=v=>trigger('RoadLaneLock','setRangeSpeed',JSON.stringify({Session:s.Session,Value:v}));
 return h('div',null,
  h('div',{className:'rll-range'},
   h('div',{className:'rll-rangesteps'},
    h('span',{'data-on':step===1?'1':'0','data-done':g.Start?'1':'0'},'① '+(g.Start||t('点起点路口'))),
    h('span',{'data-on':step===2?'1':'0','data-done':g.End?'1':'0'},'② '+(g.End||t('点终点路口')))),
   step<3&&h('small',{className:'rll-hint'},g.Hover?t('悬停：{0}',g.Hover):t('鼠标悬停路口出现白圈，左键选中；右键退一步')),
   g.Missing&&h('p',{className:'rll-warnline'},t('这两个路口之间找不到相连的道路，换一个终点试试')),
   step===3&&g.Roads>0&&h('div',{className:'rll-rangeinfo'},
    h('strong',null,(g.Names||[]).join('、')+(g.MoreNames>0?t(' 等 {0} 条路',g.Names.length+g.MoreNames):'')),
    h('small',null,t('{0} 段 · {1} 米',g.Roads,(g.Length||0).toLocaleString())+(g.Mixed?' · '+t('现在限速不一致'):''))),
   step===3&&g.Roads>0&&h(SpeedSlider,{value:g.Mixed?0:(g.Speed||0),original:g.Default,onSet:send}),
   step===3&&g.Roads>0&&h('small',{className:'rll-note'},t('左右拖动即应用到这 {0} 段；拖回原版即取消限速。',g.Roads))),
  h('div',{className:'rll-rangebtns'},
   h('button',{type:'button',onClick:()=>trigger('RoadLaneLock','rangeReset')},t('重选')),
   h('button',{type:'button',onClick:()=>trigger('RoadLaneLock','speedRange',false)},t('返回选单段'))));
}
function RuleBody({s}){
 const lr=s.LaneRules||{},tab=s.LaneTab||'lane';
 const send=(name,extra)=>trigger('RoadLaneLock',name,JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version,...extra}));
 if(tab==='speed')return h('div',null,
  h(SpeedSlider,{value:lr.Speed||0,original:lr.DefaultSpeed,onSet:v=>send('setSpeed',{Value:v})}),
  h('small',{className:'rll-note'},lr.Speed?t('路面涂限速数字；车辆按这个速度行驶，寻路也按新速度算。拖回原版即取消。'):t('滑块停在这条路的原版限速；左右拖动即生效。')),
  h('button',{type:'button',className:'rll-link rll-rangego',onClick:()=>trigger('RoadLaneLock','speedRange',true)},t('按路口选一段改限速 ›')));
 if(tab==='yellow')return h('div',null,
  h(RuleSwitch,{on:!!lr.DoubleYellow,disabled:!lr.TwoWay&&!lr.DoubleYellow,title:t('双黄线'),status:!lr.TwoWay?t('单行道没有对向车道'):lr.DoubleYellow?t('不掉头 · 不跨线进出 · 禁行人横穿'):t('未设置'),sign:ruleSign('yellow',26),onToggle:()=>send('setDoubleYellow',{Enable:!lr.DoubleYellow})}),
  h('small',{className:'rll-note'},t('路中画双黄实线：车辆不能跨对向车道左转进出路边，也不能借路边出入口掉头；路段两端的普通接头禁止行人横穿。')));
 if(tab==='truck'){
  if(!(lr.Bans||[]).length)return h('p',{className:'rll-empty'},t('此处没有可设置的机动车道。'));
  return h('div',null,
   lr.Notice&&h('p',{key:lr.Notice,className:'rll-warnline rll-shake'},t(lr.Notice)),
   ...(lr.Bans||[]).filter(g=>g.Whole&1).map(g=>h('p',{key:'w'+g.Direction,className:'rll-wholeline'},t('{0}整段禁止私家车：车辆规划路线时绕开；货车、摩托车同时禁行',g.Toward?t('驶向{0}',g.Toward):t('此方向')))),
   h('div',{className:'rll-kindlegend'},...laneKinds.map(([k,n])=>h('span',{key:k},kindIcon(k,18,false),t(n)))),
   h('small',{className:'rll-note'},t('点地图上每条车道的车型图标即可禁止或恢复；被禁的车改走别的车道，路面涂对应标志。私家车、自行车可以整段禁止（原版寻路绕开）；其他车型每个方向至少留一条车道。')));
 }
 return null;
}
// Map: the rule being edited gets a button at the middle of the road (lane change keeps its own pins,
// 分道 a row of vehicle buttons on every lane); other active rules sit above it as read-only badges.
// 流速走势: live flow per 5 game minutes and jam count, with the moments rules were switched.
function TrendChart({hist}){
 const [range,setRange]=React.useState(6);
 const pts=((hist&&hist.Points)||[]).filter(p=>p.H>=-range);
 const marks=((hist&&hist.Markers)||[]).filter(m=>m.H>=-range);
 const W=300,H=84,x=hh=>((hh+range)/range)*W,y=f=>H-(Math.max(0,Math.min(100,f))/100)*H;
 const maxJ=Math.max(1,...pts.map(p=>p.J||0));
 const line=pts.map(p=>x(p.H).toFixed(1)+','+y(p.F).toFixed(1)).join(' ');
 const jams=pts.map(p=>x(p.H).toFixed(1)+','+(H-(p.J||0)/maxJ*H*.6).toFixed(1)).join(' ');
 return h('div',{className:'rll-trendbox'},
  h('div',{className:'rll-listhead'},h('span',null,t('流速走势')),h('span',{className:'rll-range'},
   ...[6,24].map(n=>h('button',{key:n,type:'button','aria-pressed':range===n,'data-on':range===n?'1':'0',onClick:()=>setRange(n)},t('{0} 小时',n))))),
  pts.length<2?h('p',{className:'rll-empty'},t('记录中：每 5 个游戏分钟记一个点，本局内有效。')):
  h('div',{className:'rll-trendchart'},
   h('svg',{'aria-hidden':true,viewBox:'0 0 '+W+' '+H,preserveAspectRatio:'none'},
    ...[0,.5,1].map(g=>h('line',{key:'g'+g,x1:0,x2:W,y1:H*g,y2:H*g,stroke:'#3a4047',strokeWidth:.6})),
    ...marks.map((m,i)=>h('line',{key:'m'+i,x1:x(m.H),x2:x(m.H),y1:0,y2:H,stroke:'#f1be72',strokeWidth:.8,strokeDasharray:'3 2'})),
    h('polyline',{points:jams,fill:'none',stroke:'#fa7d70',strokeWidth:1,strokeDasharray:'3 2'}),
    h('polyline',{points:line,fill:'none',stroke:'#79deb7',strokeWidth:1.8})),
   ...marks.map((m,i)=>h('span',{key:'t'+i,className:'rll-trendmark',title:m.Text,style:{left:(x(m.H)/W*100)+'%',top:(i%3)*13+'rem'}},m.Text))),
  h('div',{className:'rll-trendaxis'},h('span',null,'-'+range+'h'),h('span',null,'-'+(range/2)+'h'),h('span',null,t('现在'))),
  h('div',{className:'rll-trendlegend'},h('span',{style:{color:'#79deb7'}},'— '+t('平均流速')),h('span',{style:{color:'#fa7d70'}},'- - '+t('堵点数')),h('span',{style:{color:'#f1be72'}},'¦ '+t('规则开关'))));
}

// 谁在用这条路: vehicles seen at the place in the last 10 game minutes.
function UsageBlock({u,id,colors}){
 return h('div',{className:'rll-usage',onClick:e=>e.stopPropagation()},
  h('small',null,t('近 10 分钟 · {0} 辆',u.Total)),
  h('div',{className:'rll-usage-cols'},
   h('div',null,...(u.Kinds||[]).map(k=>h('div',{key:k.Name,className:'rll-usage-row'},h('span',null,k.Name),
    h('i',null,h('b',{style:{width:k.Pct+'%',backgroundColor:colors[k.I]||'#6f7780'}})),h('em',null,k.Pct+'%')))),
   h('div',null,
    h('div',{className:'rll-usage-split'},h('b',{style:{width:(u.Far||0)+'%',backgroundColor:'#f1a45f'}}),h('b',{style:{width:(u.Near||0)+'%',backgroundColor:'#79deb7'}})),
    h('div',{className:'rll-usage-row'},h('span',{style:{color:'#f1a45f'}},'■ '+t('只是路过')),h('em',null,(u.Far||0)+'%')),
    h('div',{className:'rll-usage-row'},h('span',{style:{color:'#79deb7'}},'■ '+t('去这一带')),h('em',null,(u.Near||0)+'%')),
    u.FarTrucks>0&&h('small',null,t('货车里路过的 {0}%',u.FarTrucks)))),
  u.Suggest&&h('div',{className:'rll-usage-tip'},h('span',null,u.Suggest),
   u.CanApply&&h('span',{role:'button',tabIndex:0,className:'rll-link',onClick:e=>{e.stopPropagation();trigger('RoadLaneLock','applyUsage',id);}},t('去设置 ›'))));
}

function RulePins({s}){
 const [,redraw]=React.useState(0);RulePins.redraw=redraw;
 const pose=JSON.parse(useValue(pose$)||'{}');
 const valid=pose.Session===s.Session&&pose.Index===s.Index&&pose.Version===s.Version&&pose.Mid;
 const lr=s.LaneRules;
 if(!valid||!lr||!s.Valid||s.Picking)return null;
 const tab=s.LaneTab||'lane',act=lr.Active||{};
 const x=pose.Mid.X*window.innerWidth,y=pose.Mid.Y*window.innerHeight;
 const send=(name,extra)=>trigger('RoadLaneLock',name,JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version,...extra}));
 const stop=e=>e.stopPropagation();
 const badgeOf={lane:()=>roadIcon('lane',true,0,18,'#e8ecf0'),speed:()=>lr.Speed,yellow:()=>ruleSign('yellow',20),truck:()=>kindIcon(8,20,true)};
 const active=ruleTabNames.filter(([k,,a])=>k!==tab&&act[a]);
 const badges=active.length>0&&tab!=='truck'&&h('div',{className:'rll-badges',style:{left:x+'px',top:(y-(tab==='lane'?18:26))+'px'}},
  ...active.map(([k,n])=>h('button',{key:k,type:'button',className:'rll-badge2','data-kind':k,title:t(n)+' · '+t('点击切换到此规则'),'aria-label':t(n),onMouseDown:stop,onClick:e=>{stop(e);trigger('RoadLaneLock','laneTab',k);}},badgeOf[k]())));
 if(tab==='truck'){
  const masks={};for(const g of lr.Bans||[])masks[g.Direction]=g.Lanes;
  const lanes=pose.Lanes||[],W=window.innerWidth,H=window.innerHeight;
  // Each kind has its own spot on its lane (C#: along the lane centre line). Spots too close on
  // screen to hold a button each: one pill that zooms in.
  const spots=[];
  for(const p of lanes)(p.K||[]).forEach((q,k)=>{if(q&&laneKinds[k])spots.push({p,q,k:laneKinds[k][0],n:laneKinds[k][1],x:q.X*W,y:q.Y*H});});
  let gap=Infinity;
  for(let i=0;i<spots.length;i++)for(let j=i+1;j<spots.length;j++)gap=Math.min(gap,Math.hypot(spots[i].x-spots[j].x,spots[i].y-spots[j].y));
  if(spots.length>1&&gap<28)return h(React.Fragment,null,h('button',{type:'button',className:'rll-rulepin','data-on':act.Truck?'1':'0','aria-label':t('拉近设置分道 ›'),style:{left:x+'px',top:y+'px'},onMouseDown:stop,
   onClick:e=>{stop(e);trigger('RoadLaneLock','zoomSelectedRoad',JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version}));}},kindIcon(1,20,false),h('span',null,t('拉近设置分道 ›'))));
  const tip=RulePins.tip;
  const size=Math.max(22,Math.min(32,gap-4));
  return h(React.Fragment,null,...spots.map(({p,k,n,x:sx,y:sy})=>{
   const off=(((masks[p.D]||[])[p.I]||0)&k)!==0,label=t(n)+' · '+(off?t('已禁止 · 点击恢复'):t('可通行 · 点击禁止'));
   return h('button',{key:p.D+'-'+p.I+'-'+k,type:'button',className:'rll-kindbtn rll-kindspot','data-banned':off?'1':'0','aria-pressed':off,'aria-label':t(n)+' · '+(off?t('已禁止'):t('可通行')),
    style:{left:sx+'px',top:sy+'px',width:size+'px',height:size+'px',marginLeft:(-size/2)+'px',marginTop:(-size/2)+'px'},onMouseDown:stop,
    onMouseEnter:()=>{RulePins.tip={text:label,x:sx,y:sy-size/2};RulePins.redraw?.(v=>v+1);},
    onMouseLeave:()=>{RulePins.tip=null;RulePins.redraw?.(v=>v+1);},
    onClick:e=>{stop(e);send('setLaneBan',{Direction:p.D,Lane:p.I,Value:k,Enable:!off});}},kindIcon(k,Math.round(size*.75),off));
  }),tip&&h('div',{className:'rll-tip',style:{left:tip.x+'px',top:(tip.y-6)+'px'}},tip.text),
  lr.Notice&&h('div',{key:lr.Notice,className:'rll-mapnotice rll-shake',style:{left:x+'px',top:(y-40)+'px'}},t(lr.Notice)));
 }
 let pin=null;
 if(tab==='yellow'&&lr.TwoWay)pin={on:!!lr.DoubleYellow,sign:ruleSign('yellow',22),text:lr.DoubleYellow?t('双黄线'):t('未设双黄线'),go:()=>send('setDoubleYellow',{Enable:!lr.DoubleYellow})};
 if(tab==='speed')pin={on:lr.Speed>0,sign:ruleSign('speed',24,lr.Speed||lr.DefaultSpeed),text:lr.Speed?t('限速 {0}',lr.Speed):t('原版限速'),go:null};
 return h(React.Fragment,null,badges,pin&&h('button',{type:'button',className:'rll-rulepin','data-on':pin.on?'1':'0',role:pin.go?'switch':undefined,'aria-checked':pin.go?pin.on:undefined,'aria-label':pin.text,
  style:{left:x+'px',top:y+'px',cursor:pin.go?'pointer':'default'},onMouseDown:stop,onClick:e=>{stop(e);pin.go?.();}},pin.sign,h('span',null,pin.text)));
}
// Card picture of each rule: the same road as the lane-change card, showing that rule's paint.
function ruleArt(tab,lr){
 const road=[h('rect',{key:'g',width:160,height:104,fill:'#232b33'}),h('rect',{key:'r',x:0,y:18,width:160,height:68,fill:'#3b4046'}),
  h('path',{key:'e',d:'M0 20H160M0 84H160',stroke:'#eef0ea',strokeWidth:1.4,opacity:.7}),
  h('path',{key:'d',d:'M0 35.5H160M0 68.5H160',stroke:'#cfd2d6',strokeWidth:.9,strokeDasharray:'6 5',opacity:.55})];
 const centre=tab==='yellow'?[h('path',{key:'y',d:'M0 50.6H160M0 53.4H160',stroke:'#e8c45a',strokeWidth:1.3})]:[h('path',{key:'y',d:'M0 52H160',stroke:'#e8c45a',strokeWidth:1.3})];
 const mark=(x,y,node,k,flip)=>h('g',{key:k,transform:'translate('+x+' '+y+') scale(.42)'+(flip?' rotate(180 16 16)':'')},...node);
 let marks=[];
 if(tab==='speed'){const v=String(lr.Speed||lr.DefaultSpeed||'');marks=[0,1,2,3].map(i=>h('text',{key:i,x:80,y:[30,46,63,79][i],fill:'#f4f6f1',fontSize:11,textAnchor:'middle',fontWeight:700},v));}
 if(tab==='yellow'){
  const sign=(kind)=>kind==='uturn'?[h('circle',{key:'r',cx:16,cy:16,r:13,fill:'none',stroke:'#f4f6f1',strokeWidth:3}),h('path',{key:'u',d:'M19 25V13C19 7 11 7 11 13V18M7 15L11 20L15 15',fill:'none',stroke:'#f4f6f1',strokeWidth:2.6}),h('path',{key:'x',d:'M6 6L26 26',stroke:'#f4f6f1',strokeWidth:3})]
   :[h('circle',{key:'r',cx:16,cy:16,r:13,fill:'none',stroke:'#f4f6f1',strokeWidth:3}),h('circle',{key:'h',cx:16,cy:8,r:2.4,fill:'#f4f6f1'}),h('path',{key:'b',d:'M16 11L14 18L17 21L16 27M14 18L11 26M15 13L19 16M15 13L11 16',fill:'none',stroke:'#f4f6f1',strokeWidth:2}),h('path',{key:'x',d:'M6 6L26 26',stroke:'#f4f6f1',strokeWidth:3})];
  marks=[mark(60,21,sign('ped'),'a',true),mark(78,21,sign('uturn'),'b',true),mark(60,70,sign('ped'),'c'),mark(78,70,sign('uturn'),'d')];
 }
 if(tab==='truck')marks=[h('g',{key:'a',transform:'translate(70 22) scale(.42)'},kindIcon(8,32,true)),h('g',{key:'b',transform:'translate(70 70) scale(.42)'},kindIcon(16,32,true)),h('g',{key:'c',transform:'translate(88 70) scale(.42)'},kindIcon(32,32,true))];
 return h('svg',{'aria-hidden':true,viewBox:'0 0 160 104',className:'rll-art',preserveAspectRatio:'none'},...road,...centre,...marks);
}
const ruleDesc={
 speed:'车辆在这段路按设定速度行驶，寻路也按新速度计算；路面每条车道涂限速数字。',
 yellow:'路中画双黄实线：不能跨线左转进出路边、不能借出入口掉头，行人不能横穿；每条车道涂禁止行人和禁止掉头标志。',
 truck:'每条车道可单独禁止私家车、出租车、公交、货车、市政车或自行车，被禁的车改走其他车道。执勤的救护、消防、警车不受限。'
};
// Status pills of the card, for the rule on the switcher.
function rulePills(tab,lr,s){
 if(tab==='speed')return [h('span',{key:'a',className:'rll-pill','data-tone':lr.Speed?'mint':''},lr.Speed?t('限速 {0}',lr.Speed):t('未设限速'))];
 if(tab==='yellow')return [h('span',{key:'a',className:'rll-pill','data-tone':lr.DoubleYellow?'mint':''},lr.DoubleYellow?t('双黄线'):t('未设双黄线'))];
 if(tab==='truck'){const whole=(lr.Bans||[]).some(g=>g.Whole&1);return [h('span',{key:'a',className:'rll-pill','data-tone':whole?'amber':lr.BannedLanes?'mint':''},whole?t('整段禁私家车'):lr.BannedLanes?t('{0} 条车道有限制',lr.BannedLanes):t('未设分道'))];}
 return [h('span',{key:'a',className:'rll-pill','data-tone':s.Requested?'mint':''},s.Requested?t('规则生效'):t('未设规则'))];
}
// ---- 路口规则 ----
// Junction picture drawn for its number of roads: 2 = a straight road with a crossing, 3 = a T, 4+ = a cross.
function junctionArt(roads){
 const n=roads||4,red='#e5534b',ink='#eef0ea';
 const arms=n<=2?[h('rect',{key:'v',x:64,y:0,width:32,height:104,fill:'#3b4046'})]
  :n===3?[h('rect',{key:'v',x:64,y:0,width:32,height:104,fill:'#3b4046'}),h('rect',{key:'h',x:96,y:36,width:64,height:32,fill:'#3b4046'})]
  :[h('rect',{key:'h',x:0,y:36,width:160,height:32,fill:'#3b4046'}),h('rect',{key:'v',x:64,y:0,width:32,height:104,fill:'#3b4046'})];
 const zebra=n<=2?'M68 46H92M68 58H92':n===3?'M102 40V64M68 30H92M68 74H92':'M58 40V64M102 40V64M68 30H92M68 74H92';
 return h('svg',{'aria-hidden':true,viewBox:'0 0 160 104',className:'rll-art',preserveAspectRatio:'none'},
  h('rect',{width:160,height:104,fill:'#232b33'}),...arms,
  h('path',{d:zebra,stroke:ink,strokeWidth:3,strokeDasharray:'2 2.4',opacity:.5}),
  h('circle',{cx:80,cy:52,r:15,fill:'none',stroke:red,strokeWidth:3.2}),h('path',{d:'M70 42L90 62',stroke:red,strokeWidth:3.2}),
  h('circle',{cx:80,cy:46,r:2.2,fill:ink}),h('path',{d:'M80 49L78 55L81 58M78 55L76 61M80 51L84 54',stroke:ink,strokeWidth:1.6,fill:'none'}));
}function JunctionPage({s,shellView,heading,pageFold}){
 const j=s.Junction||{},list=j.List||[];
 const target=x=>JSON.stringify({Session:s.Session,Index:x.Index,Version:x.Version});
 const picking=!j.Selected;
 const card=h('div',{className:'rll-hero rll-modhero','data-picking':picking?'1':'0'},
  h('div',{className:'rll-artbox'},junctionArt(j.Roads)),
  h('div',{className:'rll-hero-text'},
   h('strong',null,picking?(j.HoverName||t('选择路口')):j.Name),
   !picking&&h('div',{className:'rll-pills'},statPill(t('{0} 条路',j.Roads)),statPill(t('{0} 处过街',j.Crossings)),statPill(j.Closed||j.Auto?t('禁止行人过街'):t('可过街'),j.Closed||j.Auto?'mint':'')),
   h('small',{className:'rll-desc'},t('关掉这个路口的人行横道（包括没画斑马线的横穿），行人改走天桥或别的路口。')),
   picking&&h('small',{className:'rll-hint'},t('鼠标悬停路口高亮，左键选中；选中后右键退出'))),
  !picking&&h('button',{type:'button',className:'rll-cta',onClick:()=>trigger('RoadLaneLock','deselectRoad')},t('选择另一个路口 ›')));
 return shellView(t('路口规则'),heading(t('路口规则'),h('small',{className:'rll-count'},t('已设 {0} 处',list.length))),card,
  !picking&&h(RuleSwitch,{on:!!j.Closed,title:t('禁止行人过街'),status:j.Closed?t('人行横道已关闭 · 路面已涂标志'):j.Auto?t('双黄线已自动关闭'):t('行人可过街'),sign:ruleSign('walk',26),
   onToggle:()=>trigger('RoadLaneLock','junctionRule',JSON.stringify({Session:s.Session,Index:j.Index,Version:j.Version,Enable:!j.Closed}))}),
  !picking&&j.Warn&&h('p',{className:'rll-note',style:{color:'#f1be72'}},t('相邻路口也没有可走的过街点：两侧的人可能要绕很远，部分建筑可能走不到。')),
  list.length>0?h('div',null,h('div',{className:'rll-listhead'},h('span',null,t('已禁止过街的路口')),h('small',null,t('点击定位'))),
   ...list.map(x=>h('div',{key:x.Index+'/'+x.Version,className:'rll-rulecard',...rowGo(()=>trigger('RoadLaneLock','focusJunction',target(x)))},
    h('button',{type:'button',className:'rll-rulename',title:t('定位到 {0}',x.Name),onClick:own(()=>trigger('RoadLaneLock','focusJunction',target(x)))},x.Name),
    h('button',{type:'button',className:'rll-iconbtn','aria-label':t('移除 {0}',x.Name),title:t('恢复过街'),onClick:own(()=>trigger('RoadLaneLock','removeJunction',target(x)))},trashIcon)))):h('p',{className:'rll-empty'},t('还没有禁止过街的路口。点击地图上的路口开始。')),
  pageFold(h('p',null,t('原版行人在路口（包括没画斑马线的接头）横穿只是多算一笔代价，抄近路省得多就会横穿。这里直接把横道设为不可通行，省多少路都不会走。')),
   h('p',null,t('交通路线视图里，这个路口不再出现横穿的绿色行人线就是生效了。')),
   h('p',null,t('规则随城市存档保存；存档时临时还原，卸载本模组后存档里不留改动。'))));
}
function Panel(){
 const projected=JSON.parse(useValue(pose$)||'{}');
 const s=readState();
 const [position,setPosition]=React.useState(null);
 const [dock,setDock]=React.useState(()=>({top:68,bottom:(window.innerHeight||1080)-118}));
 // Every opening starts docked again (a drag only lasts until the panel closes).
 React.useEffect(()=>{if(!s.ToolActive){setPosition(null);return;}
  if(typeof document==='undefined')return;
  const measure=()=>setDock(prev=>{const next=measureDock();return next.top===prev.top&&next.bottom===prev.bottom?prev:next;});
  measure();const timer=setInterval(measure,1000);return ()=>clearInterval(timer);
 },[s.ToolActive]);
 const [details,setDetails]=React.useState(false);
 const [usageOpen,setUsageOpen]=React.useState(null);
 const [focus,setFocus]=React.useState(null);
 const roadKey=[s.Session,s.Index,s.Version,s.SelectionRevision].join('/');
 const selected=focus?.road===roadKey?focus.direction:0;
 const panel=React.useRef(null),drag=React.useRef(null);
 const endDrag=()=>{drag.current=null;};
 const close=()=>trigger('RoadLaneLock','cancelPick');
 const fit=(x,y)=>{const r=panel.current?.getBoundingClientRect();return clampPosition(x,y,r?.width||320,Math.max(r?.height||320,200),window.innerWidth,window.innerHeight);};
 React.useLayoutEffect(()=>{
  endDrag();
  setDetails(false);
 },[s.ToolActive,s.ViewingVehicles,s.TransitOpen,s.MonitorOpen,s.ParkingOpen,s.Picking,s.Session,s.SelectionRevision]);
 React.useEffect(()=>{
  const move=e=>{
   if(!drag.current)return;
   setPosition(fit(e.clientX-drag.current.x,e.clientY-drag.current.y));
  };
  const resize=()=>setPosition(p=>p?fit(p.x,p.y):null);
  const key=e=>{if(e.key==='Escape'&&s.ToolActive)close();};
  window.addEventListener('mousemove',move);window.addEventListener('mouseup',endDrag,true);window.addEventListener('blur',endDrag);window.addEventListener('resize',resize);window.addEventListener('keydown',key);
  return ()=>{window.removeEventListener('mousemove',move);window.removeEventListener('mouseup',endDrag,true);window.removeEventListener('blur',endDrag);window.removeEventListener('resize',resize);window.removeEventListener('keydown',key);drag.current=null;};
 },[s.ToolActive]);
 const vehicleId=s.VehicleId&&h('strong',{'aria-label':'车辆 ID',style:{fontFamily:'var(--fontFamily)',userSelect:'text'}},'车辆 ID '+s.VehicleId);
 if(!s.ToolActive)return null;
 const request=(direction,enable)=>trigger('RoadLaneLock','requestRule',JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version,Direction:direction,Enable:enable}));
 const stop=e=>e.stopPropagation();
 const row={display:'flex',alignItems:'center'};
 const split={...row,justifyContent:'space-between'};
 const small={fontSize:'12rem',color:palette.secondary,lineHeight:'1.5',margin:'5rem 0'};
 const shell={position:'fixed',display:'flex',flexDirection:'column',width:'580rem',maxWidth:'calc(100vw - 24px)',maxHeight:'calc(100vh - 24px)',overflow:'hidden',boxSizing:'border-box',padding:'12rem 12rem 12rem 10rem',background:'rgb(22,28,30)',color:palette.ink,border:'1rem solid #333e4a',borderRadius:'16rem',fontFamily:'var(--fontFamily)',fontSize:'14rem',pointerEvents:'auto',boxShadow:'0 7rem 22rem rgba(0,0,0,0.42)'};
 const action={...split,width:'100%',boxSizing:'border-box',padding:'9rem 8rem',marginTop:'4rem',border:0,borderRadius:'10rem',background:'#343a40',color:'#eff3f8',fontFamily:'var(--fontFamily)',fontSize:'13rem',cursor:'pointer',textAlign:'left'};
 const btn=(label,go,end,props={})=>h('button',{type:'button','aria-label':label,onClick:go,...props,style:{...action,...props.style}},h('span',{style:{fontFamily:'var(--fontFamily)'}},label),h('span',{'aria-hidden':true,style:{marginLeft:'8rem',color:'inherit'}},end));
 const title=(name,dragHandle)=>h('div',{onMouseDown:dragHandle,style:{...split,flexShrink:0,cursor:dragHandle?'move':'default',marginBottom:'10rem'}},
  h('div',{style:row},h('div',{style:{...row,justifyContent:'center',width:'32rem',height:'32rem',background:'#393d41',borderRadius:'8rem',marginRight:'9rem'}},h('img',{src:icon,alt:'',style:{width:'24rem',height:'24rem'}})),h('strong',{style:{fontSize:'16rem'}},name)),
  h('button',{'aria-label':s.Picking?'取消选择':'关闭',onMouseDown:stop,onClick:close,style:{...action,width:'26rem',height:'26rem',justifyContent:'center',padding:0,margin:0,fontSize:'18rem'}},'X'));
 const dragTitle=e=>{if(e.button!==0)return;const rect=panel.current?.getBoundingClientRect()||{left:position?.x||0,top:position?.y||0};drag.current={x:e.clientX-rect.left,y:e.clientY-rect.top};e.preventDefault();stop(e);};
 const dialogProps={className:'rll-shell',ref:panel,role:'dialog',onMouseDown:stop,onMouseUp:e=>{endDrag();stop(e);},onClick:stop,style:{...shell,...(position?{left:position.x+'px',top:position.y+'px',maxHeight:'calc(100vh - '+(position.y+12)+'px)'}:
  // Docked left; while the game's own info panel is open on the left, docked at the right edge instead
  // (clear of the small mod icon column there).
  {...(s.NativePanel?{right:'48rem'}:{left:'10px'}),top:dock.top+'px',maxHeight:Math.max(240,dock.bottom-dock.top)+'px'})}};
 const railButton=(name,asset,active,event)=>h(Tooltip,{key:event,tooltip:name},
  h('button',{type:'button',className:'rll-module',title:name,'aria-label':name,'aria-pressed':!!active,
   onClick:()=>{if(!active)trigger('RoadLaneLock',event);},style:{fontFamily:'var(--fontFamily)'}},
   h('img',{alt:'',src:'coui://roadlanelock/'+asset+'.svg',style:{width:'25rem',height:'25rem'}})));

 const legend=h('div',{className:'rll-legend'},...['变道管制（白实线）','拥堵疏导（绿箭头）','当前选择'].map((name,i)=>h('span',{key:name},h('b',{style:{color:['#ffffff','#7fb3ea','#d8dcda'][i]}},'━ '),name)));
 const fold=(name,...contents)=>h('div',null,h('button',{className:'rll-fold','aria-label':name,'aria-expanded':details,onClick:()=>setDetails(v=>!v)},name,actionIcon(false,details)),details&&h('div',{className:'rll-reveal'},legend,...contents,vehicleId));
 const heading=(name,aside)=>h('div',{className:'rll-heading'},h('h2',null,name),aside);
 // Rail order: everyday modules on top, the developer-only recorder pinned to the bottom.
 const modules=[[t('交通信息'),'Recorder',s.ViewingVehicles,'showVehicles'],[t('车道规则'),'LaneChange',!s.TransitOpen&&!s.ViewingVehicles&&!s.MonitorOpen&&!s.ParkingOpen&&!s.JunctionOpen,'pick'],[t('路口规则'),'Junction',s.JunctionOpen,'showJunction'],[t('拥堵疏导'),'Detour',s.MonitorOpen,'showMonitor'],[t('路边停车'),'Parking',s.ParkingOpen,'showParking'],[t('公共交通信息'),'Transit',s.TransitOpen,'showTransit']];
 const activeModule=Math.max(0,modules.findIndex(m=>m[2]));
 // The selected module's tile joins the main panel; inverted corners hide the seam. Every tile is
 // laid out top-down (38rem tall, 4rem apart), so the tongue follows the same 42rem step.
 const tongue=h('span',{'aria-hidden':true,className:'rll-tongue','data-edge':activeModule===0?'top':'',style:activeModule===0?{top:0,height:'42rem'}:{top:(4+activeModule*42)+'rem'}},h('i',null,h('b',null)),h('i',null,h('b',null)));
 const shellView=(label,...body)=>h('div',{...dialogProps,'aria-label':label},title('交通工具箱',dragTitle),
  h('div',{style:{display:'flex',minHeight:0,flex:1}},h('nav',{'aria-label':'功能模块',className:'rll-rail'},
   h('style',null,railStyles+panelStyles),tongue,...modules.map(m=>railButton(...m))),
   h('div',{className:'rll-main',style:{borderTopLeftRadius:activeModule===0?0:'12rem'}},...body)));
 const switchTrack=on=>h('span',{'aria-hidden':true,className:'rll-track','data-on':on?'1':'0'},h('span',null));
 const reselect=action=>btn('重选',()=>trigger('RoadLaneLock',action),actionIcon(true),{style:{width:'auto',background:'transparent'}});
 const pageFold=(...contents)=>h('div',null,h('button',{className:'rll-fold','aria-label':'图例与说明','aria-expanded':details,onClick:()=>setDetails(v=>!v)},'图例与说明',actionIcon(false,details)),details&&h('div',{className:'rll-reveal'},...contents));
 const pageHero=(art,name,hint)=>h('div',{className:'rll-hero'},h('div',{className:'rll-artbox'},art),h('div',{className:'rll-hero-text'},h('strong',null,name),h('small',null,hint)));
 if(s.ParkingOpen){
  const on=!!s.ParkingOn;
  const toggle=()=>trigger('RoadLaneLock','parkingRule',JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version,Enable:!on}));
  const label=on?'恢复路边停车':'禁止路边停车';
  const symbol=h('svg',{'aria-hidden':true,viewBox:'0 0 64 64',style:{width:'40rem',height:'40rem',flexShrink:0}},
   h('circle',{cx:32,cy:32,r:29,fill:'#f1f3ef'}),h('path',{d:'M23 49V15h13c17 0 17 23 0 23h-5v11z M31 23v7h5c6 0 6-7 0-7z',fill:'#22272c',fillRule:'evenodd'}),
   on&&h('circle',{cx:32,cy:32,r:27,fill:'none',stroke:'#ed393c',strokeWidth:6}),on&&h('path',{d:'M12 12L52 52',stroke:'#ed393c',strokeWidth:6}));
  const matches=projected.Session===s.Session&&projected.Index===s.Index&&projected.Version===s.Version;
  const point=matches?projected.Parking:null;
  const pin=s.Valid&&!s.Picking&&point&&h('button',{'aria-label':'路面停车开关',title:label,role:'switch','aria-checked':on,disabled:!s.ParkingReady,onClick:e=>{stop(e);toggle();},onMouseDown:stop,onMouseUp:stop,className:'rll-direction',style:{position:'fixed',left:point.X*window.innerWidth+'px',top:point.Y*window.innerHeight+'px',transform:'translate(-50%,-50%)',pointerEvents:'auto',padding:'5px',border:'2px solid #5393db',borderRadius:'10px',background:'#22272c'}},symbol);
  const ps=s.ParkStats||{};
  return h(React.Fragment,null,pin,shellView('禁止停车',heading('禁止停车',h('div',{className:'rll-pills'},ps.Total>0&&statPill('已清走 '+fmt(ps.Total)+' 辆'),h('small',{className:'rll-count'},'已设 '+(s.ParkingCount||0)+' 段'))),
   h(ModuleHero,{s,art:parkingArt,picking:s.Picking,pickTitle:'选择路段',desc:parkDesc(ps.Auto!==false),
    extra:h('div',{className:'rll-pills'},statPill(on?'禁停中':'未禁停',on?'mint':''),ps.Road>0&&statPill('已清走 '+fmt(ps.Road)+' 辆'))}),
   !s.Picking&&h(React.Fragment,null,
    s.Valid&&btn(label,toggle,symbol,{role:'switch','aria-checked':on,disabled:!s.ParkingReady,className:'rll-card','data-on':on?'1':'0',style:{padding:'12rem',border:'1rem solid '+(on?'#5393db':'#525a63'),background:on?'#303b47':'#292f35'}}),
    h('p',{role:'status',style:small},s.ParkingStatus||'选择一个路段，设置路边停车。')),
   pageFold(h('p',null,'地图：黄实线为已禁停路段的路缘，红圈斜杠为禁停标记。'),h('p',null,'禁停路段随城市存档保存，读档后自动恢复。关闭面板不撤销禁停；卸载本 mod 后存档里不留禁停标记。'),h('p',null,ps.Auto!==false?'仅控制路边泊位。开启禁停时清走已停车辆（可在模组设置里关闭），读档后也会对已禁停路段清一次；数字从本次进游戏算起，打开本页时刷新。':'仅控制路边泊位。已停车辆不搬移（可在模组设置里打开清车）；恢复后沿用原版资格。'),h('p',null,'道路拆分、重建后请重新选定。'),s.DiagPath&&h('p',null,'诊断文件：'+s.DiagPath),s.ParkingCount>0&&btn('恢复全部本次停车规则',()=>trigger('RoadLaneLock','clearParking',s.Session),'↺'))));
 }
 if(s.MonitorOpen){
  const target=r=>JSON.stringify({Session:s.Session,Index:r.Index,Version:r.Version});
  const preview=direction=>trigger('RoadLaneLock','previewMonitor',JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version,Direction:direction}));
  const toggleSelected=c=>trigger('RoadLaneLock','monitorRule',JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version,Direction:c.Direction,Enable:!(s.MonitorMask&c.Direction)}));
  const selected=((s.Valid&&!s.Picking?s.Controls:[])||[]).filter(Boolean);
  const headingOf=d=>(d===1?s.Heading1:s.Heading2)||(d===1?'A 向':'B 向');
  const js=s.JamStats||{};
  const card=h(ModuleHero,{s,art:jamArt,picking:s.Picking,pickTitle:'选择监测路段',desc:jamDesc,extra:h(React.Fragment,null,
    selected.length>0&&h(JamCapsules,{s,selected,headingOf,toggle:toggleSelected,preview}),
    js.Road>0&&h('div',{className:'rll-pills'},statPill('改直行 '+fmt(js.Road)+' 次'+(js.RoadFollowers>0?' · 解锁后车 '+fmt(js.RoadFollowers)+' 辆':''))),
    !s.Valid&&h('small',null,'原路段已失效，请重新选择。'))});
  const roads=s.MonitorRoads||[];
  const list=roads.length?h('div',null,h('div',{className:'rll-listhead'},h('span',null,'已监测路段'),h('small',null,'点击定位')),
   ...roads.map(r=>{
    const name=r.Name||'未命名道路';
    return h('div',{key:r.Index+'/'+r.Version,className:'rll-rulecard',...rowGo(()=>trigger('RoadLaneLock','focusMonitorRoad',target(r)))},
     nameCell(r,h('button',{type:'button',className:'rll-rulename',title:'定位到 '+name,onClick:own(()=>trigger('RoadLaneLock','focusMonitorRoad',target(r)))},name)),
     r.Jams>0&&h('small',{className:'rll-rowstat',title:'头车改直行次数（本次进游戏起）'},r.Jams+' 次'),
     ...[1,2].filter(d=>r.Directions&d).map(d=>{
      const on=!!(r.Mask&d),label=r['Heading'+d]||(d===1?'A 向':'B 向');
      return h('button',{key:d,type:'button',className:'rll-pill rll-pillbtn','data-tone':on?'mint':'',role:'switch','aria-checked':on,'aria-label':name+' '+label+(on?' 监测中':' 未监测'),title:on?'监测中，点击关闭':'未监测，点击开启',
       onClick:own(()=>trigger('RoadLaneLock','monitorRoadDirection',JSON.stringify({Session:s.Session,Index:r.Index,Version:r.Version,Direction:d,Enable:!on})))},label);
     }),
     h('button',{type:'button',className:'rll-iconbtn','aria-label':'移除 '+name,title:'移除此路段',onClick:own(()=>trigger('RoadLaneLock','removeMonitorRoad',target(r)))},trashIcon));
   })):h('p',{className:'rll-empty'},'还没有监测路段。点击地图上的道路开始。');
  const events=s.JamEvents||[];
  const recent=h('div',null,h('div',{className:'rll-listhead'},h('span',null,'最近处理'),events.length>0&&h('small',null,'点卡片查看车辆')),
   ...(events.length?events.map((e,i)=>h('button',{key:i+'/'+e.Id,type:'button',className:'rll-jamcard','data-live':e.Live?'1':'0','aria-label':'查看车辆 '+e.Id,onClick:()=>trigger('RoadLaneLock','focusJamVehicle',e.Id)},
    h('span',{className:'rll-jamtop'},h('strong',null,e.Road+' · '+e.Dir),e.Live&&h('i',{className:'rll-jamdot','aria-hidden':true}),h('span',{className:'rll-pill','data-tone':'mint'},'车辆 '+e.Id)),
    h('small',null,e.Reason+' · '+e.Trigger+' · '+e.Ago),
    h('span',{className:'rll-pills'},h('span',{className:'rll-pill','data-tone':'mint'},'直行驶出'),e.Followers>0&&h('span',{className:'rll-pill'},'后车解锁 '+e.Followers)))):[h('p',{key:'none',className:'rll-empty'},'监测路段暂无疏导记录。')]));
  return h(React.Fragment,null,s.Valid&&!s.Picking&&h(MonitorPins,{s,toggle:toggleSelected,preview}),shellView('拥堵疏导',
   heading('拥堵疏导',h('div',{className:'rll-pills'},s.JamReady===false&&h('span',{className:'rll-pill','data-tone':'amber',title:'在模组设置里打开「新执行器（测试）」'},'需开启新执行器'),js.Total>0&&statPill('改直行 '+fmt(js.Total)+' 次 · 解锁 '+fmt(js.Followers)+' 辆'),h('small',{className:'rll-count'},'已监测 '+(s.MonitorCount||0)+' 段'))),
   card,list,recent,
   fold('图例与说明',
    h('p',null,'头车停满 4 秒（在路口等红灯的时间不算，绿灯后才开始计）、本车道前方空着，并且身后倒灌到上一个路口、排队超过车道容量六成或停满 15 秒，就放弃转弯直行驶出，到下一条路再重新寻路。'),
    h('p',null,'排在它身后的车解除车道锁定，按原版方式变道绕开。'),
    h('p',null,'地图：淡绿箭头为监测路段（变红＝头车卡住还没走，亮绿＝刚放行），薄荷圈为被疏导的头车，红叉为放弃的转弯，薄荷虚线为直行驶出路线。'),
    h('p',null,'统计从本次进游戏算起，打开本页时刷新。'),
    h('p',null,'车辆交还原版后，若新路线在路段中间违规掉头，会先直行过下个路口再寻路，最多 3 次。'),
    s.MonitorMessage&&h('p',null,s.MonitorMessage))));
 }
 if(s.ViewingVehicles){
  const t=s.Traffic;
  const ring=(fraction,tone)=>h('svg',{'aria-hidden':true,viewBox:'0 0 36 36'},h('circle',{cx:18,cy:18,r:14,fill:'none',stroke:'#3f464e',strokeWidth:4}),
   h('circle',{cx:18,cy:18,r:14,fill:'none',stroke:tone,strokeWidth:4,strokeDasharray:(Math.max(0,Math.min(1,fraction))*88).toFixed(1)+' 88',transform:'rotate(-90 18 18)'}));
  const tile=(label,value,fraction,tone,amber,art,sub)=>h('div',{key:label,className:'rll-tile','data-tone':amber?'amber':''},art===null?null:art||ring(fraction,tone),h('div',null,h('small',null,label),h('strong',null,value),sub&&h('em',null,sub)));
  // Gauges: arcs on a 36-unit ring (circumference 88).
  // The game's UI ignores negative stroke-dashoffset, so each arc is turned to its own start instead.
  const arc=(from,len,color,key)=>h('circle',{key,cx:18,cy:18,r:14,fill:'none',stroke:color,strokeWidth:4,strokeDasharray:Math.max(0,len*88).toFixed(1)+' 88',transform:'rotate('+(-90+from*360).toFixed(1)+' 18 18)'});
  const gauge=(...parts)=>h('svg',{'aria-hidden':true,viewBox:'0 0 36 36'},h('circle',{cx:18,cy:18,r:14,fill:'none',stroke:'#3f464e',strokeWidth:4}),...parts);
  const flowColor=f=>f>=70?'#79deb7':f>=45?'#f1d06a':f>=25?'#f1a45f':'#fa7d70';
  const kindColors=['#5393db','#5dcaa5','#c9b48a','#9aa8b5','#f09595','#c49be0','#8fd36a','#6f7780'];
  const body=!t?[h('p',{key:'wait',className:'rll-empty'},'正在统计全城交通……')]:(()=>{
   const total=Math.max(1,t.Moving||0);
   const kinds=(t.Kinds||[]).map((k,i)=>({...k,color:kindColors[i]||'#6f7780',pct:Math.round((k.Count||0)*100/total)}));
   const spots=t.Spots||[];
   const flowTone=f=>f<25?'#fa9e90':f<40?'#f1be72':'#79deb7';
   const hover=id=>trigger('RoadLaneLock','hoverTraffic',id);
   return [
    h('div',{key:'tiles',className:'rll-tiles'},
     (()=>{const all=Math.max(1,t.LiveCars||0),stop=Math.min(all,t.Standing||0),go=all-stop,goPct=Math.round(go*100/all);
      // No ring here: the game's UI would not draw the coloured segments (G35); the mix bar below shows it.
      return tile('在路上',(t.Moving||0).toLocaleString(),0,'',false,null,tr('行驶 {0}% · 停着 {1}%',goPct,100-goPct));})(),
     (()=>{const f=t.Flow??100,ago=t.FlowHourAgo??-1,a=Math.max(0,Math.min(1,f/100))*360;
      return tile('平均流速',f+'%',0,'',false,gauge(arc(0,f/100,flowColor(f),'f'),h('line',{key:'n',x1:18,y1:2,x2:18,y2:8,stroke:'#e8ecf0',strokeWidth:1.6,transform:'rotate('+a.toFixed(0)+' 18 18)'})),
       ago<0?tr('实时车速 ÷ 限速'):tr('1 小时前 {0}% {1}',ago,f>ago+2?'↑':f<ago-2?'↓':'→'));})(),
     (()=>{const m=t.Managed||0,red=t.ManagedRed||0,b=t.Bottlenecks||0;
      const dots=(t.Radar||[]).slice(0,12),more=Math.max(0,(t.Radar||[]).length-dots.length);
      const radar=h('svg',{'aria-hidden':true,viewBox:'0 0 36 36',className:'rll-radar'},
       h('circle',{cx:18,cy:18,r:16.5,fill:'#17283d',stroke:'#5393db',strokeWidth:.8}),
       h('circle',{cx:18,cy:18,r:11,fill:'none',stroke:'rgba(83,147,219,.35)',strokeWidth:.6}),
       h('circle',{cx:18,cy:18,r:5.5,fill:'none',stroke:'rgba(83,147,219,.35)',strokeWidth:.6}),
       h('path',{d:'M18 1.5V34.5M1.5 18H34.5',stroke:'rgba(83,147,219,.2)',strokeWidth:.5}),
       h('g',{className:'rll-sweep'},h('path',{d:'M18 18L18 1.5A16.5 16.5 0 0 1 29.7 6.3Z',fill:'rgba(83,147,219,.25)'})),
       ...dots.map((d,i)=>h('circle',{key:'d'+i,cx:(18+Math.max(-1,Math.min(1,d.X))*15).toFixed(1),cy:(18+Math.max(-1,Math.min(1,d.Y))*15).toFixed(1),r:1.9,fill:'#ff6b6b'})),
       h('circle',{cx:18,cy:18,r:1,fill:'#5393db'}),
       more>0&&h('text',{x:34,y:34,textAnchor:'end',fontSize:6,fill:'#ffb3b3'},'+'+more));
      return tile('堵点',b+' 处',0,'',b>0||red>0,radar,m>0?tr('疏导路段 {0} 段 · {1} 段红',m,red):tr('原版堵点图标'));})()),
    h('div',{key:'mix',className:'rll-mix','aria-label':'车辆构成'},...kinds.filter(k=>k.Count>0).map(k=>h('span',{key:k.Name,style:{width:(k.Count*100/total)+'%',backgroundColor:k.color}}))),
    h('div',{key:'mixl',className:'rll-mix-labels'},...kinds.filter(k=>k.Count>0).map(k=>h('span',{key:k.Name},h('i',{style:{backgroundColor:k.color}}),k.Name+' '+k.pct+'%'))),
    h('div',{key:'head',className:'rll-listhead'},h('span',null,'最堵的地方'),spots.length>0&&h('small',null,'点一下飞过去')),
    ...(spots.length?spots.map(x=>h('button',{key:x.Id,type:'button',className:'rll-spot','aria-label':'定位 '+x.Name,title:'点击移动镜头到这里',
      onMouseEnter:()=>hover(x.Id),onMouseLeave:()=>hover(''),onFocus:()=>hover(x.Id),onBlur:()=>hover(''),onClick:()=>trigger('RoadLaneLock','focusTraffic',x.Id)},
     h('span',{className:'rll-spot-top'},h('strong',null,x.Name),x.Bottleneck&&h('span',{className:'rll-pill','data-tone':'red'},'原版堵点'),x.Monitored&&h('span',{className:'rll-pill','data-tone':'mint'},'监测中'),!x.Bottleneck&&h('span',{className:'rll-pill'},'缓行')),
     h('span',{className:'rll-spot-flow'},h('i',null,h('b',{style:{width:x.Flow+'%',backgroundColor:flowTone(x.Flow)}})),h('small',null,'流速 '+x.Flow+'%'+(x.Queue>0?' · 排队 '+x.Queue+' 辆'+(x.QueueParts?'（'+x.QueueParts+'）':''):''))),
     x.Cause&&h('span',{className:'rll-cause'},h('small',null,tr('原因：{0}',x.Cause)+' · '+tr('建议：{0}',x.Suggest)),
      h('span',{role:'button',tabIndex:0,className:'rll-link','aria-label':tr('去设置 {0}',x.Name),onClick:e=>{e.stopPropagation();trigger('RoadLaneLock','applyCause',x.Id);}},tr('去设置 ›'))),
     x.Usage&&h('span',{role:'button',tabIndex:0,className:'rll-link rll-usage-toggle','aria-expanded':usageOpen===x.Id,onClick:e=>{e.stopPropagation();setUsageOpen(v=>v===x.Id?null:x.Id);}},usageOpen===x.Id?tr('收起'):tr('谁在用这条路'),' ',h('span',{className:'rll-caret',style:{transform:usageOpen===x.Id?'rotate(-90deg)':'rotate(90deg)'}},'›')),
     usageOpen===x.Id&&x.Usage&&h(UsageBlock,{u:x.Usage,id:x.Id,colors:kindColors}))):
     [h('p',{key:'none',className:'rll-empty'},'全城通畅，暂无拥堵。')]),
    (t.Effects||[]).length>0&&h('div',{key:'effh',className:'rll-listhead'},h('span',null,tr('规则效果')),h('small',null,tr('开关规则两分钟后开始对比'))),
    ...(t.Effects||[]).map((e,i)=>h('div',{key:'eff'+i,className:'rll-effect'},
     h('strong',null,e.Name+' · '+e.Rule+' '+(e.On?tr('开启'):tr('关掉'))),
     h('small',null,e.Ready?tr('之后 流速 {0}%→{1}%，排队 {2}→{3} 辆',e.FlowBefore,e.FlowAfter,e.QueueBefore,e.QueueAfter):tr('观察中 · 开关时 流速 {0}%，排队 {1} 辆',e.FlowBefore,e.QueueBefore)))),
    h('button',{key:'fold',className:'rll-fold','aria-label':'车辆明细与趋势','aria-expanded':details,onClick:()=>setDetails(v=>!v)},'车辆明细与趋势',actionIcon(false,details)),
    details&&h('div',{key:'more',className:'rll-reveal'},
     ...kinds.map(k=>h('div',{key:k.Name,className:'rll-kindrow'},h('span',null,k.Name),h('span',null,(k.Count||0)+' 辆'))),
     h('div',{className:'rll-kindrow'},h('span',null,'停着的车'),h('span',null,(t.Parked||0)+' 辆')),
     h(TrendChart,{hist:t.History}),
     h('p',null,'本局拥堵疏导 '+(t.Jams||0)+' 次'),vehicleId)];
  })();
  return shellView('交通信息',heading('交通信息',h('small',{className:'rll-count'},'实时 · 每 5 秒')),...body);
 }
 if(s.TransitOpen)return shellView('公共交通信息',h(TransitPanel,{key:s.Session}));
 if(s.JunctionOpen)return h(JunctionPage,{s,shellView,heading,pageFold});
 const idPill=s.VehicleId&&h('span',{className:'rll-pill','data-tone':'mint',title:'车辆 ID（与诊断日志一致）',style:{userSelect:'text'}},'车辆 '+s.VehicleId);
 const laneState=s.ExecutorReady===false?h('span',{className:'rll-pill','data-tone':'amber'},'执行器不可用'):h('span',{className:'rll-pill','data-tone':'mint'},s.Enabled?'执行中':'待命');
 if(s.Picking&&s.SpeedRange)return shellView(t('按路口选一段'),heading(t('车道规则'),laneState),h(RangePanel,{s}));
 if(s.Picking){
  const roads=s.RuleRoads||[],both=roads.filter(r=>(r.Mask&3)===3).length,detours=s.Detours||0;
  const ls=s.LaneStats||{};
  // One row of rule counts; the vehicle counts sit small beside the state pill (G36).
  const headStats=h('div',{className:'rll-headstats'},h('small',null,t('按道驶出 {0}',fmt(ls.Total||0))+' · '),h('small',{'data-tone':detours>0?'amber':''},t('绕行 {0}',detours)),laneState);
  return shellView('选择路段',heading(t('车道规则'),headStats),
   h('div',{className:'rll-pills rll-oneline'},h('span',{className:'rll-pill','data-tone':'mint'},'已设 '+roads.length+' 段'),roads.some(r=>r.Mask)&&statPill(t('禁变道 {0}',roads.filter(r=>r.Mask).length)),roads.some(r=>r.Speed)&&statPill(t('限速 {0}',roads.filter(r=>r.Speed).length)),roads.some(r=>r.Yellow)&&statPill(t('双黄线 {0}',roads.filter(r=>r.Yellow).length)),roads.some(r=>r.BannedLanes)&&statPill(t('分道 {0}',roads.filter(r=>r.BannedLanes).length)),idPill),
   h(ModuleHero,{s,art:overviewArt(),picking:true,pickTitle:'选择路段',desc:rulesDesc}),
   h('button',{type:'button',className:'rll-link rll-rangego',onClick:()=>trigger('RoadLaneLock','speedRange',true)},t('按路口选一段改限速 ›')),
   h(RuleRoadList,{s}),
   fold('图例与说明',h('p',null,'悬停道路高亮，左键选中；已选中时点另一条直接换选，右键退出所选道路。'),h('p',null,'地图：白实线为已设变道管制的方向。按道驶出＝驶出管制路段且途中没变道的车，从本次进游戏算起，打开本页时刷新。')));
 }

 const controls=(s.Controls||[]).filter(Boolean);
 const hover=direction=>{setFocus({road:roadKey,direction});trigger('RoadLaneLock','previewMonitor',JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version,Direction:direction}));};
 const toggle=c=>request(c.Direction,!c.On);
 const laneTab=s.LaneTab||'lane';
 const overlay=h(React.Fragment,null,laneTab==='lane'&&h(RoadPins,{state:s,controls,selected,toggle,hover}),h(RulePins,{s}));
 const target=JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version});
 const allOn=controls.length>0&&controls.every(c=>c.On);
 const quickIcon=d=>h('svg',{'aria-hidden':true,width:14,height:14,viewBox:'0 0 24 24',fill:'none',stroke:'currentColor',strokeWidth:2,strokeLinecap:'round',strokeLinejoin:'round'},h('path',{d}));
 const ls=s.LaneStats||{};
 const dialog=shellView('路段操作',heading(t('车道规则'),h('div',{className:'rll-pills'},ls.Total>0&&statPill('按道驶出 '+fmt(ls.Total)+' 辆'),h('small',{className:'rll-count'},'已设 '+(s.SavedCount||0)+' 段'))),
  h(ModuleHero,{s,art:laneTab==='lane'||!s.LaneRules?laneArt(s.PaintText):ruleArt(laneTab,s.LaneRules),picking:false,pickTitle:'选择路段',desc:laneTab==='lane'||!ruleDesc[laneTab]?laneDesc:ruleDesc[laneTab],extra:
   s.Valid&&h('div',{className:'rll-pills'},s.Description&&h('span',{className:'rll-pill'},s.Description.RealLanes+' 车道'),s.RoadExtras&&h('span',{className:'rll-pill'},'长 '+s.RoadExtras.Length+' 米'),...rulePills(laneTab,s.LaneRules||{},s),laneTab==='lane'&&ls.Road>0&&statPill('按道驶出 '+fmt(ls.Road)+' 辆'),idPill)}),
  s.Valid&&h(RuleTabs,{s}),
  laneTab!=='lane'&&s.Valid&&h(RuleBody,{s}),
  laneTab==='lane'&&h(RoadCards,{s,controls,selected,toggle,hover}),
  laneTab==='lane'&&s.Valid&&controls.length>0&&h('div',{className:'rll-quick'},
   h('button',{type:'button','aria-label':'定位此路段',onClick:()=>trigger('RoadLaneLock','focusSelectedRoad',target)},quickIcon('M12 3v3M12 18v3M3 12h3M18 12h3M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6'),'定位'),
   controls.length>1&&h('button',{type:'button','aria-label':allOn?'两向都恢复':'两向都禁止',onClick:()=>trigger('RoadLaneLock','requestBoth',JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version,Enable:!allOn}))},quickIcon('M7 7h11l-3-3M17 17H6l3 3'),allOn?'两向都恢复':'两向都禁止')),
  laneTab==='lane'&&!controls.length&&h('p',{style:small},s.Valid?'此处没有可设置的机动车方向。':'原路段已失效，请重新选择。'),
  fold('图例与诊断',
   s.Valid&&s.Description&&h('p',{style:small},'路段 #'+s.Index+' · 节点 #'+s.Description.Start+' → #'+s.Description.End+' · '+s.Description.RealLanes+' 车道'),
   h('p',{style:small},'走错车道后绕行。市政车辆连续两次寻路失败后，临时使用原版选道。'),
   s.Valid&&btn('记录此路段及相邻路口',()=>trigger('RoadLaneLock','inspect',JSON.stringify({Session:s.Session,Index:s.Index,Version:s.Version})),'>'),
   h('p',{style:small},'已设置 '+(s.PendingCount||0)+' 段 · 正在绕行 '+(s.Detours||0)+' 辆'),
   s.Status&&h('p',{style:small},s.Status),
   (s.Enabled||s.PendingCount>0)&&btn('清除全部规则并恢复原版导航',()=>trigger('RoadLaneLock','resetAll',s.Session),'X',{style:{color:'#ffc5b5',background:'#493538'}})),
  s.ExecutorReady===false&&h('p',{style:{...small,color:palette.warning}},s.Status||'执行器当前不可用'));
 return h(React.Fragment,null,overlay,dialog);
}
export default function register(registry){registry.append('GameTopLeft',Launcher);registry.append('Game',Panel);}
export const hasCSS=false;

const data$=bindValue('RoadLaneLock.Palette','data','{}'),status$=bindValue('RoadLaneLock.Palette','status','');
export const presets=[['G 银座线','#FF9500'],['M 丸之内线','#F62E36'],['H 日比谷线','#B5B5AC'],['T 东西线','#009BBF'],['C 千代田线','#00BB85'],['Y 有乐町线','#C1A470'],['Z 半藏门线','#8F76D6'],['N 南北线','#00AC9B'],['F 副都心线','#9C5E31']];
const P=(text)=>text.split(',').map(x=>{const i=x.lastIndexOf(' ');return [x.slice(0,i),x.slice(i+1)];});
// Official line colours per operator (country → city → [label, hex]).
export const presetGroups=[
 ['日本',[['东京 Metro',presets.map(([n,v])=>[n.slice(2),v])],['都营地铁',P('浅草线 #E85298,三田线 #0079C2,新宿线 #6CBB5A,大江户线 #B6007A')],['大阪 Metro',P('御堂筋线 #E5171F,谷町线 #522886,四桥线 #0078BA,中央线 #019A66,千日前线 #E44D93,堺筋线 #814721,长堀鹤见绿地线 #A9CC51')]]],
 ['美国',[['纽约地铁',P('1/2/3 #EE352E,4/5/6 #00933C,7 #B933AD,A/C/E #0039A6,B/D/F/M #FF6319,G #6CBE45,J/Z #996633,L #A7A9AC,N/Q/R/W #FCCC0A')],['芝加哥 CTA',P('Red #C60C30,Blue #00A1DE,Brown #62361B,Green #009B3A,Orange #F9461C,Purple #522398,Pink #E27EA6,Yellow #F9E300')],['华盛顿地铁',P('Red #BF0D3E,Orange #ED8B00,Blue #009CDE,Green #00B140,Yellow #FFD100,Silver #919D9D')]]],
 ['英国',[['伦敦地铁',P('Bakerloo #B36305,Central #E32017,Circle #FFD300,District #00782A,H&C #F3A9BB,Jubilee #A0A5A9,Metropolitan #9B0056,Northern #000000,Piccadilly #003688,Victoria #0098D4,W&C #95CDBA,Elizabeth #6950A1,DLR #00A4A7,Overground #EE7C0E')]]],
 ['法国',[['巴黎地铁',P('1号线 #FFCD00,2号线 #003CA6,3号线 #837902,4号线 #CF009E,5号线 #FF7E2E,6号线 #6ECA97,7号线 #FA9ABA,8号线 #E19BDF,9号线 #B6BD00,10号线 #C9910D,11号线 #704B1C,12号线 #007852,13号线 #6EC4E8,14号线 #62259D')]]],
 ['德国',[['柏林 U-Bahn',P('U1 #7DAD4C,U2 #DA421E,U3 #16683D,U4 #F0D722,U5 #7E5330,U6 #8C6DAB,U7 #528DBA,U8 #224F86,U9 #F3791D')],['慕尼黑 U-Bahn',P('U1 #52822F,U2 #C20831,U3 #EC6726,U4 #00A984,U5 #BC7A00,U6 #0065AE')]]],
 ['中国',[['北京地铁',P('1号线 #C23A30,2号线 #006098,4号线 #008E9C,5号线 #A6217F,6号线 #D29700,7号线 #F6C582,8号线 #009B6B,9号线 #8FC31F,10号线 #009BC0,13号线 #F9E700,14号线 #D5A7A1,15号线 #5B2C68')],['上海地铁',P('1号线 #E3002B,2号线 #8CC220,3号线 #FCD600,4号线 #461D84,5号线 #944D9A,6号线 #D40068,7号线 #ED6F00,8号线 #0094D8,9号线 #87CAED,10号线 #C6AFD4,11号线 #871C2B,12号线 #007A60,13号线 #E999C0')],['香港 MTR',P('港岛线 #007DC5,荃湾线 #E2231A,观塘线 #00AB4E,将军澳线 #7D499D,东涌线 #F7943E,东铁线 #5EB6E4,屯马线 #9A3B26,南港岛线 #BAC429')]]],
 ['韩国',[['首尔地铁',P('1号线 #0052A4,2号线 #00A84D,3号线 #EF7C1C,4号线 #00A5DE,5号线 #996CAC,6号线 #CD7C2F,7号线 #747F00,8号线 #E6186C,9号线 #BDB092')]]]];
function readCountry(){try{return window.localStorage.getItem('rll.palette.country')||'';}catch{return '';}}
function saveCountry(v){try{window.localStorage.setItem('rll.palette.country',v);}catch{}}
export function normalize(value){const s=String(value||'').trim().replace(/^#/,'').toUpperCase();return /^[0-9A-F]{6}$/.test(s)?'#'+s:null;}
export function toColor(hex){const v=normalize(hex);if(!v)return null;return {r:parseInt(v.slice(1,3),16)/255,g:parseInt(v.slice(3,5),16)/255,b:parseInt(v.slice(5,7),16)/255,a:1};}
export function toHex(c){if(!c||![c.r,c.g,c.b].every(Number.isFinite))return null;return '#'+[c.r,c.g,c.b].map(v=>Math.round(Math.max(0,Math.min(1,v))*255).toString(16).padStart(2,'0')).join('').toUpperCase();}
// Line palette, shown inside the city transit panel's per-line settings.
function Palette({color,lineId,line,lines=[]}){
 const raw=useValue(data$),status=useValue(status$);
 let data;try{data=JSON.parse(raw)||{};}catch{data={};}
 const [country,setCountry]=React.useState(readCountry);
 const pickCountry=name=>{const next=country===name?'':name;setCountry(next);saveCountry(next);};
 const group=presetGroups.find(([name])=>name===country);
 const [hex,setHex]=React.useState(''),[name,setName]=React.useState(''),[message,setMessage]=React.useState('');
 const current=toHex(color);
 React.useEffect(()=>{trigger('RoadLaneLock.Palette','uiReady');},[]);
 React.useEffect(()=>{setHex(current||'');setMessage('');},[current]);
 const apply=value=>{const v=normalize(value);if(!v){setMessage('请输入六位色号，例如 #FF9500');return;}trigger('RoadLaneLock','applyLineColor',lineId,v);setHex(v);setMessage('已应用到当前线路');};
 const chip=(label,value,key,city)=>h('button',{key,type:'button',className:'rll-chip','aria-pressed':current===value,title:(city?city+' ':'')+label+' '+value,onClick:()=>apply(value)},h('i',{style:{backgroundColor:value}}),label);
 const dot=(value,key)=>h('button',{key,type:'button',className:'rll-dot-chip','aria-pressed':current===value,title:value,'aria-label':'应用 '+value,onClick:()=>apply(value),style:{backgroundColor:value}});
 const suggested=line?recommendColors(line,lines):[];
 const similar=line?similarLines(line,lines):[];
 const recent=data.Recent||[],favorites=data.Favorites||[];
 return h('div',{className:'rll-set-sec'},
  h('div',{className:'rll-set-head'},h('span',null,'线路配色'),h('span',{className:'rll-current'},h('i',{style:{backgroundColor:current||'transparent'}}),current||'')),
  similar.length>0&&h('small',{className:'rll-warn'},'与 '+similar.slice(0,3).join('、')+(similar.length>3?' 等':'')+' 颜色相近'),
  suggested.length>0&&h('div',{className:'rll-sub'},'推荐 · 按'+(labels[line.Mode]||'交通方式')+'挑选，已避开现有线路的相近色'),
  suggested.length>0&&h('div',{className:'rll-swatches'},...suggested.map(v=>dot(v,'s'+v))),
  h('div',{className:'rll-sub'},'参考色 · 各地轨道线路标准色'),
  h('div',{className:'rll-countries',role:'tablist'},...presetGroups.map(([name])=>h('button',{key:name,type:'button',role:'tab',className:'rll-country','aria-selected':country===name,'aria-expanded':country===name,onClick:()=>pickCountry(name)},name))),
  group&&h('div',{className:'rll-cities'},...group[1].map(([city,colors])=>h('div',{key:city},h('div',{className:'rll-city'},city),h('div',{className:'rll-swatches'},...colors.map(([n,v])=>chip(n,v,city+n,city)))))),
  h('div',{className:'rll-inline'},h('input',{className:'rll-input',style:{width:'100rem'},value:hex,maxLength:7,'aria-label':'颜色色号',onChange:e=>setHex(e.target.value),onKeyDown:e=>{e.stopPropagation();if(e.key==='Enter')apply(hex);}}),
   h('button',{type:'button',className:'rll-btn','data-primary':'1',onClick:()=>apply(hex)},'应用色号')),
  recent.length>0&&h('div',{className:'rll-sub'},'最近使用'),
  recent.length>0&&h('div',{className:'rll-swatches'},...recent.map(v=>dot(v,'r'+v))),
  h('div',{className:'rll-sub'},'我的收藏'),
  favorites.length>0&&h('div',{className:'rll-swatches'},...favorites.map(x=>h('span',{key:x.Hex,className:'rll-fav'},chip(x.Name||x.Hex,x.Hex,x.Hex),
   h('button',{type:'button',className:'rll-x',title:'删除此收藏','aria-label':'删除收藏 '+(x.Name||x.Hex),onClick:()=>trigger('RoadLaneLock.Palette','remove',x.Hex)},'×')))),
  h('div',{className:'rll-inline'},h('input',{className:'rll-input',style:{flex:1,minWidth:0},value:name,maxLength:32,placeholder:'收藏名称（可选）','aria-label':'收藏名称',onChange:e=>setName(e.target.value),onKeyDown:e=>e.stopPropagation()}),
   h('button',{type:'button',className:'rll-btn',disabled:!current,onClick:()=>{trigger('RoadLaneLock.Palette','favorite',JSON.stringify({Hex:current,Name:name}));setMessage('已收藏当前线路颜色');}},'收藏当前颜色')),
  h('small',{className:'rll-note'},message||status||'点击色块即应用到这条线路。'));
}
