// Summarise hooked-executor traces (Kind=laneHook) from the game's Logs folder.
// usage: node tools/lane-hook-report.mjs [version (default: latest with traces)] [logsDir]
import fs from 'fs';
import path from 'path';
import os from 'os';

let version = process.argv[2];
const logs = process.argv[3] || path.join(process.env.CSII_USERDATAPATH || path.join(os.homedir(), 'AppData', 'LocalLow', 'Colossal Order', 'Cities Skylines II'), 'Logs');
const files = ['RoadLaneLock-events.1.jsonl', 'RoadLaneLock-events.jsonl'].map(f => path.join(logs, f)).filter(f => fs.existsSync(f));

if (!version) { const vs = []; for (const f of files) for (const l of fs.readFileSync(f, 'utf8').split('\n')) { try { const e = JSON.parse(l); if (e.Kind === 'laneHook') vs.push(e.Version); } catch { } } version = vs.at(-1) || 'none'; }
const events = [];
for (const f of files) for (const line of fs.readFileSync(f, 'utf8').split('\n')) {
  if (!line.trim()) continue;
  try { const e = JSON.parse(line); if (e.Version === version) events.push(e); } catch { }
}
const kinds = {};
for (const e of events) kinds[e.Kind] = (kinds[e.Kind] || 0) + 1;
const traces = events.filter(e => e.Kind === 'laneHook').map(e => ({ t: e.UTC, ...e.Data }));
const pct = (a, b) => b ? (100 * a / b).toFixed(1) + '%' : '-';

console.log(`版本 ${version}：事件 ${events.length} 条`, kinds);
const starts = events.filter(e => e.Kind === 'laneHookStarted').map(e => e.UTC);
console.log('新执行器启动：', starts.length ? starts.join(', ') : '无（开关可能没开）');
for (const e of events.filter(e => e.Kind === 'laneHookRules')) console.log('规则同步：', e.UTC, JSON.stringify(e.Data));

const exited = traces.filter(t => t.End === '驶出');
const other = traces.filter(t => t.End !== '驶出');
console.log(`\n逐车记录 ${traces.length}（驶出 ${exited.length}，其他 ${other.length}：${[...new Set(other.map(t => t.End))].join('/')}）`);
const stat = list => {
  const aligned = list.filter(t => t.Entry?.Aligned).length;
  const changed = list.filter(t => t.LaneChangedInside).length;
  const off = list.filter(t => t.Exit?.OffRoute).length;
  const unreach = list.filter(t => t.Plans?.Unreachable > 0).length;
  const early = list.filter(t => t.EarlyAligned).length;
  const held = list.filter(t => t.StrictHold?.Times > 0).length;
  const handoff = list.filter(t => t.StrictHold?.Handoff > 0).length;
  return `${list.length} 辆｜入口对准 ${pct(aligned, list.length)}｜提前对准 ${early}｜段内变道 ${changed}（${pct(changed, list.length)}）｜出口偏离路线 ${off}（${pct(off, list.length)}）｜曾无解 ${unreach}｜严格锁死 ${held}（交还 ${handoff}）`;
};
console.log('全部驶出：', stat(exited));
const byKind = {};
for (const t of exited) (byKind[t.Kind || '?'] ||= []).push(t);
for (const [k, list] of Object.entries(byKind)) console.log('  车型 ' + k + '：', stat(list));
const byTurn = {};
for (const t of exited) (byTurn[t.Route?.Turn || '?'] ||= []).push(t);
for (const [turn, list] of Object.entries(byTurn)) console.log(`  ${turn}：`, stat(list));

const byRoad = {};
for (const t of exited) { const k = (t.Route?.ExitRoad || '?') + ' / ' + (t.Route?.Turn || '?'); (byRoad[k] ||= []).push(t); }
console.log('\n按出口（前 15）：');
for (const [k, list] of Object.entries(byRoad).sort((a, b) => b[1].length - a[1].length).slice(0, 15)) console.log('  ', k, '→', stat(list));

// Stops inside the stretch: who needs the kerb lane, and how it went.
const stops = exited.filter(t => t.Stop);
console.log(`\n段内有停靠/终点：${stops.length} 辆`);
const stopBy = {};
for (const t of stops) { const k = (t.Kind || '?') + ' · ' + t.Stop.Kind; (stopBy[k] ||= []).push(t); }
for (const [k, list] of Object.entries(stopBy).sort((a, b) => b[1].length - a[1].length)) {
  const ranks = {};
  for (const t of list) { const m = /从右数第(\d+)\//.exec(t.Stop.Lane || ''); const key = m ? '右' + m[1] : '?'; ranks[key] = (ranks[key] || 0) + 1; }
  console.log('  ', k, '→', stat(list), '｜停靠车道', JSON.stringify(ranks));
}
const stopRoads = {};
for (const t of stops) { const road = (t.Stop.Lane || '?').split(' ')[0]; stopRoads[road] = (stopRoads[road] || 0) + 1; }
console.log('  按路段：', JSON.stringify(stopRoads));

// Kerb work decisions (0.2.85+): served as routed / skipped (conflicts with the exit lane) / served after too many skips.
const kerb = exited.filter(t => t.KerbWork);
if (kerb.length) {
  console.log(`\n路边作业处理：${kerb.length} 辆`);
  const kerbBy = {};
  for (const t of kerb) (kerbBy[t.Kind || '?'] ||= []).push(t);
  for (const [k, list] of Object.entries(kerbBy)) {
    const sum = f => list.reduce((a, t) => a + (t.KerbWork[f] || 0), 0);
    console.log('  ', k, `照常停靠 ${sum('Served')}｜跳过 ${sum('Skipped')}｜保底停靠 ${sum('Guarded')}`, '→', stat(list));
  }
}

// Jam relief on monitored roads (0.2.87+): released heads, why, and how many vehicles behind were unlocked.
const count = (list, key) => { const m = {}; for (const x of list) { const k = key(x); m[k] = (m[k] || 0) + 1; } return JSON.stringify(m); };
for (const e of events.filter(e => e.Kind === 'laneJamRoads')) console.log('监测路段同步：', e.UTC, JSON.stringify(e.Data));
const jams = events.filter(e => e.Kind === 'laneJam').map(e => e.Data);
const followerBy = new Map(events.filter(e => e.Kind === 'laneJamFollowers').map(e => [e.Data.Id, e.Data.Followers]));
console.log(`\n拥堵疏导：头车放弃转弯直行 ${jams.length} 次`);
if (jams.length) {
  console.log('  按路段（路段号 / 方向）：', count(jams, j => '#' + j.RoadId + '/' + j.Direction));
  console.log('  在等：', count(jams, j => j.Wait + (j.TargetFull ? '（转入车道满）' : '')));
  console.log('  触发：', count(jams, j => j.Trigger));
  console.log('  头车车型：', count(jams, j => j.Kind));
  const f = jams.map(j => followerBy.get(j.Id)).filter(n => n != null);
  console.log(`  后车解锁：共 ${f.reduce((a, b) => a + b, 0)} 辆（${f.length} 次有统计，平均 ${f.length ? (f.reduce((a, b) => a + b, 0) / f.length).toFixed(1) : '-'}）`);
}
// Heads that qualified but did not leave (0.2.91+): no straight way out, or still standing 10 s after the release.
const stuck = events.filter(e => e.Kind === 'laneJamStuck').map(e => e.Data);
if (stuck.length) {
  console.log(`  符合但没走（地图上变红）：${stuck.length} 次`, count(stuck, s => s.Reason));
  for (const s of stuck.slice(0, 8)) console.log(`    车#${s.Id} ${s.Kind} ${s.Road || ''}#${s.RoadId}/${s.Direction} ${s.Reason}`);
}
// Heads that stood 4 s with vehicles behind but were not released (0.2.88+): which condition stopped it.
const stalls = events.filter(e => e.Kind === 'laneJamMiss' && /停住20秒/.test(e.Data.Reason)).map(e => e.Data);
console.log(`
锁定车道停住保险阀：解锁 ${stalls.length} 次`, count(stalls, m => m.Kind), count(stalls, m => m.Road || m.RoadId));
for (const m of stalls.slice(0, 10)) console.log(`    车#${m.Id} ${m.Kind} ${m.Road || ''}#${m.RoadId}/${m.Direction} 停 ${m.Seconds} 秒｜下一步 ${m.Move}｜阻挡 ${m.Blocker}｜车道 ${m.LaneFlags}｜下一段 ${m.NextFlags}｜导航 ${m.Nav}`);
const misses = events.filter(e => e.Kind === 'laneJamMiss' && !/停住20秒/.test(e.Data.Reason)).map(e => e.Data);
if (misses.length) {
  console.log(`  未触发的头车：${misses.length} 条（每车每种原因记一次）`);
  console.log('    原因：', count(misses, m => m.Reason));
  console.log('    按路段（路段号 / 方向）：', count(misses, m => '#' + m.RoadId + '/' + m.Direction));
  const long = misses.filter(m => m.Queue >= 3).sort((a, b) => b.Queue - a.Queue).slice(0, 10);
  for (const m of long) console.log(`    车#${m.Id} ${m.Kind} ${m.Road || ''}#${m.RoadId}/${m.Direction} ${m.Reason}｜身后 ${m.Queue} 辆｜已停 ${m.Seconds} 秒${m.Move ? '｜下一步 ' + m.Move : ''}`);
  // 0.2.89+: state of heads with no blocker (why are they standing?)
  const none = misses.filter(m => m.Reason === '没有阻挡对象' && m.Path !== undefined);
  if (none.length) {
    console.log(`  "没有阻挡对象"的头车细节（${none.length} 条）：`);
    console.log('    下一步：', count(none, m => m.Move), '｜下一段是路口：', count(none, m => m.NextIsJunction));
    console.log('    路线状态：', count(none, m => m.Path), '｜本车道信号：', count(none, m => m.Signal ?? '无'), '｜路口车道信号：', count(none, m => m.NextSignal ?? '无'));
    console.log('    车辆标志：', count(none, m => m.Car));
    console.log('    当前车道标志：', count(none, m => m.LaneFlags));
    console.log('    下一段标志：', count(none, m => m.NextFlags ?? '无'));
    for (const m of none.slice(0, 8)) console.log(`    车#${m.Id} ${m.Kind} ${m.Road || ''} 停${m.Seconds}秒 身后${m.Queue} ${m.Move} 路线=${m.Path} 信号=${m.Signal ?? '-'}/${m.NextSignal ?? '-'} 导航${m.Nav}段 车道=${m.LaneFlags} 下一段=${m.NextFlags ?? '-'}`);
  }
}
// Route check after a handoff: unsafe mid-block U-turns replaced by driving straight past the next junction.
const reroutes = events.filter(e => e.Kind === 'laneReroute').map(e => e.Data);
console.log(`\n交还后掉头检查：改为直行 ${reroutes.filter(r => r.Try).length} 次（涉及 ${new Set(reroutes.filter(r => r.Try).map(r => r.Id)).size} 辆）｜交给原版 ${reroutes.filter(r => !r.Try).length} 辆 ${count(reroutes.filter(r => !r.Try), r => r.Result)}`);
if (reroutes.length) console.log('  车型：', count(reroutes.filter(r => r.Try), r => r.Kind), '｜第几次：', count(reroutes.filter(r => r.Try), r => r.Try));

const bad = exited.filter(t => t.LaneChangedInside || t.Exit?.OffRoute || !t.Entry?.Aligned);
console.log(`\n问题样本（共 ${bad.length}，列前 20）：`);
for (const t of bad.slice(0, 20)) console.log('  ', t.t, `车#${t.Id||t.Vehicle?.Index} ${t.Kind||''}`, `去向=${t.Route?.ExitRoad}/${t.Route?.Turn}`, `计划入口=${t.Entry?.Planned}`, `实际入口=${t.Entry?.Actual}`,
  t.LaneChangedInside ? '段内变道' : '', t.Exit?.OffRoute ? `偏离→${t.Exit?.Actual}` : '', JSON.stringify(t.Plans), t.Skipped&&Object.keys(t.Skipped).length?'跳过:'+JSON.stringify(t.Skipped):'');

// Errors mentioning the mod in game logs.
for (const name of ['RoadLaneLock.log', 'Player.log', 'Modding.log']) {
  const f = path.join(logs, name);
  if (!fs.existsSync(f)) continue;
  const hits = fs.readFileSync(f, 'utf8').split('\n').filter(l => /exception|error/i.test(l) && /RoadLaneLock|LaneHook/i.test(l));
  if (hits.length) { console.log(`\n${name} 相关错误 ${hits.length} 条（前 10）：`); hits.slice(0, 10).forEach(l => console.log('  ', l.slice(0, 300))); }
}
