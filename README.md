# Traffic Toolbox / 交通工具箱

A Cities: Skylines II code mod for lane-level traffic rules. Every rule is painted on the road, so you can see on the street what the rule does.

Status: **alpha (0.3.5)**. It's tested privately and not yet on Paradox Mods. The internal name is `RoadLaneLock`, and saves use it.

[中文说明](#中文说明)

## Features

- **Road rules**
  - No lane change on a section (solid white lines).
  - Speed limit per road (number painted on every lane; route planning uses the new speed).
  - Double yellow line (closes driveway left turns, U-turns and mid-block crossings).
  - Lane access by vehicle type: cars, trucks, buses, taxis, motorbikes, bicycles and service vehicles. Each lane gets its own sign.
- **Junction rules**: ban pedestrians from crossing a junction. Crosswalks are closed, and people use a footbridge or another junction instead.
- **Congestion relief**: the head of a queue that's stuck waiting to turn gives up and drives straight on, then re-routes from the next junction.
- **Traffic info**
  - Live vehicle mix.
  - Average flow against the speed limit.
  - A radar of the game's bottlenecks.
  - 6 h / 24 h trend with rule changes marked.
  - "Who uses this road": vehicle types, and whether drivers are passing through or headed nearby.
- **Roadside parking**: no-parking rules with signs.
- **Public transport**: line overview, line naming and a line colour palette.

The interface follows the game language: English, Simplified Chinese and Traditional Chinese are built in. Settings can export a template for other languages.

## Building

You need the Cities: Skylines II modding toolchain installed by the game, so that `CSII_MODPOSTPROCESSORPATH`, `CSII_UNITYMODPROJECTPATH` and `CSII_MANAGEDPATH` are set. You also need the .NET SDK.

```powershell
.\Build.ps1 -PostProcess
```

The output goes to `bin/Release/net48`. `Install.ps1` copies a matching official build into your local mods folder (`CSII_LOCALMODSPATH`).

The Windows ransomware protection (Controlled folder access) blocks the post-processor from writing inside `Documents`. Keep the project outside protected folders.

## Tests

```powershell
node tests/ui.test.mjs
node tests/palette.test.mjs
```

`tools/lane-hook-report.mjs <version>` summarises the mod's event log after a play session.

## Repository layout

| Folder | What's in it |
| --- | --- |
| `src/` | The mod: systems, rules, UI bindings |
| `ui/` | The in-game panel (`RoadLaneLock.mjs`), icons and painted sign artwork |
| `lang/` | Built-in translations (keys are the Chinese source strings) |
| `core/` | Pure planning logic shared with the checks |
| `tests/`, `verification/` | UI tests and offline regression checks |
| `tools/` | Log report, sign painting and translation helpers |
| `experimental/` | Earlier navigation prototypes kept for reference |
| `docs/` | Older release notes (0.2.x) |

---

## 中文说明

交通工具箱是《都市：天际线 2》的代码模组，做车道级的交通规则。每条规则都会画到路面上，路上看得到规则在管什么。

当前是 **alpha 0.3.5**，只在私下测试，还没上 Paradox Mods。内部名叫 `RoadLaneLock`，存档里用的也是这个名字。

- **路段规则**
  - 禁止变道（白实线）。
  - 按路段限速（每条车道都刷上数字，寻路也按新限速算）。
  - 双黄线：封住出入口左转、掉头和路段中间的横穿。
  - 按车型分道：私家车、货车、公交、出租、摩托车、自行车、市政车，每条车道各有标志。
- **路口规则**：禁止行人过街，人行横道关闭，行人改走天桥或别的路口。
- **拥堵疏导**：等着转弯又卡住的头车放弃转弯、直行通过，到下一个路口再重新找路。
- **交通信息**
  - 实时车型构成。
  - 平均流速（实时车速和限速比）。
  - 原版堵点雷达。
  - 6 小时和 24 小时走势，规则开关时间会标在图上。
  - 「谁在用这条路」：看是哪些车，是路过还是去附近。
- **路边停车**：禁停规则，带禁停牌。
- **公共交通**：线路总览、线路命名、线路色板。

构建和测试方法见上面英文部分。项目别放在「文档」这类受保护的文件夹里，不然 Windows 的受控文件夹访问会拦住官方后处理器。
