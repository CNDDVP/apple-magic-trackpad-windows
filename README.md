# Apple Magic Trackpad Windows Suite 1.1.1

Apple USB-C / Lightning 妙控板在 Windows 11 上的签名驱动、原生手势设置、深按文件预览及维护工具。底层驱动仍为发行包 v2.2 / INF `2026.3982.1.1000`；本页的 **1.1.1 是工具套件版本**。

## 1.1.1 的修复与可选手感

- **Directory Opus 13 深按预览**：识别 `dopus.exe` 的 Lister 文件列表，调用现有的空格预览动作。重命名框、地址栏、搜索框、嵌入式预览器、对话框及按住修饰键时均不发空格；等待期间焦点改变也会取消。用户已在 Opus 13.25 x64 / 蓝牙 A3120 上确认深按可预览。
- **“轻压跨屏”预设**：压力阈值90、稳定按压180ms，还需比本次物理按下时增加至少15的压力；只保持普通按压不会变成长按预览。达到180的明确深按保留即时响应。最终温和曲线为慢速增益0.95、最大增益3.5、介入速度3.8、指数2.0；用户已确认预览省力、跨屏速度合适、定位和连续移动正常。
- **低压力下的拖动保护**：从物理按下时锚定位置和初始力度，中途压力回落重新计时，移动/多指/接触更换仍锁定取消。阈值低于120必须同时启用至少120ms稳定判断和10的额外加力要求；默认180/0ms配置保持兼容。
- **多屏位置补偿**：通过Windows `SendInput` 的绝对虚拟桌面坐标提交，处理负坐标、屏幕间空隙和像素中心映射；不再直接用`SetCursorPos`改位置。运行状态记录输入提交失败次数。
- **保留个人设置**：升级仍保留已有JSON，发布默认值也沿用v1.1参数。需要轻压方案时，打开设置，点“轻压跨屏”后保存；“v1.1 手感”可恢复原参数。

预览在资源管理器/桌面使用QuickLook，在Directory Opus文件列表使用现有Space操作。Opus中须先配置好Space可以预览。没有新增独立第二次震动。

## 1.1.0 的三项改进

- **按设备扫描时间计算速度**：使用112字节、ReportID=0x05报文的ScanTime（100µs/计数，16位回绕），不再用处理消息的间隔估算速度。批量到达、重复帧、计数重启、接触ID切换和长时间停顿均有独立处理。
- **拖拽与深按互斥**：从物理按下开始记录起点，不受普通按压力度低于110影响。手指移动超限、光标移动超限、多指或接触更换后，本次按压锁定为拖拽/取消预览，直到松开。异步预览还检查取消标记与前台窗口是否变化。
- **独立开关与手感面板**：深按预览、S型加速、多屏补偿可独立关闭；可调慢/快增益、介入速度、曲线指数、压力阈值与拖拽保护。保存约1秒生效，无效编辑保留上一次有效配置。

默认沿用原有曲线 `0.95 / 3.40 / 4.5 / 1.85`、压力阈值180、Windows速度14。S型增益叠加在Windows输出上，并非替换Windows加速。多屏补偿仍沿用此前实现。没有新增独立的第二次震动。

## 已经配置好这台电脑：只升级工具

从 [Releases](https://github.com/CNDDVP/apple-magic-trackpad-windows/releases) 下载更新包，解压后在当前登录用户的PowerShell执行：

```powershell
.\Update_MagicForceClick.ps1
# 也可显式指定正在使用的程序目录
.\Update_MagicForceClick.ps1 -RuntimeDir 'C:\实际目录\MagicTrackpad'
```

更新器从当前用户启动项识别现有路径，先校验配置、备份、停止并释放旧进程，再切换文件。现有JSON设置保留。新建启动项不再携带固定180参数，因此设置面板中的压力阈值可以生效；旧版位置参数仍兼容。

**此工具升级不安装驱动，不改Windows速度、蓝牙配对、QuickLook或维护任务。** 安装失败会自动还原本次快照内的文件与启动项。

设置入口：双击运行目录的 `Open_MagicTrackpad_Settings.cmd`，或运行 `MagicForceClick.exe --settings`。

## 回退到升级前版本

升级器输出 `SnapshotDir`，其中包含文件的原有/不存在状态、SHA256、原启动项和独立恢复脚本。请保留整个 `rollback-snapshots` 文件夹。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '实际SnapshotDir\Restore_MagicForceClick.ps1' -SnapshotDir '实际SnapshotDir'
```

恢复先检查备份哈希和路径，再停止程序、还原原文件、移除本次新增文件、恢复启动项，必要时重启原程序。可重复执行。不要用 `-DoNotStart`，除非希望恢复后保持关闭。

此次发布的基线是提交 [`2594280`](https://github.com/CNDDVP/apple-magic-trackpad-windows/commit/2594280052ed327c5a328f9b0e3d38f2ec7545ce)。Release另附该提交的原程序和源码；本地升级快照用于恢复个人配置。恢复脚本不能恢复被外部删除/损坏的备份，也不撤销用户之后另行安装的驱动或修改。

## 新电脑完整安装

Windows 11 24H2 / Build 26100+，64位PowerShell；使用当前交互用户的管理员权限，不要切换到另一个管理员账户。

```powershell
git clone https://github.com/CNDDVP/apple-magic-trackpad-windows.git
cd apple-magic-trackpad-windows
.\Install_All_MagicTrackpad.ps1 -CursorSpeed 14 -ForceClickThreshold 180
# 直接使用实机校准的轻压与温和跨屏方案
.\Install_All_MagicTrackpad.ps1 -CursorSpeed 14 -LightCrossScreen
```

主脚本合并两个QuickLook分卷并验证SHA256，安装对应架构的Apple驱动，配置USB/蓝牙两分支、原生触控板参数、QuickLook、自启和维护任务。驱动安装可能需要重连或重启；首次蓝牙配对仍需手动完成。

`Realtek_Bluetooth_Backup` 仅对应本机RTL8851BU `USB\VID_0BDA&PID_B851&MI_00`，不会自动安装到其他电脑。可选三指拖拽应用需额外配置；三/四指滑动手势映射由系统保留。

## 配置与自测

- `MagicForceClick.json`：版本化配置。`EnableForceClick` / `EnableAcceleration` / `EnableMonitorCompensation` 为独立开关，`AccelerateWhilePressed` 可关闭按住时的额外S型加速。
- `MagicForceClick.status.json`：当前生效设置、RawInput注册、有效/重复扫描帧和计时重置统计。它是诊断状态，不作为设备实际手势正常的唯一证据。
- `MagicForceClick.Core.cs`：可离线测试的扫描时间、曲线、按压状态机与预览路由；`MagicForceClick.Preview.cs`：前台窗口/焦点检查；`MagicForceClick.Settings.cs`：设置窗口。
- `Build.ps1`：用.NET Framework编译器构建程序并运行59项行为测试、5项配置/界面/输入结构集成测试。`tests/Test-UpdateRollback.ps1`另验证更新与回退；真实程序已运行时还测试启动失败自动恢复，总计5项。
- `.github/workflows/build.yml`：Windows构建和行为/集成测试；main的`RELEASE_VERSION`变更或手动发布Release后，`release.yml`先构建/测试/校验，再创建Release并上传更新包、完整包、两份基线包和校验清单。
- `SHA256SUMS.json`：自动生成的发布文件大小与SHA256；不包含自身、构建缓存或个人回退快照。

```powershell
.\Build.ps1
```

测试覆盖计时回绕、蓝牙式批量到达、重复/旧帧、松手与重连、普通/深按、低压力开始拖动、多指取消、配置热加载、无效配置保护、文件校验与失败恢复。实际蓝牙/USB输入、睡眠恢复和不同使用者的压力手感仍需实机验收，测试不会伪称提供第二次震动。

## 依赖与许可

签名驱动来自 [mac-precision-touchpad](https://github.com/imbushuo/mac-precision-touchpad) 的发行版（Bingxing Wang、Vito Plantamura）；预览依赖 [QuickLook](https://github.com/QL-Win/QuickLook)。本项目工具源码采用MIT许可；打包依赖保留各自许可。
