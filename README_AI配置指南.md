# Windows 11 苹果妙控板配置与迁移指南（AI可执行版）

> 修订日期：2026-10-22。硬件：本机 USB-C Magic Trackpad A3120 / PID_0324。
> 配置包：当前仓库根目录（或解压后的本地目录）
> 本次修正前完整备份位于配置包上级的 `MagicTrackpad_修正前备份_20261022_173024`。

## 给新电脑 AI 的配置指令

```text
请读取 Apple妙控板Windows完整配置包_MagicTrackpad 中的 README_AI配置指南.md。
先核对 Windows 版本、系统架构、妙控板型号和蓝牙适配器驱动，使用当前已登录用户的管理员权限运行 Install_All_MagicTrackpad.ps1；不要切换到另一个管理员账户。
默认：光标速度14、轻点点击开、轻点两下拖拽关、双指滚动与缩放开、IgnoreButtonFinger=0同时写入USB和蓝牙分支；保留三/四指手势映射。
原生参数必须通过 SPI_GET/SETTOUCHPADPARAMETERS 读取、修改、回读。TapAndDrag 是 userFlags 的 0x08 位，双指滚动是0x80位，缩放是0x100位。不能用固定整组flags覆盖其他选项。
安装后核对驱动、任务触发器、自启项、预览程序、AutoFix日志与Install.result.json。首次蓝牙配对由用户完成。已存在的QuickLook将先备份后部署，脚本不会直接删除旧用户数据。
不要自动安装 Realtek_Bluetooth_Backup；它只适用于本机硬件USB\VID_0BDA&PID_B851&MI_00，其他电脑使用自己的蓝牙驱动。
最后请用户分别测试移动、点击、滚动、按住拖拽、深按文件预览、三/四指手势，以及USB和蓝牙两种连接方式；实际使用确认前不能宣称全部生效。
```

## 使用条件与入口

- 需要 Windows 11 24H2 或更新版本，使用64位 PowerShell。当前已验证的电脑为 AMD64；包中包含ARM64驱动，但未在ARM64电脑上完成部署验证。
- 双击 `一键配置妙控板(管理员运行).cmd`，或以当前用户的管理员 PowerShell 运行 `Install_All_MagicTrackpad.ps1`。
- 可选参数：`-CursorSpeed 14`、`-ForceClickThreshold 180`、`-EnableThreeFingerDrag`。三指拖拽应用安装后仍需打开应用配置，不能与三指滑动切换窗口混为一谈。
- 部署会安装驱动、调整参数、部署QuickLook、更新自启与计划任务。已有文件、注册表快照与原任务先备份到 `%LOCALAPPDATA%\MagicTrackpad_Backups\时间戳`。耗时取决于解压和安装，驱动可能要求重新连接或重启。
- 日志和结果：备份目录中的 `Install.log`、`Install.result.json`。安装脚本遇到关键失败会停止，不再无条件打印“全部立即生效”。

## 文件清单

| 文件 | 作用 |
|---|---|
| `AMD64/`、`ARM64/` | v2.2发行包中的Apple妙控板驱动；INF版本 `2026.3982.1.1000`，日期 `10/11/2026`，提供商 `Bingxing Wang, Vito Plantamura`。目录签名发布者为 Microsoft Windows Hardware Compatibility Publisher；不据此额外宣称WHQL测试结果。 |
| `Install_All_MagicTrackpad.ps1` | 带备份、退出码检查、设置回读和部署结果报告的安装脚本。 |
| `Set_MagicTrackpad_UserSettings.ps1` | 当前用户原生触控板参数配置。`-ReadOnly`仅读取；默认速度14，关闭轻点拖拽，保留无关设置。 |
| `Register_MagicTrackpad_AutoFix.ps1` | 注册登录、每15分钟和系统恢复事件三个触发器；需要管理员。 |
| `MagicTrackpad_AutoFix.ps1`、`.vbs` | 当前设备电源参数维护、两分支按压拖拽参数维护及驱动配置重载；结果写入 `MagicTrackpad_AutoFix.last.json`。 |
| `AmtPtpControlPanel.exe` | 驱动配置面板，可调按压力度与静音点击。 |
| `MagicForceClick.cs`、`.exe` | 二段重压预览（默认阈值180）+ 多屏物理速度对齐（PPI等速补偿）+ 类macOS S型动态加速曲线守护进程；不支持独立第二次马达震动。 |
| `QuickLook_Portable_Clean.zip` | 修复目录结构的便携备份；20个插件目录、21个插件组件DLL（PDF目录另附HtmlViewer组件）。已删除186个WebView2数据/缓存文件，并去除1312条内容相同的重复归档记录；保留1346个唯一文件。 |
| `Realtek_Bluetooth_Backup/` | 本机RTL8851BU蓝牙驱动1.10.1061.3000离线备份，主脚本不会自动安装。 |
| `ThreeFingersDragOnWindows_2.0.7_x64.Msix` | 可选三指拖拽应用，备用，不表示当前已启用。 |
| `SHA256SUMS.json` | 包内文件校验清单（不包含自身）。 |

## 驱动参数与USB/蓝牙分支

蓝牙：`HKLM\SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters`

USB：`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters`

| DWORD | 目标值 | 作用 |
|---|---:|---|
| IgnoreButtonFinger | 0 | 允许物理按住的手指继续移动，支持单指按压拖拽。不能笼统说所有驱动出厂值都是1。 |
| IgnoreNearFingers / PalmRejection | 1 / 1 | 驱动防误触参数。 |
| ButtonDisabled | 0 | 允许按压点击。 |
| FeedbackClick / FeedbackRelease | 394775 / 20 | 本机沿用的按压、释放反馈参数。 |
| StopPressure / StopSize | 0 / 4294967295 | 本机沿用的驱动参数。 |

重载：蓝牙控制设备 `\\.\AmtPtpControlDeviceUm`，IOCTL `0x00222000`；USB命名管道 `AmtPtpControlPanelUsbUmInterface`，写入字节0并等回复。未连接的分支可能不可用，日志会记录。

AutoFix仅对当前存在的目标Apple设备应用维护。MI_00只在实际ProblemCode=10时禁用，不能把所有非OK状态一概禁用，也不能说所有电脑都必须禁用MI_00。

## Windows手势参数：正确位掩码与速度

`userFlags`是原生结构中一组用户设置的位域，不是整个触控板开关集合：

| 设置 | 掩码 |
|---|---:|
| 外接鼠标时仍工作 | 0x01 |
| 单指轻点 | 0x04 |
| 轻点两下后拖拽 | **0x08** |
| 双指轻点 | 0x10 |
| 双指滚动 | **0x80** |
| 双指缩放 | **0x100** |
| 反向滚动 | 0x200 |

正确关闭轻点拖拽：读取现有结构，执行 `p.userFlags &= ~0x08u`，调用设置接口并回读。其他选项不变时，447清除0x08后为439，**不是319**。不得清除0x80，否则会关闭双指滚动。

目标速度为14（合法范围1～20），保留单指轻点、双指轻点右键、双指滚动与缩放。关闭轻点拖拽是为了减少误拖动；单指物理按压拖拽继续保留。没有测量证据证明固定减少150～200ms或实现零延迟。

注册表路径：`HKCU\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad`。保存值和API回读须分开检查，不能只凭注册表写入成功认定运行效果。

原生结构说明：https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-touchpad_parameters_v1

## 重压预览、多屏等速补偿与类 macOS S型动态加速曲线

`MagicForceClick`（开启 `PerMonitorV2` 物理像素感知）监听 112 字节、`ReportID=0x05` 的原始 HID 触控输入：
1. **三屏物理速度自动对齐（PPI 等速补偿）**：启动及分辨率变化（`WM_DISPLAYCHANGE`）时通过 `EnumDisplaySettings` 读取各显示器物理像素边界与物理宽度，以 32 寸 4K 主屏（`54.08 px/cm`）为 `1.000x` 基准，对右屏 24 寸 2K（`48.30 px/cm`）施加 `0.893x` 补偿、对左屏 24 寸 1080P（`36.23 px/cm`）施加 `0.670x` 反向阻尼补偿，使手指在妙控板上滑动 1cm 在不同分辨率屏幕上的物理移动厘米数一致。
2. **类 macOS S型动态加速曲线**：在单指滑动（`contactCount == 1 && tipSwitch`）时，根据触控板表面 16 位坐标与高精度时间戳计算手指物理滑动速率 $v$（`units/ms`），按平滑 S 型曲线 $G(v) = 0.95 + (3.40 - 0.95) \cdot \frac{(v/4.5)^{1.85}}{1+(v/4.5)^{1.85}}$ 调节增益（慢滑微操 `0.95x` 精准不拖沓，中常速 `1.4x~2.2x` 轻快跟手，快甩跨屏最高 `3.40x`），并带亚像素余数累积（`_remX/_remY`）；双指滚动与三/四指手势完全不受影响。
3. **二段式重压预览**：默认压力阈值 180、单触点、位移小于 220。此前本机普通按压峰值 65～115，深按 216～253，这些是该次用户校准数据，换人/设备可能需要重新校准。

触发时释放左键，通过 `QuickLook.App.Pipe.<当前用户SID>` 发送 `QuickLook.App.PipeMessages.Toggle||`，失败时回退为空格。只在桌面、资源管理器和QuickLook窗口处理预览。可检查程序同目录 `MagicForceClick.log`。

此前用户已确认：蓝牙移动/点击/双指滚动、单指按住拖拽、深按文件预览、三指上滑任务视图、下滑桌面、左右切应用、四指左右切虚拟桌面可用；关闭轻点拖拽后误拖动改善。

限制：没有独立第二次震动；USB分支参数已同步，但有线实际操作和睡眠恢复仍需分别测试。三/四指手势的系统映射由本脚本保留，不主动覆盖。

## 自启与维护任务验收

当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`：
- `QuickLook`：`"实际路径\QuickLook.exe" /autorun`
- `MagicForceClick`：`"实际路径\MagicForceClick.exe" 180`

计划任务 `\MagicTrackpad_AutoFix`：最高权限、当前用户交互会话、登录触发、每15分钟，以及System日志中Power-Troubleshooter/EventID=1恢复事件。该事件是否在特定睡眠模式产生需要实测；另有定期巡检作为补充。维护日志会记录重载结果，不保证解决所有断连原因。

检查进程正在运行不能代替检查下次登录能否自启；检查任务存在也不能代替睡眠唤醒实测。

## 回退

本次修正前完整归档在上级 `MagicTrackpad_修正前备份_20261022_173024`，含原配置包、笔记、本机AutoFix、触控板/Run注册表快照和原任务XML。安装脚本的每次部署另有独立备份。

需要回退时，先关闭相关程序，按具体失败项还原对应文件/值/任务。注册表快照包含整个键，导入前先比较，避免覆盖之后其他软件的新自启项。恢复触控板设置应同时用Windows设置界面或原生API应用并回读。驱动安装不因还原脚本而自动卸载，应依据实际设备状态单独处理。

## 2026-10-22 本次修正与验收记录

- 本机API独立进程回读：速度14、userFlags=439、轻点拖拽关、双指滚动和缩放开；注册表与API一致。USB和蓝牙IgnoreButtonFinger均为0。
- 已补齐当前用户Run下的QuickLook与MagicForceClick启动项；两程序原有进程继续运行。下次登录自启还需实际登录验证。
- 维护任务已增加恢复事件触发器。VBS启动器等待PowerShell完成并返回退出码，任务成功状态不再只表示启动器已发出命令。
- 蓝牙重载IOCTL成功。USB未连接时管道不可用会写入日志，不作为USB分支配置丢失的证据。
- 未重新安装正在正常工作的Apple或Realtek驱动；未在本机执行整套安装流程。部署脚本已做语法、编译及关键分支检查，新电脑端到端迁移、USB实操和真实睡眠恢复仍需实测。


## 工具套件1.1.0更新（相对2594280基线）

现有电脑使用Update_MagicForceClick.ps1进行应用升级，不应为本次更新重装驱动或重新配对蓝牙。新增Core/Settings源码、MagicForceClick.json、设置入口、独立更新和恢复脚本。原有曲线与180阈值作为默认配置保留；取消启动项中的固定180，使后续JSON调节生效。

蓝牙速度使用ScanTime的100µs单位和16位模差，不以接收时间参与速度计算；接收时间只用于长时间无输入后的重置。拖拽保护从物理按下开始，移动、多指、接触更换和配置重载后锁定取消，直到完全松开。

更新前必须保留SnapshotDir输出。回退使用该目录独立Restore_MagicForceClick.ps1，可恢复原文件字节、原启动项，并移除新增文件。不要删除回退快照。完整源代码基线提交2594280052ed327c5a328f9b0e3d38f2ec7545ce；当前工具版本1.1.0与驱动v2.2是不同版本体系。

当前生效配置和计时统计读取MagicForceClick.status.json；设置窗口通过Open_MagicTrackpad_Settings.cmd打开。无效JSON保留运行中的上一份有效配置。1.1.0的Build.ps1运行39项纯逻辑与4项集成测试，另有5项更新回退测试；软件测试和用户真实手势验收必须分开报告。

## 工具套件1.1.1：最终校准与防回跳

新增MagicForceClick.Preview.cs，编译、完整安装和独立升级都必须包含它。Opus仅在dopus.exe、dopus.lister窗口、dopus.filedisplay/dopus.iconfiledisplay焦点中转发已有Space预览，发送前复核窗口/焦点/修饰键；不向输入框、预览器、对话框发空格。

最终“轻压跨屏”预设：ForceClickThreshold=90、ForceClickDwellMs=180、ForceClickPressureRise=15、ForceClickImmediateThreshold=180；MinGain=0.95、MaxGain=3.5、MidSpeed=3.8、CurvePower=2.0。普通按下后仍需增加一点力度，不是长按即预览。双路径都受原拖动互斥保护。低于120的阈值必须配合至少120ms稳定判断和10的额外力度，不能只降低压力值。

多屏补偿经Windows SendInput绝对虚拟桌面坐标提交，处理负坐标、屏幕空隙和裁剪；跟踪提交的目标，不立即用旧GetCursorPos读数回写起点。修复了快甩后下一次移动回原屏幕的问题。59项行为、5项集成、5项更新回退测试通过；实际压力回放2810+1322帧、快甩前后2080/2028帧验证。用户已确认预览省力、普通点击/拖动正常、无跨屏回跳，最终速度和定位合适。USB和睡眠恢复仍需另行实测。

升级保留已有JSON。选择设置面板“轻压跨屏”并保存，或新机完整安装时用 -LightCrossScreen；不要同时传入 -ForceClickThreshold。默认值保留180/0ms/原曲线；现有JSON未显式指定压力时不被完整安装覆盖。发布前的1.1.0和2594280基线都保留，个人SnapshotDir不能删除。