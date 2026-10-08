# 便携版随附运行库条款

PhoneLyrics 源码和原创图标采用项目根目录的 MIT 许可。单文件 EXE 同时包含下列 Microsoft 运行库；它们保留各自许可，不因打包而改用 PhoneLyrics 的版权署名。

| 组件 | 当前便携发布版本 | 许可文件及来源 |
| --- | --- | --- |
| Microsoft.NETCore.App.Runtime.win-x64 | 10.0.12 | `dotnet-runtime-LICENSE.txt` 与 `dotnet-runtime-THIRD-PARTY-NOTICES.txt`，复制自对应官方 NuGet 包 |
| Microsoft.WindowsDesktop.App.Runtime.win-x64 | 10.0.12 | `windowsdesktop-runtime-LICENSE.txt`，复制自对应官方 NuGet 包 |
| WinRT.Runtime / C#/WinRT | 2.2.0 | `cswinrt-LICENSE.txt`，来自 [Microsoft CsWinRT 2.2.0.241111.1](https://github.com/microsoft/CsWinRT/blob/2.2.0.241111.1/LICENSE) |
| Microsoft.Windows.SDK.NET.Ref / Windows SDK .NET 投影 | 本机打包使用 10.0.19041.57 | `windows-sdk-LICENSE.rtf`，保留 NuGet 元数据所指向的 [Windows SDK 原始许可](https://aka.ms/WinSDKLicenseURL)；同名 `.txt` 为其纯文本转换 |

Windows SDK .NET 投影版本由构建 SDK 选择，在另一 SDK feature band 下可能不同。升级运行库或 SDK 后应核对实际依赖，并同步随附的原始许可与通知。本目录只用于保留第三方条款，没有分发完整 SDK 或参考项目的可执行程序。

这些文本也嵌入独立 EXE，可在托盘「关于手机歌词 / 参考项目」窗口阅读。
