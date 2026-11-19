# netmusic-tile

在 Windows 11 的**开始菜单旁边**叠一块网易云音乐控制面板 —— 真封面、歌名、歌手，三个按钮**真的能控歌**。

打开开始菜单 → 面板出现在右下角；关掉开始菜单 → 面板消失。它不抢焦点、不挡鼠标，平时就是个不存在的东西。

![面板预览](docs/preview.png)

*(截图里的封面是合成的示例图，不是任何专辑的美术作品。)*

---

## 为什么做这个

Windows 11 没了 Win10 的动态磁贴，用 Start11 之类的工具也补不回「开始菜单上有一块活的音乐磁贴」。
于是换个思路：**不要磁贴，就在开始菜单那块空白上叠一个自己的窗口**，用系统的媒体接口
（SMTC）读歌、发控制指令。

它不依赖任何播放器 SDK，也不模拟按键 —— 只要播放器注册了系统媒体会话（网易云、Spotify、
浏览器里的音乐页面都算），它就能读写。

---

## 安装

需要 **Windows 10 1809+ / Windows 11** 和 **.NET 6 桌面运行时**（SDK 也行）。

```
1. 双击 build.cmd      构建并打包到 release\netmusic-tile
2. 双击 install.cmd    复制到 C:\netmusic-tile、设开机自启、启动
```

`install.cmd` 支持指定目录：`install.cmd D:\start11-music`。

**卸载**：先运行 `remove-autostart.cmd`（删掉自启快捷方式并停止程序），再手动删掉那个文件夹。
不改注册表、不装服务。

> ⚠️ **别在受限目录里直接跑 `app\bin\...\netmusic-tile.exe`。**
> 从某些位置（例如受完整性策略约束的目录）启动时，**托盘图标会被 shell 拒绝注册**
> （`Shell_NotifyIcon` 返回 `False`，`GetLastError = 5`）。同一个 exe 复制到普通目录就正常。
> 装到 `C:\netmusic-tile` 就是为了避开这个。

---

## 怎么用

| 操作 | 结果 |
|---|---|
| **打开开始菜单** | 面板出现（默认位置在右下角） |
| **关掉开始菜单** | 面板消失 |
| **右键面板** | **收起** —— 以后开菜单不再出现，**程序继续运行** |
| **再运行一次 exe** | **恢复**（在「收起 / 恢复」之间切换） |
| **`Ctrl` + 右键面板** | **退出程序** |
| **托盘图标**（紫音符） | 双击 = 显示/隐藏；右键 = 菜单（含退出） |

按钮：`⏮ 上一首`、`▶/⏸ 播放暂停`、`⏭ 下一首`。

---

## 调整位置和大小 ← 重点

面板位置用**物理像素**表示，而且只在开始菜单打开时才可见 —— 所以**别手工试坐标，用拖动工具**：

```
双击 position.cmd
```

会弹出一个和面板同样大小的框，直接拖：

| 操作 | 作用 |
|---|---|
| **拖动框体** | 移动 |
| **拖右下角** | 改变大小（保持正方形） |
| **方向键** | 微调 1 像素（按住 `Shift` = 10 像素） |
| **`Enter` / 保存** | 写入 `config.json` |
| **`Esc` / 取消** | 不保存，退出 |

**保存后面板立刻就跟过去了** —— 程序每秒检查 `config.json` 的时间戳，变了就重读并重新定位，
不需要重启。

也可以直接给数字（脚本、或者你已经有想要的值）：

```
netmusic-tile.exe --set 1995,1465,303,303
```

---

## 配置

`config.json` 和 exe 放在同一个文件夹，**存盘即生效**：

```jsonc
{
  "x": 1995, "y": 1465, "w": 303, "h": 303,       // 物理像素。4K@150% 下 (303,303) ≈ 逻辑 202×202
  "pollMenuMs": 120,          // 越小菜单响应越跟手（但更耗 CPU）
  "pollSongMs": 1000,         // 读歌名/封面的间隔
  "menuCloseDebounceMs": 0,   // 关菜单延迟；0 = 立刻消失
  "menuOpenDebounceMs": 120,  // 开菜单防抖（避开菜单动画）
  "menuWindowClass": "DV2ControlHost",   // 开始菜单窗口的类名
  "menuWindowTitle": "Start menu",
  "playerAppId": "cloudmusic" // SMTC 会话 appId 的子串（网易云 = cloudmusic.exe）
}
```

**换了别的播放器**：把 `playerAppId` 改成它的 appId 子串（例如 Spotify 的 `Spotify.exe`）。
没匹配到时会退回到「系统当前媒体会话」，所以浏览器里放视频也会被它接住 —— 想要只认某个播放器，
就把 `playerAppId` 写准。

---

## 命令行

```
netmusic-tile.exe                    正常运行（有个互斥体，重复启动 = 切换显示/收起）
netmusic-tile.exe --calibrate        打开位置/大小调整框
netmusic-tile.exe --set X,Y,W,H      直接写入位置和尺寸后退出
```

---

## 工作原理

三件事，都不难，但每件都有坑：

**1. 读歌、控歌 —— 用 SMTC（系统媒体传输控件）**

`GlobalSystemMediaTransportControlsSessionManager` 能拿到所有注册了媒体会话的播放器：
歌名、歌手、封面缩略图、播放状态，以及 `TryPlayAsync / TryPauseAsync / TrySkipNextAsync`。
不用抓窗口标题，也不用 `SendInput` 模拟按键（试过，在这台机器上跨进程合成输入会被拒）。

**2. 知道开始菜单开没开**

找以 `DV2ControlHost` / `Start menu` 为标识的窗口，判据是
`IsWindowVisible && 没被 DWM 遮蔽(cloaked) && 矩形在显示器内` —— **不是**比较坐标。
Start11 也能用，因为它复用同一个宿主窗口；如果哪天类名变了，改 `config.json` 即可。

**3. 叠在桌面上但不抢焦点**

`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`，并把 `WM_MOUSEACTIVATE`
回 `MA_NOACTIVATE`；鼠标不在面板上时再加 `WS_EX_TRANSPARENT | WS_EX_LAYERED` 让它**穿透**，
所以点旁边的图标不会点到它。位置用 `SetWindowPos` 按物理像素设，配合清单里的
`PerMonitorV2`。

---

## 已知限制 / 故障排查

### 面板显示「(SMTC 无响应)」

**这是系统媒体服务卡死了，不是面板的错。** 判据：程序日志里写着
`SessionManager.RequestAsync did not respond within 3000 ms`。

实测过一次：某杀毒软件 hook 了媒体服务，`RequestAsync` 永久挂起（两个独立程序都超时），
**卸载它之后 13 毫秒就恢复了**。重启 explorer、重启播放器都救不回来。

**处理**：排查杀毒软件/系统 hook 类软件 → 重启 explorer → 最后重启系统。

> 为什么面板要显示这句话：早期版本没有超时，服务一挂整个面板**静默冻死** —— 按钮点了没反应，
> 日志一条都不写（日志写在调用返回之后）。现在每个跨进程调用都有 deadline，
> 失败会**说出来**，而不是装死。

### 面板不显示

1. **播放器在放吗** —— 没有媒体会话时面板是空的，这是正常的
2. 面板被**收起**了？再运行一次 exe
3. 开始菜单是用 Start11 吗 —— 类名不同的话改 `menuWindowClass`

### 封面糊

系统媒体缩略图只有 **162×162**（网易云只发布这么大），而面板通常要 303 像素以上，
所以是约 1.9 倍放大，**看起来偏软不是布局问题**（换成一张 1500×1500 的图就锐利）。

试过从网易云的 Chromium 缓存里捞全分辨率封面 —— 缓存里**确实有** 1500×1500 的图，
但**没有「哪张属于哪首歌」的索引**，用当前缩略图当指纹比对 284 张候选，最佳差异 53（真匹配应 <10），
即当前曲的高清图不在缓存里。**这条路已经放弃。**

### 托盘图标不出现

见上面「安装」里的那条 ⚠️ —— **换个普通目录装**。

---

## 构建

```
build.cmd
```

= `dotnet build app\NetMusicTile.csproj -c Release` + 打包到 `release\netmusic-tile`。

如果仓库里有 `.nuget\` 离线缓存，`build.cmd` 会自动用它（不需要联网）。

开发用的小工具：

| 工具 | 用途 |
|---|---|
| `tools\panelpreview` | **离屏渲染真实的 `MainWindow.xaml` 成 PNG** —— 改布局先看图，还能提前撞出 XAML 错误 |
| `tools\genicon` | 重新生成 `app.ico`（紫底白音符，7 个尺寸） |
| `tools\smtc-probe.ps1` | 独立探针：直接问 SMTC「有哪些会话、卡不卡」（排障用，带 15 秒超时） |

---

## 目录

```
app\                 WPF 程序
  MainWindow.xaml    面板界面（封面底图 + 渐变 + 文字/按钮）
  MainWindow.xaml.cs 菜单跟随、定位、穿透、托盘、按钮
  CalibrateWindow.*  位置/大小调整框
  SmtcClient.cs      媒体会话读取与控制（全部带超时）
  Config.cs / config.json
build.cmd           构建 + 打包
install.cmd         安装（复制、自启、启动）
position.cmd        打开位置调整框
remove-autostart.cmd
tools\              开发工具（见上）
```

---

## 许可

MIT，见 [LICENSE](LICENSE)。
