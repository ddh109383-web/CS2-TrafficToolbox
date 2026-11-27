# 路边连接车道反复寻路审计与修复

日志：RoadLaneLock-vehicles-20261117-130235-102-2cd1bb.jsonl，0.2.9-alpha，记录道路 1302231:1，752 完整帧、58,763 条车辆观测、85 辆车、264 个路径定义；格式 3 按 Entity+Version+Revision 重建，缺失引用 0。

## 已确认的异常
车辆 718686:1 在整个约 28.37 秒的采样跨度位置完全不变，Blocker 始终为空，24 次独立 Obsolete → Pending → Updated → 正常状态循环。当前车道 840893:1 标记 Unsafe, SideConnection, TurnRight, Yield, AllowEnter；车身曲线位置约 0.483322，导航终点仅约 0.483323。路径反复生成与清空，导航缓冲区为空。

车辆 719150:1 同样位置完全不变、没有 Blocker，23 次独立重寻路循环。当前车道 1033308:3 标记 Unsafe, SideConnection, TurnRight, Yield，车辆处于 EndReached, EnteringRoad, FixedLane, RequestSpace, ClearedForPathfind；在记录中生成的路径首段包含 938345 与 938346 等不同选择。这与用户观察到的路线来回变化相符，但日志未记录车型名、司机名，不能唯一证明是哪张截图中的车辆。

## 代码对应
GameHorizonPlanner.Add 拒绝 SideConnection，Evaluate 因而无法构造连续普通车道方案。当前 owner 在保护范围内时，FillNavigationPaths 尝试 BuildLegalExit，而 PhysicalLane 同样拒绝 SideConnection；随后 FixedLane + invalidPath=0 清空路径并设置 Obsolete。每次原版返回新路径后都会重复。这说明当前路边连接动作被错误套用了普通行车道的禁变道约束。

## 0.2.10 修改
FillNavigationPaths 只在当前 CarLane 带 SideConnection 标记时跳过本 mod 的反向跳道拦截和连续车道规划，交还原版选道及路口预留。离开连接车道后立即恢复普通禁变道规则。未放宽所有 Unsafe 车道，不根据超时放行，不删除／移动车辆，不改信号灯，不取消已开始的变道。市政类别豁免保持不变。

## 范围与验证
编译 0 错误 0 警告、官方 Entities/Jobs/Burst 处理和 778 项既有路径策略检查通过。这些检查不能替代实机恢复测试。日志中另有自行车 1773032:1 停留、没有导航且车道 Obsolete，及多个稳定阻挡队列；它们不是这两辆车的同一重寻路循环，未宣称全部解决。车辆帧没有 job 内事件，因果判断来自记录状态与代码分支对应。
