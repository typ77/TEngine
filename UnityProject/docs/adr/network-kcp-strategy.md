# ADR: 网络传输层战略（KCP / WebSocket 定位）

- **Status**: Accepted（2026-07-08）
- **Deciders**: 网络模块方向 3 战略定位
- **关联**: 五维评估路线图方向 3；`NetworkModule.CreateTransport`

## Context

TEngine 网络模块传输层现状（2026-07-08）：

| 传输 | 状态 | 实现 |
|---|---|---|
| TCP | ✅ 已实现 | `TcpTransport`（.NET Socket） |
| KCP | 占位 `NotSupportedException` | 待 T10 |
| WebSocket | 占位 `NotSupportedException` | 待 T11 |
| Platform | ✅ 已实现 | adapter 模式（WebGL/小游戏） |

TEngine 定位为**通用游戏框架**，须覆盖多种游戏类型（含实时对战）。

## Decision

**通用框架，TCP + KCP + WebSocket 全支持。KCP 纳入路线图（T10 借 kcp2k），暂不实现，排期由首个实时游戏项目驱动。**

- **TCP**：默认传输，已实现，覆盖非实时游戏（回合制/SLG/卡牌/聊天/MMO 弱交互）
- **KCP**：可靠 UDP（ARQ over UDP），服务实时对战（MOBA/FPS/动作/格斗）+ 弱网。牺牲 ~10-20% 带宽换更低延迟。**实现方式：借 kcp2k（成熟 Unity KCP 库），不自写**。纳入路线图 T10，当前保留接口占位
- **WebSocket**：服务 WebGL/H5（T11，借 NativeWebSocket）
- **Platform adapter**：微信/字节小游戏等平台（已实现）

## Reasoning

1. **通用框架须支持实时 + 非实时**：只做 TCP 会限制框架适用范围（实时游戏弱网体验差）
2. **kcp2k 远优于自写**：kcp2k 是 Unity 生态成熟的 KCP 实现（广泛用于联网游戏），自写成本高 + 易错（KCP 的 ARQ/快速重传/拥塞控制复杂）
3. **暂不实现的理由（YAGNI）**：当前无具体实时对战项目驱动；过早实现 + 维护 kcp2k 依赖是负担。占位保留接口契约，项目驱动时再启动
4. **排期触发条件**：当首个实时对战游戏项目启动，T10（kcp2k → KcpTransport）+ T11（NativeWebSocket → WebGLWebSocketAdapter）排期

## Alternatives Considered

| 方案 | 决定 | 理由 |
|---|---|---|
| TCP-only | ❌ 否决 | 通用框架须支持实时游戏 |
| 自写 KCP | ❌ 否决 | 成本高、易错，kcp2k 成熟 |
| 立即实现 KCP | ❌ 否决 | 无项目驱动，YAGNI |
| **KCP 纳入路线图 + 占位** | ✅ 采纳 | 平衡通用性与当前投入 |

## Consequences

- **正向**：TCP 已可用于生产；KCP/WebSocket 接口契约保留（`TransportType` 枚举 + `CreateTransport` 分支），未来 T10/T11 无需改公共 API
- **负向**：实时游戏项目在 T10 完成前无法用本模块（需自行集成 KCP 或等 T10）
- **后续行动**：
  - 实时游戏项目启动 → T10（kcp2k → KcpTransport）+ T11（NativeWebSocket）
  - `NetworkModule.CreateTransport` 的 KCP/WebSocket `NotSupportedException` 消息已指向本 ADR
