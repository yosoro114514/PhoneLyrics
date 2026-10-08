# 参与开发

请先阅读 README 的连接条件和验证范围。PhoneLyrics 当前只使用 AMS 作为手机媒体后端，任务栏 UI Automation 仅用于控件几何测量。

## 本地检查

在 Windows 安装 .NET 10 SDK 后执行：

```powershell
dotnet build PhoneLyrics.slnx -c Release
dotnet run --project tests/PhoneLyrics.Tests -c Release --no-build
.\scripts\Publish-Portable.ps1 -OutputDirectory .\artifacts\review
```

使用 `overlay --demo --mode floating` 可以检查界面，不连接手机。29 项逻辑检查也不访问蓝牙。真实手机、Explorer 重启和 DPI 行为需要单独验证；请区分合成检查、实际观测与手机侧确认。

## 问题报告

说明 Windows 版本、iOS 版本、播放器、蓝牙适配器、显示模式、DPI、复现步骤和实际结果。位置问题请附手机整秒显示与程序读数；控制问题请说明手机是否真的响应。

日志可通过托盘打开，可能包含设备名称和媒体文字。分享前移除设备名、蓝牙标识、个人文件路径和不相关内容。原始日志与本机设置不需要提交到源码仓库。

## 修改原则

- 保持手机继续播放、电脑只显示与遥控的产品范围。
- 确保目标设备或播放器失效时，控制被拒绝；不要自动切换到其他来源。
- 没有有效位置时显示未知；收到歌词文字不能推导出逐字时间。
- 用事件接收 AMS 与任务栏变化，避免加入定时手机读取。
- 保持 UI 与 BLE 连接分离；显示模式切换复用连接。
- 添加必要的行为检查并记录未测场景，避免把构建通过当成手机测试通过。

修改采用仓库的 MIT 许可证；引入第三方代码时保留其适用的许可证说明。
