# AI Native OS - 第一阶段结构设计（核心功能完成后再接 UI）

> 目标：先跑通稳定采集与核心业务闭环，不引入隐私安全复杂度；UI（WPF）在核心功能全部完成并稳定后再接入。

## 1. 实现顺序（严格按序）

1. **项目骨架与启动链路**
   - 建立三层：`Host`（Console/Worker 启动壳）/ `Core`（采集与状态机）/ `Infra`（SQLite与日志）
   - 跑通 Host、DI、后台服务生命周期（启动/停止/异常退出）

2. **统一事件模型 + 事件总线**
   - 定义标准事件结构与元数据
   - 所有采集模块只发布标准事件，不直接落库

3. **SQLite 最小闭环**
   - 先建 `Events` 总表（Payload JSON）
   - 异步批量写入（推荐：200ms 或 50 条触发一次）
   - 启用 WAL，减少写锁争用

4. **窗口监控（M1）**
   - 监听前台窗口变化
   - 上报窗口标题、进程名、窗口类名、时间戳
   - 去重（同窗口短时间重复不重复入库）

5. **进程监控（M2）**
   - 监听进程启动/退出
   - 建立启动-退出关联，计算会话时长

6. **输入快照（M3）**
   - 仅记录“最终值快照”，不记录逐键
   - 三触发：Focus Lost / Idle Timeout(3s) / Window Switch

7. **统一状态机整合**
   - 单一状态机维护：当前窗口、当前焦点控件、输入缓冲、最后输入时间
   - 窗口切换时强制 flush 当前控件快照

8. **运行控制（无 UI）**
   - 提供最小控制能力：启动后读取配置，支持暂停/恢复开关（配置热更新或本地控制命令）
   - 暂停时停止采集发布，进程保持运行

9. **配置系统最小版（M15 替代）**
   - 可配置：输入空闲阈值、是否启用输入采集、批量写入阈值
   - 使用 `appsettings.json` + `IOptionsMonitor` 热加载

10. **日志与自恢复**
    - 结构化日志（模块名、事件ID、耗时、异常）
    - 采集器异常自动重启（指数退避）

11. **性能基线与P95统计**
    - 资源采样间隔 1s
    - CPU/内存按滑动窗口计算 P95
    - 每 10 分钟落一次统计摘要

12. **回放与验收工具**
    - 提供简单查询（按时间段、按模块）
    - 用于验证事件顺序、完整性、延迟

13. **UI 集成阶段（后置）**
   - 仅在核心功能全部完成并通过稳定性验收后，开始接入 WPF 界面
   - 首批 UI 仅承载：状态展示、配置编辑、时间线查询，不改动采集与存储内核

---

## 2. 推荐目录结构（第一版）

```text
NTracking/
├─ src/
│  ├─ NTracking.Host/                         # Console/Worker 启动、控制入口
│  │  ├─ Program.cs
│  │  ├─ HostedServices/TrackingWorker.cs
│  │  ├─ Control/RuntimeControlService.cs
│  │  └─ appsettings.json
│  │
│  ├─ NTracking.Core/                         # 领域与采集核心
│  │  ├─ Abstractions/
│  │  │  ├─ IEventPublisher.cs
│  │  │  ├─ IEventSink.cs
│  │  │  ├─ ICollector.cs
│  │  │  └─ IResourceSampler.cs
│  │  ├─ Models/
│  │  │  ├─ EventBase.cs
│  │  │  ├─ WindowEvent.cs
│  │  │  ├─ ProcessEvent.cs
│  │  │  ├─ InputSnapshotEvent.cs
│  │  │  └─ ResourceSampleEvent.cs
│  │  ├─ Bus/EventBus.cs
│  │  ├─ State/InputCaptureStateMachine.cs
│  │  ├─ Collectors/
│  │  │  ├─ WindowCollector.cs
│  │  │  ├─ ProcessCollector.cs
│  │  │  └─ InputCollector.cs
│  │  ├─ Services/
│  │  │  ├─ CollectorHostService.cs
│  │  │  ├─ FlushCoordinator.cs
│  │  │  └─ PerformanceMonitorService.cs
│  │  └─ Config/TrackingOptions.cs
│  │
│  ├─ NTracking.Infrastructure/               # 存储、日志、配置
│  │  ├─ Storage/
│  │  │  ├─ SqliteConnectionFactory.cs
│  │  │  ├─ EventRepository.cs
│  │  │  ├─ EventBatchWriter.cs
│  │  │  └─ SchemaInitializer.cs
│  │  ├─ Logging/StructuredLogger.cs
│  │  └─ Configuration/OptionsProvider.cs
│  │
│  └─ NTracking.Contracts/                    # 可选：跨层DTO/常量
│     ├─ EventType.cs
│     └─ EventNames.cs
│
├─ tests/
│  ├─ NTracking.Core.Tests/
│  │  ├─ InputCaptureStateMachineTests.cs
│  │  └─ FlushCoordinatorTests.cs
│  └─ NTracking.Infrastructure.Tests/
│     └─ EventBatchWriterTests.cs
│
└─ docs/
   ├─ structure.md
   └─ event-schema.md
```

---

## 3. 类职责与方法清单（方法级）

## 3.1 Core.Abstractions

### `ICollector`
- `Task StartAsync(CancellationToken ct)`：启动采集器
- `Task StopAsync(CancellationToken ct)`：停止采集器
- `string Name { get; }`：模块名（用于日志/监控）

### `IEventPublisher`
- `ValueTask PublishAsync(EventBase evt, CancellationToken ct)`：发布标准事件

### `IEventSink`
- `Task HandleAsync(EventBase evt, CancellationToken ct)`：消费事件（如写库）

---

## 3.2 Core.Models

### `EventBase`
字段：`EventId`、`EventType`、`OccurredAtUtc`、`Source`、`SessionId`、`PayloadVersion`

### `WindowEvent`
字段：`ProcessName`、`WindowTitle`、`ClassName`、`IsSwitch`

### `ProcessEvent`
字段：`ProcessId`、`ProcessName`、`ExecutablePath`、`Action(Started/Exited)`、`DurationMs?`

### `InputSnapshotEvent`
字段：`ProcessName`、`WindowTitle`、`ControlType`、`ControlName`、`SnapshotText`、`TriggerReason`

### `ResourceSampleEvent`
字段：`CpuPercent`、`WorkingSetMb`、`PrivateMb`

---

## 3.3 Core.State

### `InputCaptureStateMachine`
职责：管理“输入缓冲 + 三触发快照”的唯一状态源。

关键方法：
- `void OnFocusChanged(ControlRef? oldControl, ControlRef? newControl)`
- `void OnTextChanged(ControlRef control, string currentText, DateTime utcNow)`
- `void OnWindowSwitched(WindowRef oldWindow, WindowRef newWindow, DateTime utcNow)`
- `IReadOnlyList<InputSnapshotEvent> OnIdleTick(DateTime utcNow)`
- `InputSnapshotEvent? ForceFlush(string reason, DateTime utcNow)`

规则：
- 触发 `FocusLost`：oldControl 有缓冲则 flush
- 触发 `IdleTimeout`：同控件 3 秒无变化则 flush
- 触发 `WindowSwitch`：当前缓冲立即 flush

---

## 3.4 Core.Collectors

### `WindowCollector`
- `StartAsync`：订阅前台窗口变化
- `OnForegroundChanged`：构造 `WindowEvent`
- `ShouldEmit`：短时间去重判断

### `ProcessCollector`
- `StartAsync`：启动 WMI 进程事件监听
- `OnProcessStarted` / `OnProcessExited`
- `TryCompleteDuration`：补全时长

### `InputCollector`
- `StartAsync`：订阅 UIA 焦点与Value变化
- `OnFocusChanged`
- `OnValueChanged`
- `OnTimerTick`：1s 心跳驱动 idle 判定

---

## 3.5 Core.Services

### `CollectorHostService`
- `StartAsync`：按顺序启动全部采集器
- `StopAsync`：按逆序停止
- `RestartCollectorAsync(string name)`：异常恢复

### `FlushCoordinator`
- `OnWindowSwitchFlushAsync`
- `OnFocusLostFlushAsync`
- `OnIdleFlushAsync`

### `PerformanceMonitorService`
- `CollectSampleAsync`：每秒采样
- `ComputeP95(window)`：计算窗口 P95
- `PersistSummaryAsync`：每10分钟入库

---

## 3.6 Infrastructure.Storage

### `SchemaInitializer`
- `InitializeAsync()`：建表与索引

### `EventBatchWriter`
- `Enqueue(EventBase evt)`
- `FlushAsync(CancellationToken ct)`
- `RunLoopAsync(CancellationToken ct)`：定时 + 阈值双触发

### `EventRepository`
- `InsertBatchAsync(IEnumerable<EventBase> events, CancellationToken ct)`
- `QueryByTimeRangeAsync(DateTime fromUtc, DateTime toUtc, string? type, CancellationToken ct)`

### `SqliteConnectionFactory`
- `Create()`：统一连接参数（WAL、BusyTimeout）

---

## 4. 数据库第一版（最小可用）

```sql
CREATE TABLE IF NOT EXISTS Events (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId TEXT NOT NULL,
    EventType TEXT NOT NULL,
    OccurredAtUtc TEXT NOT NULL,
    Source TEXT NOT NULL,
    SessionId TEXT NOT NULL,
    PayloadJson TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Events_OccurredAtUtc ON Events(OccurredAtUtc);
CREATE INDEX IF NOT EXISTS IX_Events_EventType ON Events(EventType);
CREATE INDEX IF NOT EXISTS IX_Events_SessionId ON Events(SessionId);
```

说明：
- 第一阶段用“总表 + JSON”减少模式演进成本。
- 第二阶段再按查询热点拆分明细表（如 Inputs、Windows）。

---

## 5. 核心阶段验收清单（通过后再做 UI）

- 程序启动后，后台服务稳定运行，采集状态可切换（配置热更新或本地控制命令）
- 能稳定记录窗口切换、进程启动退出、输入快照三触发
- SQLite 连续写入 2 小时无阻塞、无明显丢事件
- 暂停后无新增采集事件，恢复后继续正常采集
- 资源统计可输出 CPU/内存 P95（10 分钟窗口）
- 上述能力稳定后，才进入 WPF/托盘/设置页等 UI 开发

---

## 6. 第二阶段预留接口（先不实现）

- 敏感过滤三层联合：进程名 + 窗口标题关键词 + URL 规则
- 数据加密模块（DPAPI）
- 浏览器扩展与 Native Messaging
- LLM 摘要与语义检索
- WPF UI 层：托盘、设置页、时间线与搜索界面

---

## 7. 开发建议（你现在就可以开写）

- 先写：`EventBase`、`EventBus`、`EventBatchWriter`、`InputCaptureStateMachine`
- 再接：`WindowCollector`、`InputCollector`
- 最后接运行控制入口（配置热更新或本地控制命令）
- 每完成一个采集器，立即用查询工具验证事件流
- 核心阶段全部稳定后，再新建 `NTracking.App`（WPF）对接现有服务
