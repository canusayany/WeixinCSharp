# 微信助理 C# 实现与语音核查报告

版本：1.3.0。核查日期：2026-10-04，Asia/Shanghai。此前 1.2.1 的完整报告保存在 [历史报告](REPORT-1.2.1.md)。本版把生产 SILK 编解码及构建、测试、报告、打包工具全部迁到 C#，删除 Node/WASM、Python 与 PowerShell 实现。研究目录中的官方 TypeScript/C/C++ 快照是静态依据，不参与运行。

## 官方依据与通信范围

主要依据是腾讯 [openclaw-weixin 2.4.9](https://github.com/Tencent/openclaw-weixin/tree/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c) 及其 [后端协议说明](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/docs/protocol_zh_CN.md)。2026-10-04 重新检查，main 仍为该提交、npm latest 仍为 2.4.9。固定源码、MIT 许可与哈希保留在 research/upstream-snapshot；WorkBuddy 的公开逆向与第三方 demo 只用于机制对照，来源见 research/github-evidence.md。

本程序连接 iLink Bot HTTPS/JSON 通道，仅处理自己扫码绑定的微信账号与机器人。没有普通个人号通讯录、群聊或任意联系人发送功能，也没有读取 WorkBuddy 凭据。扫码授权、长轮询、上下文、CDN 及持久状态都由 C# 实现。

| 环节 | 本版行为与官方依据 |
| --- | --- |
| 登录 | get_bot_qrcode / get_qrcode_status，手机确认绑定 |
| 接收 | getupdates 长轮询，持久游标与 Inbox 记录 |
| 回复 | sendmessage，绑定 to_user_id 与入站 context_token |
| 请求默认值 | 官方 iLink-App-Id、版本、BaseInfo、随机 client_id 行为；不增加本项目的线协议标识 |
| 媒体 | getuploadurl → AES-128-ECB/PKCS#7 加密 → CDN → 消息描述符 |
| 媒体密钥 | 发送使用 Base64(ASCII 十六进制 AES key)，保持公开发送器行为 |
| 恢复 | Sending/Sent/Unknown 持久化；不对 Unknown 自动重发 |

SDK 的上传描述符含媒体密钥，业务应用必须自己保护并保存上传检查点。CLI 在上传前查业务键，已 Sent 返回旧确认，变更内容或 Unknown 停止。CDN 内部最多三次上传尝试；上传后、发送记录落盘前退出仍可能留下一份未发送的上传。

## 文字、Markdown、文件、视频与打字状态

文字使用 SendBoundTextAsync；Markdown 使用移植官方流式过滤器的 SendBoundMarkdownAsync。文件、视频分别通过 MediaKind.File / Video 上传后发送。打字生命周期调用 getconfig 和 sendtyping，默认每五秒刷新、六十秒停止；停止时取消并等待在途请求，再尝试 CANCEL。

1.2.1 已实机观察文字、Markdown、三秒视频播放、打字提示及 file7.txt 的收到/打开，用户确认文件内容正确。这些历史样本见 [1.2.1 验证记录](VALIDATION-1.2.1.md)，不能直接视为新 1.3.0 二进制的实机验收。当前版本的新测试另见 [验证记录](VALIDATION.md)。

## 长度和大小限制

| 项目 | 当前值 | 来源与边界 |
| --- | --- | --- |
| 文字分段 | 4000 UTF-16 码元 | 官方客户端配置；不是已证明的服务端字节限制，完整代理对不跨段 |
| 媒体上传/下载 | 默认 100 MiB | 本地资源保护，CLI 可调；不是腾讯配额 |
| 原生语音声明时长 | 默认最多 60 秒 | 本地保护；未找到已发布的统一服务端硬上限 |
| PCM/SILK 转换输入/输出 | 各默认 16 MiB | 本地资源保护，另有 100 MiB 配置硬保护 |
| 转换时长/超时 | 默认 60 秒 / 30 秒 | 本地逐帧检查；取消和超时均等待实际任务退出 |
| SILK 内部帧 | 20 ms，单包最多 5 帧 | 旧 SILK 解码合同；容器允许最多 1250 字节，当前解码算术缓冲为 1024，超出明确拒绝 |
| WAV 入口 | PCM16、mono、24000 Hz | 当前纯 C# WAV 解析入口；不接受压缩 WAV 或立体声 |
| PCM 入口/解码输出 | 8/12/16/24/32/44.1/48 kHz | 组件支持范围；与服务端接收条件不同 |
| 视频 | 没有额外推定时长上限 | 官方公开实现未给出统一硬上限，只实施媒体大小保护 |

SILK 入站预检用包数×五帧计算最坏输出容量，是保守保护，可能在实际音频较短时提前拒绝。短 PCM 至少补到四十毫秒，尾帧补零；编码返回时长包含补齐部分。原生字段 playtime 的单位是毫秒、sample_rate 为 Hz，不能用 QQ 的本地限制替代微信依据。

## 纯 C# SILK 实现

生产编码使用 [greepar/SilkCodec.NET](https://github.com/greepar/SilkCodec.NET/tree/01e40689c61e399e193524ea83a4f89e27604201) 的完整 Jitsi/Skype 编码器，固定提交 01e40689c61e399e193524ea83a4f89e27604201。引入 126 个底层 C# 文件，未引入 MP3/NLayer/FFmpeg 入口；本地封装为二十毫秒包、25 kbps 目标码率、Tencent 容器，每帧检查取消/超时和输出容量。码率是编码器控制目标，不是每个短文件精确等长的承诺。

解码与容器使用 [DrAbcOfficial/SilkCodec.NET](https://github.com/DrAbcOfficial/SilkCodec.NET/tree/51205c364d685c78e64a0702474718358099caa3) 的 24 个核心 C# 文件并做本地硬化：拒绝截断、零长度、不合法范围码流；限定 CDF、指针、数组和整数边界；超长包不得变成静默 PLC，第五帧仍声明后续帧时明确拒绝。该组件内的简化编码器没有进入生产 VoiceCodec 编码路径。

各操作使用独立 codec state，VoiceCodec 等待工作任务真正结束，不把取消后的计算留在后台。入口副本、临时 PCM、包和单一有界输出缓冲清零；内部 DSP 状态由托管运行时回收，未保证所有分析状态清零。调用者持有自己的输入/结果，下载后的明文媒体应由调用者管理。

没有 Node、WASM、外部 codec DLL 或 FFmpeg 进程。C# 使用的 unsafe 指针属于托管组件内部；Weixin.Tools 只启动 .NET SDK 来构建、测试与发布。

固定合成测试向量包含七种采样率，以及二十至一百毫秒的多帧包。独立编译的未改动 Skype SDK 1.0.9 C Decoder.c 只用于研究参考，不分发、不由日常测试调用。已确定逐字节一致的样本把参考 PCM SHA 固定在 Golden/manifest.json；高采样率重采样样本存在波形差异，只声称对应时长/结构通过。新生产编码的两秒码流也由独立 C 参考解出与 C# 相同的 96000 字节 PCM，不把“有声音”当作正确性标准。

## 原生语音发送核查

腾讯 [VoiceItem 定义](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/types.ts#L116) 中，消息项 VOICE=3、上传 media_type=4、encode_type=6 表示 SILK。可选位深、采样率与时长应反映实际源数据，未知值省略。本版 CLI 提供显式 --voice-sample-rate 和 --voice-bits-per-sample，默认不猜测；SDK 可调用 ToMessageItem(duration, 6, 24000, 16) 来构造本地 24 kHz PCM16 WAV 编码的描述符。

然而正式 [send-media.ts](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/send-media.ts#L60) 的音频路由仍为 FILE，公开上传入口只接入 IMAGE/VIDEO/FILE。[官方 SILK 转换](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/media/silk-transcode.ts) 是入站解码，没有正式出站语音 helper；getconfig 也没有可实现的语音能力开关。

用户提供的 moneyNews 仓库已做一次只读核查，以下结论仅覆盖该仓库当时的 QQ 路径：通过 QQBot 富媒体上传返回 file_info，再发送富媒体消息；MP3/WAV/SILK 直接交给该接口，没有 PCM→SILK 编码器，其他格式转换需要外部 FFmpeg。这里只独立借鉴格式检查、上传与发送分步及持久记录。仓库未提供许可，本项目未复制其私有源码，也没有把 QQ 的 file_type/msg_type 套到微信。

历史完整元数据 SILK、真实手机语音重新上传及原描述符回发都出现过 API 接受而客户端无气泡。更换本地语言不能消除这个未知条件。本版已实际发送由发布 EXE 纯 C# 编码的两秒 SILK：4756 字节、显式 6/24000 Hz/16 bit/2000 ms，API 确认并保存唯一 Sent；随后配套文字在电脑版可见，但两次观察未见新语音气泡。电脑版还有存储不足弹窗，截至发布核查时尚未得到本轮手机确认，不能确认播放成功。详细证据见 [新版原生语音记录](evidence/native-silk-v130.json)。原生 VOICE 保持实验状态，音频附件不作为气泡验收。

## 测试、交付与许可

运行 `dotnet run --project src/Weixin.Tools -- test-all --output <新目录>` 完成 locked restore、Release 构建、单元/codec/tool 测试、自包含发布、真实 EXE 离线进程测试和再次 locked restore。Console 测试项目需要 dotnet run，dotnet test 不运行这些检查。离线 fixture 不证明真实微信送达。

报告、打包和隐私审计也由 Weixin.Tools 完成。源码包只取公开源码、静态研究快照、合成向量与许可；成品包只带 .NET 应用和报告。绑定状态、聊天、用户媒体、下载目录、二维码及日志不进入公开包。审计结论只覆盖实际提供的状态凭据与私有媒体目录。

自有代码采用 MIT；完整 SILK 编码器保留 Apache-2.0、Jitsi 和 Skype BSD-3-Clause-Clear 声明，解码器保留原 MIT。第三方许可不会被项目 MIT 替代。原/现 SHA 见 research/greepar-silk-provenance.json、research/managed-silk-provenance.json；详细许可见 [第三方通知](../THIRD-PARTY-NOTICES.md)。

## 封号风险与已知范围

请求默认字段遵循固定官方源码，不增加本项目的线协议标识，不加入规避风控或隐匿客户端的机制。公开源码兼容不等于腾讯批准任意第三方客户端，也不能证明零封号或接口长期不变。字段声明及 HTTP 成功都不构成某项能力已开放的证据。

程序遇到会话或权限异常会停止相应请求；Unknown 不自动重发，范围只限绑定会话。本次没有观察到可以归因于程序的封号，也没有足够样本计算封号概率；不能由短时测试推广到长期稳定。原生语音外发仍需真实客户端验收，相关未知不得写成已支持。
