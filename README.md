<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/assets/hero-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="docs/assets/hero-light.svg">
    <img alt="Apple Magic Trackpad Windows Suite" src="docs/assets/hero-light.svg" width="920">
  </picture>
</p>

<div align="center">
<details><summary><b>Read this in other languages / 切换语言</b></summary>

<a href="README.md">简体中文</a> | <a href="docs/README.en.md">English</a>

</details>
</div>

<p align="center">
  <a href="https://github.com/CNDDVP/apple-magic-trackpad-windows/releases"><img src="https://img.shields.io/badge/Suite-v1.2.0-008a64?style=flat" alt="Suite v1.2.0"/></a>
  <a href="https://github.com/CNDDVP/apple-magic-trackpad-windows/actions/workflows/build.yml"><img src="https://github.com/CNDDVP/apple-magic-trackpad-windows/actions/workflows/build.yml/badge.svg" alt="Build and behavior tests"/></a>
  <a href="https://github.com/CNDDVP/apple-magic-trackpad-windows/actions/workflows/release.yml"><img src="https://github.com/CNDDVP/apple-magic-trackpad-windows/actions/workflows/release.yml/badge.svg" alt="Package release assets"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue?style=flat" alt="License: MIT"/></a>
  <img src="https://img.shields.io/badge/Windows_11-24H2%2B_(Build_26100%2B)-0078D4?style=flat&logo=windows11&logoColor=white" alt="Windows 11 24H2+"/>
  <img src="https://img.shields.io/badge/Arch-AMD64_%7C_ARM64-4d6b60?style=flat" alt="AMD64 | ARM64"/>
  <a href="TEST_REPORT_v1.2.0.md"><img src="https://img.shields.io/badge/Tests-Verified-16a34a?style=flat" alt="Tests: verified"/></a>
</p>

<p align="center">
  <b>让 Apple 妙控板在 Windows 11 上拥有原生手势、硬件时钟级 S 型加速与防误触深按预览。</b><br/>
  底层签名驱动仍为发行包 v2.2 / INF <code>2026.3982.1.1000</code>；本页的 <b>1.2.0 是工具套件版本</b>，增强后台保留已验证的 v1.1.1。
</p>

在 Windows 11 上运行一行安装或热升级脚本，即可为 **Apple USB-C / Lightning 妙控板（Magic Trackpad）** 配置 WHQL/目录签名驱动、USB 与蓝牙双分支参数、**100µs 硬件扫描时钟 S 型加速**、**跨屏像素中心补偿**，以及支持 **文件资源管理器 / 桌面（QuickLook）** 与 **Directory Opus 13** 的 **Force Click 深按文件预览**。

- **硬件 `ScanTime` 驱动的指针与跨屏曲线。** 直接解析 112 字节、`ReportID=0x05` 原始 HID 报文的硬件 `ScanTime`（100µs/计数，16 位回绕），不再受 Windows 消息调度间隔抖动影响；多屏位置补偿通过 `SendInput` 绝对虚拟桌面坐标提交，妥善处理负坐标、屏幕间空隙与像素中心映射。
- **物理按下即锁定的拖拽/深按互斥状态机。** 从物理按下（Button-Down）瞬间锚定起点与基准压力；手指位移、光标移动超限、多指触控或触点切换立即锁定为拖拽并取消预览。可选 **“轻压跨屏”** 预设（90 阈值 + 180ms 稳定 + 额外加力 15），原地普通长按绝不误触预览，达到 180 的明确重按仍即时响应。
- **热加载面板与字节级快照回退。** 深按预览、S 型加速、多屏补偿可独立开关，保存后约 1 秒热生效；升级器自动创建含 SHA256 校验与独立恢复脚本的 `SnapshotDir`，启动验证失败自动回滚。

> [!NOTE]
> **关于震动与预览边界的诚实说明：** 资源管理器与桌面使用内置 [QuickLook](https://github.com/QL-Win/QuickLook) 预览；Directory Opus 13 仅在 `dopus.exe` 的文件列表（Lister File Display）中调用现有的 `Space` 预览操作（须先在 Opus 中配置好空格预览）。**本套件没有新增独立的第二次物理震动（Second-Stage Haptic）。**

---

## 30 秒快速开始 (Quickstart)

### 场景 A：这台电脑已配置过驱动 —— 仅升级工具（推荐）

从 [Releases](https://github.com/CNDDVP/apple-magic-trackpad-windows/releases) 下载 `MagicForceClick-v1.2.0-update.zip`，解压后在**当前登录用户**的 PowerShell 中执行：

```powershell
.\Update_MagicForceClick.ps1
# 也可显式指定正在使用的程序目录
.\Update_MagicForceClick.ps1 -RuntimeDir 'C:\实际目录\MagicTrackpad'
```

更新器自动从当前用户启动项识别运行目录，先校验配置并生成快照备份，再更新后台、控制中心和入口；后台字节相同时保留现有进程并保留现有 `MagicForceClick.json` 个人设置。

### 场景 B：新电脑 —— 完整安装驱动与套件

要求 **Windows 11 24H2（Build 26100+）**、**64 位 PowerShell**；请使用**当前交互登录用户的管理员权限**运行（不要切换到另一个管理员账户）：

```powershell
git clone https://github.com/CNDDVP/apple-magic-trackpad-windows.git
cd apple-magic-trackpad-windows

# 标准安装（沿用 v1.1 默认曲线与 180 压力阈值）
.\Install_All_MagicTrackpad.ps1 -CursorSpeed 14 -ForceClickThreshold 180

# 或：直接使用实机校准的“轻压跨屏”方案（90 阈值 + 180ms 稳定 + Δ15 加力 + 温和跨屏曲线）
.\Install_All_MagicTrackpad.ps1 -CursorSpeed 14 -LightCrossScreen
```

安装或升级完成后，运行目录结构如下：

```text
%LOCALAPPDATA%\Programs\MagicTrackpad\
├── MagicForceClick.exe              # 后台常驻程序（支持 --settings 打开图形设置面板）
├── MagicForceClick.json             # 个人手感与开关配置（升级自动保留，保存后约 1 秒热加载）
├── MagicForceClick.status.json      # 实时运行诊断（RawInput 状态、有效/重复帧、重置与发送统计）
├── MagicControlCenter\MagicControlCenter.exe # 五页控制中心（含独立 DPI 配置）
├── Open_MagicTrackpad_ControlCenter.cmd # 打开控制中心
├── Open_MagicTrackpad_Settings.cmd  # 双击直接打开手感与深按设置窗口
└── rollback-snapshots\              # 每次升级自动生成的字节级备份快照与独立 Restore 脚本
```

---

## 可视化控制中心（v1.2.0）

<p align="center"><img src="docs/assets/control-center.png" alt="Magic Trackpad 控制中心指针手感页" width="920"></p>

五页原生 Windows 界面统一管理指针曲线、深按预览、原生手势和后台诊断。安装或升级后，双击桌面“妙控板控制中心”，也可运行：

```powershell
.\Open_MagicTrackpad_ControlCenter.cmd
```

个人配置仍在上级运行目录的 `MagicForceClick.json`。保存前备份，保留高级及未知字段；检测其他窗口修改，并根据新鲜后台状态确认生效。截图来自隔离验收。详见[控制中心说明](MAGIC_CONTROL_CENTER.md)、[v1.2.0 更新说明](RELEASE_NOTES_v1.2.0.md)和[验证范围](TEST_REPORT_v1.2.0.md)。

主分支已修复设置页预设高亮、草稿状态和保存反馈：曲线预设保留深按力度与功能开关，原生参数改回原值可恢复干净状态，后台生效确认同时核对进程、时间和实际参数。详见[设置状态修复说明](docs/control-center-settings.md)。已有 v1.2.0 发行附件保留原版。

## 工作流与实时状态 (See it in action)

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/assets/demo-pipeline-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="docs/assets/demo-pipeline-light.svg">
    <img alt="MagicForceClick Real-Time HID and Force Click Pipeline" src="docs/assets/demo-pipeline-light.svg" width="920">
  </picture>
</p>
<p align="center">
  <em>从 112 字节 HID 0x05 原始报文到多屏虚拟桌面坐标补偿与安全焦点预览的完整处理链路。</em>
</p>

后台程序在运行期持续维护 [`MagicForceClick.status.json`](MagicForceClick.cs)，可直接查看实时扫描帧、配置生效状态与提交计数（诊断状态用于核对管线健康度，不替代实机手势验收）：

```powershell
PS> Get-Content "$env:LOCALAPPDATA\Programs\MagicTrackpad\MagicForceClick.status.json" | ConvertFrom-Json

Version                   : 1.1.1
RawInputRegistered        : True
EnableForceClick          : True
EnableAcceleration        : True
EnableMonitorCompensation : True
ForceClickThreshold       : 90
ForceClickDwellMs         : 180
ForceClickPressureRise    : 15
ForceClickImmediate       : 180
ValidScanFrames           : 2587
DuplicateScanFrames       : 0
InputSendFailures         : 0
```

随时调出手感面板切换预设或微调参数：

```powershell
# 方式 1：双击运行目录下的快捷脚本
.\Open_MagicTrackpad_Settings.cmd

# 方式 2：命令行直接呼出图形设置界面
.\MagicForceClick.exe --settings
```

---

## 核心能力一览 (What it does)

| 模块 | 提供的能力 |
|---|---|
| **Directory Opus 13 深按预览** | 精准识别 `dopus.exe` 的 Lister 文件列表（`dopus.filedisplay` / `dopus.iconfiledisplay`），调用现有 `Space` 预览动作；重命名框、地址栏、搜索框、嵌入式预览器、对话框及按住修饰键时绝不误发空格，等待期间焦点变化也会自动取消。 |
| **“轻压跨屏”实机预设** | 压力阈值 `90`、稳定按压 `180ms`、且须比物理按下瞬间增加至少 `15` 的压力（普通按住不会变成长按预览）；达到 `180` 的明确深按保留即时响应。温和跨屏曲线为 `0.95 / 3.5 / 3.8 / 2.0`。 |
| **硬件 `ScanTime` 速度解算** | 基于 112 字节 `ReportID=0x05` 报文的 `ScanTime`（100µs/计数，16 位回绕）计算真实指速；对蓝牙批量到达、重复帧、计数重启、触点 ID 切换和长停顿均有独立消抖处理。 |
| **虚拟桌面 `SendInput` 多屏补偿** | 结合显示器 EDID 物理尺寸与 DPI 缩放比，经 Windows `SendInput` 提交绝对虚拟桌面坐标，覆盖负坐标、屏幕间空隙、边界裁剪与像素中心映射，彻底消除快甩跨屏后回跳原屏幕的问题。 |
| **低压力与全行程拖拽互斥** | 从物理按下时锚定位置与初始力度，中途压力回落重新计时；手指位移超 `220`、光标移动超 `4px`、多指或触点更换后立即锁定为拖拽并取消预览，直至松手。 |
| **独立开关与 1 秒热加载** | 深按预览、S 型加速、多屏补偿、按住拖拽加速可独立启停；支持调节慢/快增益、介入速度、曲线指数、压力阈值与拖拽保护，无效编辑自动保留上次有效配置。 |
| **USB + 蓝牙双分支与自愈任务** | 自动安装 AMD64/ARM64 签名驱动（`AmtPtpDevice.inf`），同步写入 USB 与蓝牙服务注册表参数、关闭轻点拖拽防误触，并注册登录/每 15 分钟/睡眠唤醒 `MagicTrackpad_AutoFix` 自愈计划任务。 |

> [!TIP]
> **想要保留原版重按手感，或只用深按不要加速？** 升级默认完全保留已有 `MagicForceClick.json`；发布默认值也继续沿用 v1.1 参数（压力阈值 `180`、曲线 `0.95 / 3.40 / 4.5 / 1.85`）。打开设置窗口即可一键切换 **“轻压跨屏”**、**“v1.1 手感”** 或 **“仅保留深按”**。

---

## 实测与回归验证 (Validation & Benchmarks)

v1.1.1 在 **Windows 11 x64 + 蓝牙 Apple USB-C 妙控板（A3120 / PID `0324`）+ 签名驱动 `2026.3982.1.1000` + Directory Opus `13.25 x64`** 环境下完成自动化与实机采样验证（详见 **[TEST_REPORT_v1.1.1.md](./TEST_REPORT_v1.1.1.md)**）：

| 验证项 | 实测结果 | 覆盖范围与说明 |
|---|---|---|
| **离线行为测试 (`MagicForceClick.Tests`)** | **59 项全部通过** | 扫描时钟回绕、蓝牙批量帧、压感/拖拽互斥、S 型曲线、轻压稳定/加力/直达双路径、Opus 焦点路由、虚拟桌面全像素映射（`x=-1920..6399, y=0..2162`） |
| **配置与 ABI 集成测试 (`IntegrationTests`)** | **5 项全部通过** | 热加载、无效配置保护与恢复、4 项独立开关 / 9 项参数控件绑定、Windows `INPUT` 结构体原生 ABI 校验 |
| **升级与回退自测 (`Test-UpdateRollback.ps1`)** | **5 组全部通过** | 正常升级、逐字节精确还原、损坏快照拒绝、无效配置拦截、启动失败自动回滚（v1.1.1 → v1.1.0 验证 8 个文件逐字节恢复） |
| **原始重按实机报文回放** | **2,810 帧 / 20 次按压通过** | 2,587 个有效扫描间隔；4 次拖拽全部拦截，7 次深按意图准确触发，普通点击 0 误触 |
| **舒适力度与长按实机回放** | **1,322 帧通过** | 普通点击与 2 次舒适力度原地长按均不误触预览；主动加深按压准确触发 1 次预览意图 |
| **跨屏快甩防回跳采样对比** | **反向大跳变 `22 → 0` 次** | 修复前 2,080 帧出现 22 次沿运动反方向跨屏回跳；改用 `SendInput` 绝对虚拟桌面坐标后复测 2,028 帧为 **0 次**，提交失败 **0 次** |

---

## 手感预设与配置参考 (Configuration)

[`MagicForceClick.json`](MagicForceClick.json) 采用版本化配置。S 型增益叠加在 Windows 指针输出上（默认配套 Windows 鼠标速度 `14`），并非替换系统加速。

| 配置项 | `v1.1 手感`（默认） | `轻压跨屏`（可选预设） | 说明与安全约束 |
|---|---|---|---|
| `ForceClickThreshold` | `180` | `90` | 深按压力阈值（范围 `70..250`；低于 `120` 时强制要求开启稳定时间与额外加力保护） |
| `ForceClickDwellMs` | `0` | `180` | 深按稳定保持时间（毫秒，中途压力回落会重新计时；阈值 `<120` 时至少需 `120ms`） |
| `ForceClickPressureRise` | `0` | `15` | 物理按下后需额外增加的压力值（防原地普通长按误触发；阈值 `<120` 时至少需 `10`） |
| `ForceClickImmediateThreshold` | `0`（关闭） | `180` | 快速重按直达阈值（达到该力度时跳过等待，立即触发预览） |
| `MinGain` / `MaxGain` | `0.95` / `3.40` | `0.95` / `3.50` | 慢速精细定位增益 / 快速跨屏最大增益 |
| `MidSpeed` / `CurvePower` | `4.50` / `1.85` | `3.80` / `2.00` | S 型加速介入速度 / 曲线指数 |
| `DragCancelCursorPixels` | `4` | `4` | 物理按下后光标移动超过该像素数即锁定为拖拽并取消本次深按预览 |
| `EnableForceClick` | `true` | `true` | 独立开关：深按文件预览（Explorer / 桌面 QuickLook + Directory Opus 13） |
| `EnableAcceleration` | `true` | `true` | 独立开关：基于硬件 `ScanTime` 的 S 型动态加速 |
| `EnableMonitorCompensation` | `true` | `true` | 独立开关：多显示器物理尺寸与虚拟桌面绝对坐标补偿 |
| `AccelerateWhilePressed` | `true` | `true` | 独立开关：物理按住拖拽期间是否继续应用 S 型加速 |

<details>
<summary><b>查看版本演进详情（v1.1.1 & v1.1.0 更新日志）</b></summary>

### v1.1.1 的修复与可选手感
- **Directory Opus 13 深按预览**：识别 `dopus.exe` 的 Lister 文件列表，调用现有的空格预览动作。重命名框、地址栏、搜索框、嵌入式预览器、对话框及按住修饰键时均不发空格；等待期间焦点改变也会取消。用户已在 Opus 13.25 x64 / 蓝牙 A3120 上确认深按可预览。
- **“轻压跨屏”预设**：压力阈值 90、稳定按压 180ms，还需比本次物理按下时增加至少 15 的压力；只保持普通按压不会变成长按预览。达到 180 的明确深按保留即时响应。最终温和曲线为慢速增益 0.95、最大增益 3.5、介入速度 3.8、指数 2.0；用户已确认预览省力、跨屏速度合适、定位和连续移动正常。
- **低压力下的拖动保护**：从物理按下时锚定位置和初始力度，中途压力回落重新计时，移动/多指/接触更换仍锁定取消。阈值低于 120 必须同时启用至少 120ms 稳定判断和 10 的额外加力要求；默认 180/0ms 配置保持兼容。
- **多屏位置补偿**：通过 Windows `SendInput` 的绝对虚拟桌面坐标提交，处理负坐标、屏幕间空隙和像素中心映射；不再直接用 `SetCursorPos` 改位置。运行状态记录输入提交失败次数。
- **保留个人设置**：升级仍保留已有 JSON，发布默认值也沿用 v1.1 参数。需要轻压方案时，打开设置，点“轻压跨屏”后保存；“v1.1 手感”可恢复原参数。

### v1.1.0 的三项改进
- **按设备扫描时间计算速度**：使用 112 字节、`ReportID=0x05` 报文的 `ScanTime`（100µs/计数，16 位回绕），不再用处理消息的间隔估算速度。批量到达、重复帧、计数重启、接触 ID 切换和长时间停顿均有独立处理。
- **拖拽与深按互斥**：从物理按下开始记录起点，不受普通按压力度低于 110 影响。手指移动超限、光标移动超限、多指或接触更换后，本次按压锁定为拖拽/取消预览，直到松开。异步预览还检查取消标记与前台窗口是否变化。
- **独立开关与手感面板**：深按预览、S 型加速、多屏补偿可独立关闭；可调慢/快增益、介入速度、曲线指数、压力阈值与拖拽保护。保存约 1 秒生效，无效编辑保留上一次有效配置。

</details>

---

## 升级与安全回退 (Upgrade & Rollback)

> [!IMPORTANT]
> **工具升级不触碰底层驱动：** [`Update_MagicForceClick.ps1`](Update_MagicForceClick.ps1) 不会重装驱动，也不修改 Windows 鼠标速度、蓝牙配对、QuickLook 或计划任务。新建启动项不再携带固定 `180` 命令行参数，从而让设置面板中的压力阈值直接生效（旧版位置参数仍保持兼容）。

### 回退到升级前版本

每次执行 `Update_MagicForceClick.ps1` 都会输出 `SnapshotDir`（位于运行目录下的 `rollback-snapshots\`），内含升级前各文件的存在状态、SHA256 哈希、原启动项以及独立的恢复脚本。**请保留整个 `rollback-snapshots` 文件夹。**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '实际SnapshotDir\Restore_MagicForceClick.ps1' -SnapshotDir '实际SnapshotDir'
```

- **逐字节校验还原**：恢复脚本先校验备份哈希与路径，再停止当前程序、还原旧文件、清理本次新增文件、恢复原启动项并重启原程序（支持重复执行；除非希望恢复后保持关闭，否则不要传 `-DoNotStart`）。
- **多层基线保障**：此次发布的原始基线为提交 [`2594280`](https://github.com/CNDDVP/apple-magic-trackpad-windows/commit/2594280052ed327c5a328f9b0e3d38f2ec7545ce)。GitHub Release 同时附带 `v1.1.0` 回退包与 `2594280` 原始基线包；本地升级快照则用于完整恢复个人配置与启动项（恢复脚本不能恢复被外部手动删除/损坏的快照，也不撤销用户之后另行安装的驱动）。

---

## 硬件说明与注意事项 (Hardware Notes)

- **首次蓝牙配对**：主安装脚本会自动合并两个 QuickLook 分卷并校验 `SHA256`、安装对应架构（`AMD64` / `ARM64`）的 Apple 签名驱动、配置 USB/蓝牙双分支与原生触控板参数。驱动安装后可能需要重连设备或重启系统；首次蓝牙配对仍需在 Windows 设置中手动完成。
- **特定蓝牙网卡备份说明**：仓库内的 [`Realtek_Bluetooth_Backup`](Realtek_Bluetooth_Backup/README.md) 仅对应特定主机搭载的 Realtek RTL8851BU（`USB\VID_0BDA&PID_B851&MI_00`）蓝牙适配器备份，**不会自动安装到其他电脑**。
- **三指拖拽与系统手势**：可选的三指拖拽应用（`ThreeFingersDragOnWindows`）需通过 `-EnableThreeFingerDrag` 额外启用；三指/四指滑动手势映射由 Windows 11 原生精确式触控板系统保留。

---

## 源码架构与本地构建 (Architecture & Self-Test)

```powershell
# 编译 MagicForceClick.exe 并运行 59 项行为测试 + 5 项集成测试
.\Build.ps1

# 运行 5 组升级与快照回退验证（真实程序运行中还会额外验证启动失败自动恢复）
.\tests\Test-UpdateRollback.ps1
```

| 文件 / 目录 | 职责说明 |
|---|---|
| [`MagicForceClick.cs`](MagicForceClick.cs) | Win32 RawInput 注册、HID 报文读取、EDID/DPI 多屏物理补偿、`SendInput` 虚拟桌面坐标提交与常驻主循环 |
| [`MagicForceClick.Core.cs`](MagicForceClick.Core.cs) | 可完全离线测试的 `ScanTime` 计时器、S 型加速曲线、双路径按压/拖拽互斥状态机与预览路由决策 |
| [`MagicForceClick.Preview.cs`](MagicForceClick.Preview.cs) | 前台窗口、线程焦点控件类名与修饰键安全复核（Explorer / 桌面 / Directory Opus 13 路由） |
| [`MagicForceClick.Settings.cs`](MagicForceClick.Settings.cs) | WinForms 手感与功能开关图形配置窗口（含 `v1.1 手感`、`轻压跨屏`、`仅保留深按` 预设按钮） |
| [`Build.ps1`](Build.ps1) & [`tools/Package_Release.py`](tools/Package_Release.py) | 使用系统内置 .NET Framework 4.x 编译器构建并执行测试套件，校验 `SHA256SUMS.json` 并打包 Release 资产 |
| [`.github/workflows/`](.github/workflows/release.yml) | Windows CI 构建测试流水线；当 `main` 分支的 `RELEASE_VERSION` 变更或手动触发时，自动完成构建、校验并发布 4 份 Release 压缩包与哈希清单 |
| [`README_AI配置指南.md`](README_AI配置指南.md) | 供 AI 编程助手（Claude Code / Codex / Antigravity / Cursor 等）快速理解工程约束与自动化排障的详细指南 |

> [!NOTE]
> 自动化测试覆盖计时回绕、蓝牙式批量到达、重复/旧帧、松手与重连、普通/深按、低压力开始拖动、多指取消、配置热加载、无效配置保护、文件哈希校验与失败恢复。实际蓝牙/USB 输入、睡眠恢复和不同使用者的压力手感仍需实机验收。

---

## 依赖与许可 (Credits & License)

- **底层签名驱动**：来自 [mac-precision-touchpad](https://github.com/imbushuo/mac-precision-touchpad) 发行版（作者：Bingxing Wang、Vito Plantamura）。
- **快速文件预览**：Explorer / 桌面预览依赖 [QuickLook](https://github.com/QL-Win/QuickLook)；Directory Opus 13 预览直接对接 [GP Software Directory Opus](https://www.gpsoft.com.au/) 内置文件列表空格预览。
- **开源许可**：本项目工具源码与自动化脚本采用 **[MIT License](LICENSE)** 许可；随包分发的第三方二进制与驱动组件保留各自原始许可。
