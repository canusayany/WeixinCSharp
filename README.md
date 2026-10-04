# WeixinCSharp

用 C# 收发微信助理消息，适合把自己的程序接到扫码绑定的微信机器人上。

这是腾讯公开 iLink 实现的 C# 兼容客户端，参考 [Tencent/openclaw-weixin](https://github.com/Tencent/openclaw-weixin) 的 `2.4.9` 版本。项目包含协议库和 Windows 命令行程序，不是腾讯官方产品，也不是普通个人号、群聊或通讯录接口。

## 现在能做什么

| 功能 | 当前情况 |
| --- | --- |
| 扫码绑定、文字收发 | 已实机验证，只处理绑定者与机器人的消息 |
| Markdown | 按官方规则过滤、分段；标题、加粗、行内代码、列表样式已验证 |
| 文件 | 上传后发送普通文件，已在手机收到并打开 |
| 图片、视频、音频附件 | 提供接口；短视频已在微信电脑版播放，音频按文件附件发送 |
| 正在输入 | 支持开始、定时刷新和停止，已在微信电脑版看到提示 |
| 原生语音气泡 | **尚未通过**。本轮 API 接受，电脑版未观察到新气泡，手机未确认 |
| 媒体接收、SILK 编解码 | 提供下载、解密和本地转换；详细验证范围见记录 |

表中的文字、Markdown、视频、打字状态和文件实机记录来自 1.2.1。1.3.0 已重新完成离线检查，新增实机观察仅确认配套文字可见；原生语音尚未通过。

`Sent` 只表示 API 确认，不代表对方已经看到或播放。具体样本和限制放在 [验证记录](docs/VALIDATION.md)，协议来源、长度限制和封号风险见 [研究报告](docs/REPORT.md)。

## 先跑起来

从 [Releases](https://github.com/canusayany/WeixinCSharp/releases) 下载 Windows x64 成品。1.3.0 起协议、SILK 编解码和项目工具均用 C#，成品只带 .NET 运行时；解压后请保留整个目录。

在程序目录打开 PowerShell：

```powershell
.\weixin.exe login --open
.\weixin.exe listen
```

用手机微信扫描打开的二维码，确认绑定，然后向机器人发一条文字。`listen` 会显示收到的内容，默认不回复。二维码页面在登录结束后清理。

按 `Ctrl+C` 停止监听，再发送消息：

```powershell
.\weixin.exe send
.\weixin.exe send --text-file .\reply.txt
.\weixin.exe status
```

`send` 从终端读取一行文字，`--text-file` 读取 UTF-8 文件。发送需要入站消息提供的 `context_token`；没有上下文时，先从手机发一条消息并运行监听。

想试连续收发，可以显式开启回声：

```powershell
.\weixin.exe listen --echo --typing
```

收到文字后会回复“已收到：…”，回复期间显示打字状态。程序不会执行微信消息里的命令。

## 文件、Markdown 和视频

先停止监听，再运行需要的命令：

```powershell
.\weixin.exe send-media --file .\document.pdf --kind file
.\weixin.exe send-media --file .\demo.mp4 --kind video
.\weixin.exe send-media --file .\photo.jpg --kind image
.\weixin.exe send-media --file .\audio.mp3 --kind audio
.\weixin.exe send --markdown --text-file .\reply.md
.\weixin.exe listen --download-dir .\downloads
```

音频附件需要下载或打开文件，不能当作微信原生语音气泡。原生语音仍在研究中，暂不建议把它作为业务功能。

也直接跑过腾讯原版 Node 2.4.9：默认 MP3 发送显示为附件，底层 VOICE 试发仍未见气泡。OpenClaw 支持 TTS，不代表微信适配器已支持原生语音外发。过程和字段对照见[实验记录](docs/EXPERIMENT-OFFICIAL-NODE.md)；交付程序继续使用纯 C#。

WorkBuddy 5.6.2 安装包也已解包核查：个人微信助理的音频仍走 FILE，未找到原生语音外发实现。原始函数的离线执行和附带 CLI 的核对见[安装包研究](docs/EXPERIMENT-WORKBUDDY-562.md)。

需要恢复同一笔发送时，给命令加上稳定的 `--source-key <64位SHA256>`，并保留原内容。文件命令会在重新上传前检查持久记录：已有 `Sent` 返回原确认，内容变更或 `Unknown` 拒绝自动重发。Markdown 作业也用同一业务键和原文恢复，已确认的分段会跳过。

单独查看打字状态：

```powershell
.\weixin.exe typing --typing-status start --run-for 60
.\weixin.exe typing --typing-status stop
```

生命周期默认每 5 秒刷新一次，60 秒到期；停止时取消在途请求，再尝试发送取消提示。时长是客户端设置，不能当作服务端对显示时间的保证。

## 在 C# 里发送文件

先通过 CLI 完成绑定、接收一条消息并停止监听。新建 .NET 10 Console 项目，引用协议库：

```powershell
dotnet new console -n SendFile --framework net10.0
dotnet add SendFile/SendFile.csproj reference src/Weixin.Protocol/Weixin.Protocol.csproj
```

把 `SendFile/Program.cs` 换成下面的内容。示例只做一笔首次发送，`document:001` 是这笔业务的固定编号；另一笔发送应使用另一个编号。

```csharp
using System.Security.Cryptography;
using System.Text;
using Weixin.Protocol;

var statePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "WeixinCSharp", "binding.dpapi");
var sourceKey = Convert.ToHexString(SHA256.HashData(
    Encoding.UTF8.GetBytes("document:001")));

using var vault = new StateVault(statePath);
using var client = new ILinkClient();
var state = await vault.LoadAsync<BotState>()
    ?? throw new InvalidOperationException("请先通过 CLI 扫码绑定。");
var runner = new PersistentBotRunner(client, vault, state);
runner.ValidateState();
if (state.Outbox.Any(r => r.SourceKey == sourceKey))
    throw new InvalidOperationException("这笔发送已有记录，请先核对状态。");

using var media = new MediaClient(client);
var uploaded = await media.UploadBoundFileAsync(
    state.Session, args[0], MediaKind.File);
var receipt = await runner.SendBoundItemAsync(
    uploaded.ToMessageItem(), sourceKey);
Console.WriteLine($"{receipt.Status}: {receipt.ClientId}");
```

```powershell
dotnet run --project SendFile -- .\document.pdf
```

上传和发送是两个步骤。业务集成需要保护并持久保存上传描述符，它包含媒体密钥；SDK 不替应用保存上传检查点。恢复时先查发送记录，再复用原描述符，不能重新上传后把新描述符当作同一次发送。CLI 已处理上传前的确认检查，更适合直接调用。CDN 上传内部最多尝试 3 次；上传后、发送记录落盘前退出，仍可能留下未发送的上传文件。

文字使用 `SendBoundTextAsync`，Markdown 使用 `SendBoundMarkdownAsync`，视频上传用 `MediaKind.Video`。这些方法都应复用同一个 `PersistentBotRunner` 并顺序调用。更多接口可直接看 [协议库源码](src/Weixin.Protocol) 和 [调用说明](docs/REPORT.md)。

## 状态与限制

默认状态在 `%LOCALAPPDATA%\WeixinCSharp\binding.dpapi`，用当前 Windows 用户的 DPAPI 加密。一份状态同一时间只能由一个进程持有，所以运行 `listen` 时不能另开 `send`。用 `--state <路径>` 可以指定另一份状态，但不要让多个客户端争用同一个机器人 token。

发送中断、超时或未得到明确确认会保留 `Unknown`，不自动重试。先用 `status` 查看，再在微信核对。入站 handler 在崩溃恢复后可能再次执行，应用应按 `InboxEntry.Key` 处理重复业务。

| 项目 | 默认设置与来源 |
| --- | --- |
| 文字分段 | 4000 个 UTF-16 码元，来自官方客户端配置，未证实为服务端字节上限 |
| 媒体上传、下载 | 100 MiB，本地保护；CLI 用 `--max-media-mib` 调整 |
| 原生语音声明时长 | 60 秒，本地保护，且原生发送未通过 |
| 本地语音转换 | 输入、输出各 16 MiB，最长 60 秒，单次超时 30 秒 |

已核对的官方源码没有给出统一的媒体上传大小、语音或视频时长硬上限。这些本地值不代表腾讯配额，也不保证服务器接受。下载文件是明文，DPAPI 只保护绑定和会话状态。

请求字段和默认行为按公开源码实现，不添加本项目的标识，也不做隐藏客户端或绕过风控的处理。公开源码兼容不等于腾讯授权，无法保证零封号或接口永久稳定；出现会话或权限异常时程序会停止请求，原因仍需核实。

## 本地语音转换

`VoiceCodec` 用完整的托管 SILK 编码器生成腾讯格式的 `0x02 + #!SILK_V3`，不启动外部编解码程序。WAV 入口接受 24 kHz、单声道、16 位 PCM；PCM 入口另支持 8/12/16/32/44.1/48 kHz。短输入至少补到 40 ms，尾帧补零，返回的时长包含补齐部分。

```csharp
var codec = new VoiceCodec();
var encoded = await codec.EncodeWaveToSilkAsync(
    await File.ReadAllBytesAsync("voice.wav"));
await File.WriteAllBytesAsync("voice.silk", encoded.Data);
var decoded = await codec.DecodeSilkToWaveAsync(encoded.Data);
await File.WriteAllBytesAsync("voice-decoded.wav", decoded.Data);
```

实验发送使用 `MediaKind.Voice`，将 `encoded.DurationMilliseconds` 传给上传和 `ToMessageItem(duration, 6, 24000, 16)`，再交给 `SendBoundItemAsync`。`6` 是官方定义的 SILK 类型，后两项是该 WAV 源的采样率与位深；输入来源不明时不要猜测这两个字段。**编码正确和 API 接受仍不等于微信显示原生语音气泡**，当前实机结果见验证记录。

1.3.0 删除了旧 `VoiceCodec` 的 Node 路径构造参数及运行时路径选项，原异步编解码方法继续保留。调用方需要重新编译。

## 从源码构建

需要 Windows 和 .NET 10 SDK。

在仓库根目录运行：

```powershell
dotnet restore WeixinAssistant.slnx --locked-mode
dotnet build WeixinAssistant.slnx -c Release --no-restore
dotnet run --project tests/Weixin.Protocol.Tests -c Release --no-build
dotnet run --project src/Weixin.Cli -c Release -- login --open
```

测试项目是 Console 程序，使用 `dotnet run` 执行；`dotnet test` 不会运行这些检查。单元测试和离线进程测试不连接真实微信账号。完整检查包含本地 codec 和发布后的 EXE 测试：

```powershell
dotnet run --project src/Weixin.Tools -- test-all --output artifacts/tests/my-run
```

每次检查使用新的输出目录。构建 Windows x64 成品：

```powershell
dotnet run --project src/Weixin.Tools -- render-report
dotnet run --project src/Weixin.Tools -- package --skip-build
```

完整检查会发布到新的 `artifacts/win-x64-1.3.0`，再实际启动该 EXE 做离线进程测试。已有发布目录会被拒绝复用，重新检查前请将它移到另一处保留。打包命令使用已检查的成品；工具说明见 [Weixin.Tools](src/Weixin.Tools/README.md)。

协议库没有第三方 NuGet 依赖。CLI 使用 QRCoder 生成二维码；`Weixin.Silk` 随源码带有两个托管组件及原始许可。编码采用完整的 Jitsi/Skype 移植，解码使用经过边界检查修补的 DrAbc 实现。官方 TypeScript/C 研究快照仅供对照，不参与构建或运行。

## 许可

项目自有源码采用 [MIT](LICENSE)。腾讯参考源码、QRCoder、.NET 和语音组件保留各自许可，包括 SILK 编码器的 Apache-2.0 与 Skype BSD-3-Clause-Clear。详见 [第三方来源与许可](THIRD-PARTY-NOTICES.md)。
