# 直接运行官方 Node 组件的语音对照

历史实验：当前源码（2026-10-08，尚未发布）已停用 CLI 原生语音发送，音频统一走 FILE 文件附件。以下对照保留原始结论，不作为当前 CLI 的原生语音操作说明。

2026-10-04，Asia/Shanghai。OpenClaw 有语音识别和 TTS 能力，但微信通道如何发送音频，要看腾讯的适配器。这个实验直接运行原版 `@tencent-weixin/openclaw-weixin@2.4.9`，随后由绑定机器人向自己的微信发送两笔消息。

## 官方代码走哪条路

OpenClaw 的 [TTS 文档](https://docs.openclaw.ai/tools/tts)说明语音气泡取决于通道；框架支持 TTS，并不意味着每个通道都实现了原生语音。腾讯适配器的 [channel.sendMedia](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/channel.ts#L281) 与[自动回复发送器](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/process-message.ts#L374)最终调用同一个 [sendWeixinMediaFile](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/send-media.ts#L60)。这里按图片、视频、其他文件分类，没有消费 `audioAsVoice` 的原生语音分支，也没有出站 SILK 编码器。

入站是另一条路径：[media-download.ts](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/media/media-download.ts#L71)可以下载、解密微信语音，再由 [silk-transcode.ts](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/media/silk-transcode.ts#L57)转成 WAV；附带文字的语音可以直接使用文字。这证明有接收语音的实现，不能用来证明能发原生气泡。

## 实际运行环境

Node v24.19.0；腾讯适配器 2.4.9；OpenClaw 宿主 SDK 2026.8.1，按适配器开发依赖固定；silk-wasm 3.7.1。官方包的 registry SHA-512 校验通过，已安装的 125 个适配器文件与原始 tarball 逐文件 SHA-256 一致。SDK 使用真实 npm 实现，没有用空函数替代宿主导出。安装关闭生命周期脚本，文件只放在忽略的本地研究目录。

这里运行的是实际媒体组件和 SDK，不启动完整 gateway、模型、TTS 服务或自动代理。OpenClaw 框架新版本的文档用于核对语音职责，不能算作已经执行了新版本全链路。

## 七组本地检查

先拦截 fetch，仅允许测试域名并返回模拟 HTTP 响应。原版代码负责文件读取、字段构造、AES 加密及 SILK 解码；入站保存函数注入内存实现。没有连接真实账号。

| 检查 | 实际结果 |
| --- | --- |
| MP3 经正式媒体发送器 | 上传 `media_type=3`，消息项 `type=4`，FILE |
| SILK 经正式媒体发送器 | 上传 3，消息项 4，FILE |
| WAV 经正式媒体发送器 | 上传 3，消息项 4，FILE |
| 视频对照 | 上传 2，消息项 5，VIDEO |
| 手工 VOICE 交给通用发送器 | 转发调用方构造的上传 4、消息项 3、MP3 `encode_type=7` |
| 入站语音 | 实际 AES 解密、SILK 解码，保存为 `audio/wav` |
| 原版 silk-wasm 解码 | 得到 24 kHz、单声道、16 位、2000 ms WAV |

七组既定断言通过，不是七次真实微信验收。最后一组输出 96000 字节 PCM，SHA-256 为 `60462a3fecd5e604fe786eba52d7671f4dcbbc9f22cbbc281868feb509ca3a89`，与独立 Skype C 参考结果不同；脚本没有把参考一致性作为通过条件。本次只能确认官方 WASM 路径执行和上述输出结构，不能声称波形或听感完全相同。

## 向绑定微信发送的两笔对照

复用两秒合成 MP3：8493 字节、24 kHz、单声道、内容时长 2000 ms，SHA-256 为 `38a037997838112a8cb70e89d19528d701407893d8187485c9ea7b3553921a1c`。未使用用户录音。

| 路线 | 实际调用和字段 | 微信电脑版观察 |
| --- | --- | --- |
| 官方默认媒体路线 | `sendWeixinMediaFile`；上传 3、消息项 4 | 看见 `node-audio.mp3` 附件 |
| 原生语音实验 | 官方上传函数 + `sendMessageItemWeixin`；调用方手工构造上传 4、消息项 3、`encode_type=7`、`sample_rate=24000`、`playtime=2000`，省略位深 | 两次查看最新消息区域，未见本轮新语音气泡 |

六个 HTTP 请求均返回 200，两条发送各有唯一持久 `Sent`，没有 Unknown 或重发。响应没有数值 `ret` / `errcode`；这里的 Sent 仅指官方 SDK 调用正常返回并保存该状态，不是收件回执或播放确认。

后一次电脑版观察出现“暂无法连接 OpenClaw”提示。本轮观察时没有存储不足弹窗，但此前该客户端出现过该提示。附件仍可见，手机没有本轮确认，未完成气泡播放验收。这个样本不能证明所有账号或微信客户端永久不支持原生外发。

手工 VOICE 测试使用原版底层上传和结构化发送函数，但 VOICE 描述符由实验调用方构造；它不是官方默认 `sendMedia` 已实现的功能。将 MP3 换成 Node 发送，没有得到原生气泡成功的证据。

## 与 C# 对照

另用 C# `ILinkClient` 的实际方法生成 FILE / VOICE 的上传及发送请求，自定义无内层 handler 捕获，未读取绑定状态或联网。四组 JSON 请求体在替换随机值和凭据后结构和值一致，未声称序列化字节一致。请求头有一个真实差异：Node 为 `Content-Type: application/json`，C# 为 `application/json; charset=utf-8`。比较程序因此返回 1，保留 `allEqual=false`，没有写成完全一致。

比较只覆盖记录的 API 请求头和请求体，去除 Content-Length 与随机值，不比较 TLS、HTTP 栈或用户可见播放。本轮没有改动生产源码；C# 和 Node 都未完成原生气泡验收，具体外发条件仍未知。

## 证据与交付

[执行证据](evidence/official-node-v130.json)包含版本、校验、七组本地结果、两笔实机请求的脱敏结构、持久状态计数及 C# 对照差异。账号、token、上下文、client_id、签名 CDN 参数和媒体密钥用占位符替换；这些替换仅用于公开记录，实际请求没有替换。没有公开截图、聊天或用户媒体。

凭据经匿名管道传给 Node；C# 桥接程序持有状态独占锁，在真实 sendmessage 前保存 Sending，在返回后保存结果。原始绑定及诊断保留在私有目录，不使用 WorkBuddy 或其他程序的凭据。

公开项目和原 1.3.0 成品继续保持纯 C#，不新增 Node/WASM 运行依赖，也不覆盖已经发布的 ZIP 或版本标签。本记录是发布后的研究补充。正式音频附件可用，原生语音仍标为实验功能。
