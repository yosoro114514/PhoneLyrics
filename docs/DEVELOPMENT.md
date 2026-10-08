# 开发说明

## 代码边界

`Program.cs` 选择桌面启动或命令行诊断。桌面入口在 `DesktopApplication`，参数在 `CommandOptions.cs` 中分别解析。非法控制命令及缺失的播放器名称在打开蓝牙会话前被拒绝。

`Core/AmsSession.cs` 持有一台明确选择的已配对 BLE 设备；`AmsUuids` 和 `AmsCommands` 集中描述协议标识。通过 SharedReadAndWrite 打开 AMS，注册 RemoteCommand／EntityUpdate 通知，并按需读取 EntityAttribute。写入由信号量串行化，截断属性读取用版本号避免旧读取覆盖新通知。

`AmsProtocol` 生成不可变快照。Title 可能是变化的歌词，因此不作为稳定曲目标识。队列、播放器、歌手、专辑、时长变化会保守地使旧位置锚点失效；暂停时位置冻结，倒带等状态不进行正向推算。

`Desktop/PhoneOverlay.cs` 持有媒体连接，并驱动桌面和任务栏视图。锁定使用原生鼠标穿透，解锁同时保留快捷键和托盘入口。任务栏模式下控制器窗口隐藏，连接仍由它持有。

`TaskbarHost` 仅管理视图。窗口以 WS_CHILD 挂入 Shell_TrayWnd 或 Shell_SecondaryTrayWnd；`TaskbarLayoutMonitor` 在后台 MTA 线程处理布局事件，主线程应用结果。空位不足或挂靠失败时回退浮窗。几何读取不使用 UIA Name。

`Infrastructure/` 管理可写存储、JSONL 日志、图标资源和按 Windows 用户／桌面会话隔离的单实例。第二次启动只发送界面唤出事件；示例模式可以独立运行，且不连接手机或保存偏好。

早期 GSMTC、手机链接卡片控制和 MCS 探测不在此项目中。手机链接的启动入口只是帮助现有配对建连，不是媒体数据后端。

## 发布

- 默认项目：WPF GUI，程序集与 EXE 名称均为 PhoneLyrics。
- Portable profile：win-x64、SelfContained、PublishSingleFile、原生库自解压、压缩、未裁剪；图标与项目许可证嵌入。对 .NET 与 Windows Desktop FrameworkReference 固定运行库 10.0.12，Windows SDK 投影维持 SDK 自己的版本选择。
- Diagnostics profile：控制台、框架依赖、保留发布目录；适合本地终端输出和 Ctrl＋C。

日常运行仅需要便携 EXE。配置与日志在运行后创建，不应该随发布包携带。发布到一个正在运行的 EXE 路径时，脚本会提示退出或改用其他目录。

`scripts/Package-Release.ps1` 接收已发布的便携目录，核对产品版本、x64 和 GUI 子系统，然后使用明确文件清单生成 ZIP、独立 EXE 与 SHA-256。默认读 `artifacts/portable`；指定不同发布目录时使用 `-PortableDirectory`。随附依赖条款在 `licenses/`，升级运行库时同步更新；发布说明在 `docs/releases/`。没有自动上传、创建标签或 Release 的步骤。

## 无手机预览

```powershell
dotnet run --project src/PhoneLyrics -c Release -- overlay --demo --preview --mode floating --no-hotkey
```

`--demo` 使用原创示例文字与固定演示时间，不操作手机；`--preview` 把歌词与工具条 PNG 写入本次日志目录。图片是仅渲染本程序视图的结果，不截取桌面其他内容。生成图片后可从程序自身退出。

## 当前待解决问题

独立 BLE 冷启动与手动建连条件需要更多适配器／设备验证；掉线目前仅使快照与按钮失效，不自动重连。共享连接的 CCCD 不在退出时禁用，以免影响其他客户端。

任务栏依赖 Windows Shell 的窗口与 UIA 几何结构，系统升级可能改变这些结构。Explorer 重启、显示器插拔、休眠及其他缩放比例的处理已存在，但完整场景测试尚未完成。

字级高亮或完整 LRC 需要另行取得可信歌词时间轴。当前只转显播放器的媒体文字，播放进度也仅展示，不提供 seek。
