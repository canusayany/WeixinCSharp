# 微信助理协议研究与 C# 实现报告

核查日期：2026-10-04 · 时区：Asia/Shanghai · 交付版本：1.2.1

## 1. 结论与适用范围

交付 1.2.1 补齐官方打字生命周期：每 5 秒刷新、60 秒本地 TTL、连续两次刷新失败停刷新；取消时等待在途请求，清理总预算默认 10 秒。线上字段仍遵循腾讯 2.4.9 固定源码，没有增加 C# 产品标识。停止时的并发保护属于本地工程差异，60 秒不能作为微信服务器配额或显示时长承诺。

本版完整流水已通过，发布进程汇总时间为北京时间 **08:32:51**：锁定恢复、Release 构建 **0 警告/0 错误**、**94 个具名单元组及 StateVault 套件**、实际本地 codec **15/15**、发布 EXE 离线进程 **62/62**、发布后再次锁定恢复全部通过。首轮 61/62 的测试断言竞态及修订依据单独保留。新源码、成品摘要和实机记录见第 8 节及[本版验证](VALIDATION.md)，旧 73/15/56 和 5602D2/A8BA0D/903A69 摘要属于 **1.2.0 历史**，完整保留在[旧报告](REPORT-1.2.0.html)和[旧验证](VALIDATION-1.2.0.md)。

新版已在微信电脑版 **4.1.15.13** 完成新的可见窗口验收：真实 60 秒 typing 的 START 和最终 CANCEL 被 HTTP 接受、CLI 退出 0、状态字节未改变。第 **16.6 秒、55.4 秒**均直接看到“对方正在输入…”，CLI 结束后 **2.1 秒**观察到恢复正常机器人标题。每个实机刷新响应未单独插桩，不推断逐毫秒连续显示或排除服务器 TTL 后的取消因果。新版短口令 **m3b** 与完整回复“已收到：m3b”均在电脑版可见，持久记录恰好一个匹配 Sent 回执、Inbox 为 0；证据见[新版 UI 观察](evidence/desktop-client-v121-visible-evidence.json)及第 8 节。

随后使用同一版成品补验 Markdown、视频和文件发送。新版 md7 的标题、加粗、行内代码、列表样式及完整内容直接可见，合成三秒 MP4 从 00:00 播放到 00:03；两个业务键各有一个匹配 Sent。**文件接口也已实测**：发送 16 字节 file7.txt，命令退出 0、唯一匹配 Sent，用户确认微信收到、能打开且内容“文件验证 f7”正确。C# 文件调用由 UploadBoundFileAsync（MediaKind.File）、ToMessageItem 和 SendBoundItemAsync 组成；命令行使用 `send-media --kind file`。这些样本不证明全部 Markdown 语法、长视频或所有文件大小均能使用，具体证据见第 8 节。

较早窗口捕获为幻灯/播放画面、聊天不可见，未确认遮挡来源或是否锁屏；该轮 UI 未验证记录保留。第一次 k2a 监听结束前未执行 UI 发送，没有匹配消息；另起 m3b 窗口完成验收，不覆盖原失败结果。旧 d9k/ok7 与手机媒体确认仍仅属于 1.2.0 历史。

**原生语音气泡仍未完成。**此前合成 SILK、完整元数据 SILK、真实手机语音原样回发以及保留原字段/原 CDN 的实验均有 API 接受但手机或电脑版未见气泡的记录。音频 FILE 附件不能替代用户要求的直接点播语音；本版 typing 修复不解决该出站能力，也不将 VOICE 类型声明当作可用保证。旧文字与媒体确认按 1.2.0 运行字节保留；新版文字、typing、Markdown、视频及文件发送另有实机记录。新版真实语音/视频入站与自动解码尚未重测，不将旧结果换作新版本验收。

实现使用腾讯公开 **iLink Bot HTTPS/JSON 通道**，范围是个人微信与自己扫码绑定的助理/机器人。主要依据为腾讯[官方仓库](https://github.com/Tencent/openclaw-weixin)及[后端协议](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/docs/protocol_zh_CN.md)，WorkBuddy/GitHub 第三方项目仅作机制对照。当前通道已有公开官方实现，因此没有下载或反编译 WorkBuddy 安装包。交付包括 C# 源码、Windows x64 成品、固定源码/许可快照与报告；长期稳定、生产接入资格和零封号均无已验证保证。

## 2. WorkBuddy 的两条微信路线

| 对比 | WorkBuddy 逆向参考中的客服通道 | 当前实施的 iLink Bot 通道 |
|---|---|---|
| 依据 | HenryXiaoYang/wechat-openclaw-channel，2026-03-21 快照 | Tencent/openclaw-weixin，2.4.9 源码快照 |
| 身份 | CodeBuddy OAuth access/refresh token、工作区注册 | 用户扫码授权后 bot_token |
| 绑定 | wechatkfProxy/link、bindStatus | get_bot_qrcode、get_qrcode_status |
| 接收 | Centrifugo WebSocket、订阅 token | HTTPS getupdates 长轮询、持久游标 |
| 回包 | local-proxy/receive 的 COPILOT_RESPONSE | sendmessage 的 msg/item_list |
| 本次用途 | 静态机制对照，未运行或复用私有账号接口 | 重新编写 C# 兼容客户端 |

[逆向项目固定源码](https://github.com/HenryXiaoYang/wechat-openclaw-channel/tree/8b8b13434fb30a589a8ebd7c68e233c1b49e3f41)显示，其调用 CodeBuddy 登录状态、令牌交换、微信客服绑定与 registerWorkspace，然后连接返回的 WebSocket，把客服消息封装回 COPILOT_RESPONSE。它不是 iLink 的鉴权变体。名称含 WorkBuddy 的大模型 API 反代仓库也不能作为微信消息协议证据。

WorkBuddy 官方 [微信助理指南](https://www.codebuddy.cn/docs/workbuddy/WeixinBot-Guide)给出扫码接入，产品端要求 WorkBuddy ≥4.6.4、手机微信 ≥8.0.70、账号相同或关联。这些是 WorkBuddy 的产品前提；独立 C# 程序不依赖 WorkBuddy 常驻，只需其自身运行及账号具有当前微信插件入口。具体账号入口和专项授权条件仍需手机上确认。

## 3. 研究基线与可复现证据

| 资料 | 固定版本或提交 | 结果 |
|---|---|---|
| 腾讯公开源码 | 24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c | 有实际 MIT LICENSE 文件 |
| 官方 npm | @tencent-weixin/openclaw-weixin 2.4.9 | registry 发布 2026-09-17 12:20:17 北京时间；本次 dist-tags.latest 为 2.4.9 |
| npm 包校验 | SHA-256：467e8047f7114e45944961fcd3eda9421843c9c65db61ea24176e252ab800ee4 | 实际 SHA-1 与 registry shasum、SHA-512 与 SRI 均匹配 |
| 包与仓库交叉核对 | api.ts、types.ts、login-qr.ts、send.ts、monitor.ts | 统一换行后逐字一致 |
| 旧 WorkBuddy 参考 | 8b8b13434fb30a589a8ebd7c68e233c1b49e3f41 | 仅机制参考；package 声明 MIT，但快照未找到 LICENSE |
| 第三方 iLink demo | 248efbee46be7efe3c3890279d22ea2c0a59907a | 未发现许可证；仅用于辨别旧接口示例，不复制代码 |

研究未安装或启动 WorkBuddy，也未执行 OpenClaw 插件安装脚本；没有读取 WorkBuddy 配置、解密其凭据或借用他人 token。新增本地 codec 功能实际运行固定的 Node/SILK 依赖，来源、完整许可和测试单列。官方关键源码、许可证和 provenance.json 随交付保存；第三方无明确许可证的源码不随源码包分发。证据详细行链接见 research/github-evidence.md，codec 依赖见 runtime/voice/provenance.json 与 research/runtime-dependencies。

## 4. 线协议设计

正常业务使用 HTTPS POST JSON。默认 API 为 `https://ilinkai.weixin.qq.com`。鉴权是 `Authorization: Bearer <bot_token>`；这个 token 由用户在手机上确认绑定后返回，与会话上下文 token、CodeBuddy OAuth token 不同。

| 字段 | 实现值或规则 |
|---|---|
| iLink-App-Id | bot |
| iLink-App-ClientVersion | 132105，即 2.4.9 对应的 0x00020409 十进制 |
| AuthorizationType | JSON POST 使用 ilink_bot_token |
| X-WECHAT-UIN | 随机 uint32 的十进制文本，再做 Base64；不是实际微信 UIN |
| base_info.channel_version | 2.4.9，表示本次兼容配置的上游基线，不是服务端协商版本 |
| base_info.bot_agent | OpenClaw，沿用官方请求构造器的默认值；本地成品仍明确标为独立 C# 兼容实现 |
| msg.client_id | 沿用官方 openclaw-weixin:Unix毫秒-8位随机十六进制 格式，不附加自定义产品前缀 |

完整字段依据：[官方请求构造器](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/api.ts)。按用户要求，线上字段沿用公开源码的默认行为，移除自定义 WeixinCSharp bot_agent。本程序不复制设备指纹或绕过验证；成品说明仍如实标明独立 C# 实现。

使用公开协议的字段、请求头和版本值用于兼容，不是免封手段。bot_agent 的官方默认值为 OpenClaw，官方说明其仅用于日志及监控、不参与鉴权或路由；该值不赋予账号授权或免封资格。凭据在本机由 DPAPI 加密，错误日志不输出 token 或原始响应；HTTPS 保护传输过程，但合法服务端仍会收到鉴权和会话字段。隐藏本地凭据、减少诊断日志中的敏感数据，不能消除账号服务规则或风控限制。

### 4.1 扫码与配对

```http
POST /ilink/bot/get_bot_qrcode?bot_type=3
Content-Type: application/json

{"local_token_list":[]}
```

新源码是 **POST、bot_type=3**。部分旧第三方示例用 GET 或旧 bot_type，不能直接照搬。扫码创建请求使用应用头、AuthorizationType 和随机 UIN，但不带 Bearer，也不加 base_info。

状态轮询是 `GET /ilink/bot/get_qrcode_status?qrcode=<URL编码>`，只发送应用公共头。需要手机配对码时追加 verify_code；程序只接收用户输入，最多三次尝试。wait、scaned、confirmed、expired、need_verifycode、verify_code_blocked、scaned_but_redirect、binded_redirect 都有独立处理；wait/路由变化期间保留待验证代码，明确接受或刷新二维码后才清除。登录总窗口五分钟，过期二维码最多刷新三次。

confirmed 必须含 bot_token、ilink_bot_id、ilink_user_id，再保存经过校验的 baseurl。已绑定状态不被错误解释为发放新 token。依据：[官方扫码状态机](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/auth/login-qr.ts)。

### 4.2 接收与回复

```json
{
  "get_updates_buf": "上一次成功响应的游标",
  "base_info": {"channel_version":"2.4.9","bot_agent":"OpenClaw"}
}
```

该请求发往 `/ilink/bot/getupdates`。初次游标为空；正常超时及缺失/空游标维持旧游标。入站过滤同时要求 from_user_id 为绑定者、to_user_id 为已绑定 bot、message_type 为用户消息、无 group_id，且 message_state 为完成值 2 或省略。异常消息数组、空消息或空 item_list 项在提交整批游标前拒绝。message_id 按字符串保存原始整数精度，避免 uint64 被浮点数截断；未知媒体字段仍留在描述符中。

```json
{
  "msg": {
    "from_user_id":"",
    "to_user_id":"扫码绑定的 ilink_user_id",
    "client_id":"openclaw-weixin:Unix毫秒-8位随机十六进制",
    "message_type":2,
    "message_state":2,
    "context_token":"对应入站消息的上下文",
    "item_list":[{"type":1,"text_item":{"text":"你好"}}]
  },
  "base_info":{"channel_version":"2.4.9","bot_agent":"OpenClaw"}
}
```

回复发往 `/ilink/bot/sendmessage`。自动回复取具体 InboxEntry 的 context_token；手动 send 用最近保存的绑定会话上下文。context_token 缺失时客户端拒绝发送。官方发送 helper 虽会在缺失时继续请求，但这不证明服务端一定接受，也不证明允许无限期主动推送。依据：[官方消息发送器](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/send.ts)。

### 4.3 响应合同与 1.1.0 兼容修复

官方类型将 getupdates 的 ret/errcode、sendmessage 的 ret、notifyStart/notifyStop 的 ret 声明为可选。官方实现对已提供的非零结果码判断失败，缺少 ret 不阻断成功分支；`{}` 是官方客户端可以解析和接受的响应对象。1.0.0 额外要求发送、更新及通知响应必须含结果码，真实扫码后的首次监听因此退出。1.1.0 移除这一额外限制：仍要求 HTTP 成功和合法 JSON 对象，已提供的结果码须为数字，非零码仍失败；空对象更新表示无新消息，保留原游标。

HTTP 2xx 加合法空对象在这个公开客户端合同下可以形成 API 确认，不能证明手机实际收到。空响应体、损坏 JSON、非对象、结果码类型错误，以及发送中的超时/断连仍属于确认不足，记录 Unknown，不自动重发。依据：[官方响应类型](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/types.ts#L220)、[官方错误处理说明](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/docs/protocol_zh_CN.md#L69)。

### 4.4 媒体上传、下载与语音边界

本节协议、资源保护和编码路径适用于当前实现；以下各次手机及电脑版媒体实验属于 **1.2.0 历史**，各自 DLL 摘要限定其来源。详情完整保留在 [1.2.0 验证](VALIDATION-1.2.0.md)。

默认 CDN 为 `https://novac2c.cdn.weixin.qq.com/c2c`。公开上传流程读取明文、计算大小和 MD5，生成 16 字节随机 AES 密钥及 filekey，调用 getuploadurl，再向 CDN 上传 AES-128-ECB/PKCS#7 密文。rawsize 为明文字节；filesize 为 `ceil((rawsize+1)/16)*16`。优先使用 upload_full_url，否则使用 upload_param 构建官方 CDN 路径。CDN POST 使用 application/octet-stream，成功后读取 x-encrypted-param，再把引用放入 sendmessage 的媒体 item。上传成功只是媒体存储阶段完成，不代表聊天消息已经发出。

发送器中的 video_item.video_size 为密文字节数，file_item.len 为明文字节数的字符串。media.aes_key 按官方发送器使用十六进制密钥文本的 Base64；接收兼容原 16 字节密钥的 Base64 与 32 位十六进制文本的 Base64。图片优先读取 image_item.aeskey。下载和解密应验证可信 CDN 主机、响应大小、密文块长及 PKCS#7，不把带密钥或签名参数的 URL 写入日志。依据：[官方上传流水线](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/cdn/upload.ts)、[官方下载解密器](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/cdn/pic-decrypt.ts)。

公开源码的实际媒体发送路由仅有图片、视频和文件，audio/* 按文件附件处理。类型中虽然有 UploadMediaType.VOICE=4 与 voice_item，却没有已接入该路由的原生语音发送 helper。因此音频文件发送与微信原生语音气泡须分别验收：MP3/WAV 作为文件发送属于已知官方路径；直接组装 voice_item 的服务端接受与手机播放能力不能仅凭字段定义判断。依据：[官方媒体路由](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/send-media.ts)、[官方协议中的同一边界说明](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/docs/protocol_zh_CN.md#L299)。

接收语音时，voice_item.text 可含转写文本；官方 encode_type 注释列出 PCM/ADPCM/feature/Speex/AMR/SILK/MP3/OGG-Speex，sample_rate 为 Hz，playtime 为毫秒，但本次实际样本的编码字段与字节格式存在差异，不能仅按枚举选解码器。官方下载器调用 silk-wasm.decode 按 24000 Hz 将 SILK 转 WAV，失败保存原始 SILK。C# MediaClient 保存解密后的原始媒体，CLI 对每个入站 VOICE 尝试 VoiceCodec 按实际 SILK 字节校验、解码并保存 WAV，失败保留原文件；不推定全部编码可解码。原实现只在 encode_type=6 时尝试，本次真实 Tencent SILK 样本的该字段不为 6，已按官方 VOICE 解码路径修复该条件，修复后的回归另列。CLI 还提供 MP3/SILK 实验 voice 请求及音频附件。真实发送 2 秒 MP3 附件、2 秒原生 MP3 voice、3 秒蓝色视频及随后 2 秒 SILK voice 均获 API 确认并持久保存 Sent；本人确认蓝色视频正常接收并可直接播放，**SILK 原生语音没有收到，原生语音手机验收失败**。MP3 原生与音频附件没有取得手机确认；入站媒体验收另列。

随后于北京时间 **07:05:36 至 07:05:37**，通过 LiveE2E SDK helper 的显式 --native-silk 模式对 6440 字节、实际 2000 毫秒 SILK 执行一次受控发送。请求补齐 encode_type=6、sample_rate=24000、bits_per_sample=16、playtime=2000，AES 密钥字段与当前官方媒体发送器格式对齐；上传和发送获 API 接受，持久状态为 Sent，用户仍明确确认没有原生语音气泡。见 [完整元数据请求证据](evidence/native-full-metadata-evidence.json)及[人工确认](evidence/human-confirmations.json)。固定官方 VoiceItem 将采样率、位深等声明为可选字段，未给出“补齐即可送达”的支持条件；本次结果排除了这组声明差异可修复本次发送的假设，不能推定所有账号或编码都同样失败。依据：[固定官方类型](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/types.ts#L116)。

为区分合成样本与真实手机编码，另于 **07:26:08 至 07:26:10**，使用同一 helper 将刚收到的真实 3585 字节、2800 毫秒 SILK 原样重新上传并发回本人，声明 encode_type=6、sample_rate=24000、bits_per_sample=16、playtime=2800；API 接受并保存 Sent。[真实语音原样回发证据](evidence/native-real-voice-replay-evidence.json)的样本摘要与本次自动入站原文件相同，用户仍明确确认没有原生语音气泡，见[人工确认](evidence/human-confirmations.json)的 outbound-native-real-inbound-silk-replay 项。**真实手机语音原样回发也失败**，排除了本次仅因使用合成样本而失败的解释；前两次合成 SILK 手机未收到的事实保留。上传和 sendmessage API 接受不等于手机送达，未知服务端支持条件，不能据此断言全球账号或全部编码均不支持。公开证据只含元数据和摘要，不包含真实声音；该请求仍使用 07CD9A 开头的辅助程序协议 DLL，而非最终生产协议 DLL。

该 helper 模式须显式提供 live 状态、SILK 文件、独立业务键与证据输出路径，不能与 --exe 监听模式混用；它先解码核对实际时长，已有同业务键尝试则拒绝上传和重发。模式是在 06:44:52 历史生产流水之后新增，单独执行 dotnet build tests/Weixin.LiveE2E/Weixin.LiveE2E.csproj -c Release --no-restore（0 警告、0 错误）并实机运行一次。使用的辅助程序协议 DLL 摘要为 07CD9A6F895660B6B68EC525BE91E1B8B131B95FB73BAD67DB0CF772FA3426AB，区别于当时交付协议 DLL 903A69C9CF5A3E1CCC5168EF0DF5AD45B9DF4865C0702AB9584DCA8600F7454E；该 helper 测试本身未修改生产源码及成品。它不属于当时 53 个发布 CLI 案例，也不宣称再次执行过全 solution 流水。

本次另核对当前正式 main 仍为研究固定提交，最新稳定 tag 为 v2.4.9（43675b66551d12d6853155a7869a50fb12a18a1e）；未发现新增 voice-outbound.ts 或已经启用的原生发送入口。官方仓库 [#215](https://github.com/Tencent/openclaw-weixin/issues/215)、[#126](https://github.com/Tencent/openclaw-weixin/issues/126)、[#91](https://github.com/Tencent/openclaw-weixin/issues/91)含用户对 MP3/SILK 请求成功而手机不可见的复现；[#209](https://github.com/Tencent/openclaw-weixin/issues/209)是用户声称曾可用、后来失效。核查这些 issue 的 18 条评论未见可核实维护者发布的支持条件或可用承诺。用户报告是相似现象佐证，不是正式接口合同，也不能据此断言所有账号都被服务端禁用。当前只能确认本次原生语音失败；改编码或补 helper 不能代替手机验收。

未合并的 [PR #282](https://github.com/Tencent/openclaw-weixin/pull/282)也不构成官方支持条件：其较早提交 [58ee7e8e1514a2246c31489066cb028dcf3196a2](https://github.com/Tencent/openclaw-weixin/commit/58ee7e8e1514a2246c31489066cb028dcf3196a2)尝试原生 VOICE 后回退 FILE。当前 head f6278571827bfa21ef56d2802d1f84842667d6e2 有不同入口：[channel.ts 277–294 行](https://github.com/Tencent/openclaw-weixin/blob/f6278571827bfa21ef56d2802d1f84842667d6e2/src/channel.ts#L277)将 audio 降为 TEXT；[send-media.ts 59–94 行](https://github.com/Tencent/openclaw-weixin/blob/f6278571827bfa21ef56d2802d1f84842667d6e2/src/messaging/send-media.ts#L59)将 audio 走 FILE；[send.ts 301–341 行](https://github.com/Tencent/openclaw-weixin/blob/f6278571827bfa21ef56d2802d1f84842667d6e2/src/messaging/send.ts#L301)保留 VOICE helper，但前述入口没有调用它。不能概括为该 PR 全部 audio→TEXT，更不能把残留 helper 当作已启用的原生通路。正式 main 的[媒体路由](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/send-media.ts#L60)和[上传流水](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/cdn/upload.ts#L151)仍选择 image/video/file；[公开 GetConfig 类型](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/types.ts#L265)未声明 VOICE 权限开关，不推定服务端不存在其他未公开条件。

**本版外部状态复核：**北京时间 08:42 检查 main 仍为 24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c；稳定 v2.4.9 指向 43675b66551d12d6853155a7869a50fb12a18a1e，npm latest 为 2.4.9，main/stable 两个媒体路由文件逐字节相同。PR #9 和 #282 均 open、未合并；分别 4、11 条评论角色全为 NONE，没有 review，未发现可验证维护者发布的新支持条件。08:48 进一步核实上述 PR #282 入口。完整 URL、提交、文件摘要、时间和结论边界见[官方语音复核记录](evidence/official-native-voice-refresh-v121.json)。这说明当前已检查的官方发布材料未提供已启用的原生发送路径，不证明所有账号均不支持，也不是官方禁用声明。

北京时间 **09:36:46**再次进行有限的公开状态检查，main、stable、npm latest 和两个 PR 的 state、merged、head、updated_at 均未变化，见[官方状态增量记录](evidence/official-native-voice-state-v121-r2.json)。没有重复下载同一不可变源码或评论，也没有账号操作；此检查不揭示私有后端权限条件。

VoiceCodec 的本地编码使用固定 silk-wasm 3.7.1 自带的 encode API，生成 0x02 + #!SILK_V3 腾讯格式；这是本 C# 客户端的扩展，不是腾讯插件已接入的原生发送默认流程。prepare-voice 接受 PCM16 单声道 24000 Hz WAV 或裸 PCM，按 20 毫秒帧补齐末尾，短于 40 毫秒的输入补到两帧；返回时长包含补齐。decode-voice 输出 PCM16 单声道 WAV，验证包结构并按实际 PCM 大小计算时长。默认本地保护为输入/输出各 16 MiB、时长 60000 毫秒、操作超时 30 秒；解码预检按每包最多五帧保守估计资源，可能在配置过小时拒绝有效输入。这些不是腾讯服务上限。来源：[腾讯实际解码调用](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/media/silk-transcode.ts#L57)、[silk-wasm 编解码接口](https://github.com/idranme/silk-wasm/blob/5971995be3ce05d4a1bf820fdf9d7ccdc942e945/src/index.ts)。

编解码使用随包 Node.js 24.19.0 与 silk-wasm 3.7.1，音频经二进制标准输入/输出传递，不通过微信执行代码。分发保留 silk-wasm MIT、捆绑 wav-file-decoder MIT、Russell BSD、底层 Skype SILK SDK 版权/条件/免责及 Node 许可。Skype SDK 许可不授予专利权；不以 codec 许可推定账号服务授权或原生语音送达能力。

官方下载合同还允许无 AES 密钥的明文图片；C# 仅对图片启用该路径，语音、视频和文件仍要求有效密钥。下载对 Content-Length、实际读入及解密后大小分别限制，关闭重定向，只信任明确允许的 CDN 域名。下载名由稳定 SHA-256 键和受限扩展名构成，拒绝已有链接或内容冲突文件。媒体传输错误使用独立 MediaTransferException，CDN 401/403 不误报为 Bot token 失效；CLI 返回 7，并保留失败收件项供重启恢复。

#### 4.4.1 原字段与原 CDN 描述符回发实验

为补充电脑版观察，另于 **07:50:41 至 07:50:43** 使用 LiveE2E 的显式 --replay-inbound-voice 模式，保留新收到的 VOICE 原字段及原 CDN 描述符，使用对应消息上下文构造 Bot 回复。该样本声明 encode_type=4、sample_rate=16000、bits_per_sample=16、playtime=2320，实际媒体 1600 字节通过 0x02 + #!SILK_V3 签名及 codec 校验，解码为 111404 字节、2320 毫秒 WAV。**这是一次字段与实际格式不一致的样本观察，不将 4 改称官方 SILK 映射，不推定所有语音均如此。**入站按实际字节验证、解码的生产行为仍适用。

[脱敏实验记录](evidence/native-descriptor-replay-public.json)显示发送已保存 Sent，handler 已确认、Inbox=0、停止通知已确认。本次同时保留原字段与 CDN 引用，不能把结果因果归到单个字段；原 CDN 复用只是显式实验，不是已支持的生产功能。新辅助代码已单独 Debug 构建为 0 警告、0 错误，使用协议 DLL **EEF0CFAEB46A572C6D6D902B713107991970BA634AF28472482E46D4ED1118B9**，区别于最终生产 Release 的 903A69 开头摘要。生产源码未改，不重计 73/15/56，也不声称新的 helper 被此前 56 案例覆盖。

本机微信 **4.1.15.13** 截至 **07:51:42** 仍没有新的 Bot 原生语音气泡，距实验开始至少 60 秒；按本次观察窗口登记失败，不能排除未知更长延迟。与此同时新短口令 d9k 的完整文字回复正常可见，区别于语音不可见的结果。已点击旧蓝色三秒视频，官方电脑版播放器进度到 00:03；旧 Markdown 卡片中包含 bold/inlinecode 测试内容的普通文本可见，不视为全部 Markdown 语法富文本渲染通过；音频 FILE 显示为附件，不能代替原生气泡。typing 开始时顶部“对方正在输入…”已直接观察到，但本轮定时取消连接失败、CLI 退出 2；随后显式 stop 请求被 HTTP 接受、退出 0，指示器在 stop 前已消失，不将消失归因于停止请求。参见 [电脑版综合观察](evidence/desktop-client-e2e-public.json)。未保存桌面截图、联系人信息或原始声音。此前手机失败记录保留，仍不能将原生语音目标登记为完成。

### 4.5 Markdown 与 C# 调用函数

Markdown 通过普通 text_item.text 传输，协议没有独立的 Markdown 消息类型。官方回复路径使用 StreamingMarkdownFilter，保留代码块、行内代码、表格及粗体等，过滤图片语法、H5/H6 和部分中日韩斜体标记；手机显示效果仍以实际客户端为准。不能把发送了一段含 Markdown 标记的字符串写成完整富文本渲染保证。依据：[官方 Markdown 过滤器](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/markdown-filter.ts)。C# 暴露 MarkdownFormatting.ConvertToPlainText/ChunkText、PersistentBotRunner.SendBoundMarkdownAsync、MediaClient.UploadBoundFileAsync/DownloadItemAsync、UploadedMedia.ToMessageItem 及 PersistentBotRunner.SendBoundItemAsync；示例见 README。

SendBoundMarkdownAsync 过滤并按最多 4000 UTF-16 码元分段，持久保存原始输入摘要和各分段确认，最多 64 段、64 个历史作业，均为本地限制。相同业务键及输入可恢复已确认分段，Unknown 不自动重发，输入改变则拒绝。模型/Markdown 单元套件 19 个具名组及完整进程测试覆盖过滤、代理对、分块、部分发送恢复和未知确认。旧版一段 Markdown 获用户手机确认；新版 md7 的 88 字节、52 个 UTF-16 码元样本在电脑版完整显示标题、加粗、行内代码和列表样式，独立调用当前 DLL 核对其过滤结果、分段和摘要。这不代表全部语法或长分段都已实机验证。

**文件发送接口：**使用 `MediaClient.UploadBoundFileAsync(session, filePath, MediaKind.File)`加密上传，再用 `UploadedMedia.ToMessageItem()`构造 FILE，调用 `PersistentBotRunner.SendBoundItemAsync`持久发送。上传的 media_type 为 3，发送项 type 为 4，file_item 的 file_name 为文件基名、len 为明文字节数的十进制字符串，media 沿用公开 AES/CDN 字段。依据：[官方上传流程](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/cdn/upload.ts#L151)、[官方发送 helper](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/send.ts)。SDK 首次发送示例及 `send-media --file <文件> --kind file` 见 README。低层 SDK 是上传与发送两阶段；业务恢复需先核对持久 receipt 并保留已上传描述符，不能在同一键下重新上传后直接重发。CLI 已有源文件/声明摘要前置检查，同键、同源和 Sent 在再次上传前返回既有确认；Unknown 不自动重发。

“一次 CLI 调用”不等于一次 CDN HTTP 上传尝试：上传阶段仍可按既有流水最多尝试三次；上传完成而聊天意图尚未持久化前若发生崩溃，重启可能再次上传。上述 Sent 前置保护及聊天 Unknown 保护不能写成全过程绝无重复上传的保证。

协议类型还含工具调用开始/结果描述符 11/12，字段为 tool_name、tool_call_id 及结果 status。它们是消息描述字段，不能据此推断服务端执行任意 C# 函数、理解任意参数 schema 或授权系统命令。C# 调用函数在本机应用中由开发者显式调用；收到微信内容仍按数据处理。

### 4.6 长度、大小与来源口径

| 项目 | 官方公开客户端的实际值 | 能否作为微信服务端硬上限 |
|---|---|---|
| 文字分块 | channel.ts 的 textChunkLimit=4000 | 否；这是客户端分块配置，源码本项未规定 UTF-8 字节计数规则 |
| 接收媒体保存 | media-download.ts 向 saveMedia 传 100×1024×1024 字节，即 100 MiB | 否；是本地存储上限，不是上传配额，官方下载器先读取 arrayBuffer |
| 单条引用媒体缓存 | quote-store.ts 默认 25 MiB | 否；是本地引用缓存预算 |
| 每账号引用媒体缓存总量 | quote-store.ts 默认 256 MiB | 否；是本地引用缓存预算 |
| bot_agent | 清洗后最多 256 ASCII 字节；默认 OpenClaw | 这是公开客户端字段校验规则，不是消息长度 |
| AES 密文填充 | 16 字节块，PKCS#7 总是添加填充 | 是本次公开加密格式要求，不是文件大小上限 |
| 语音/视频时长、媒体上传最大大小 | 本次固定源码未发现统一硬上限 | 未知；不能编造官方时长/MB 数值 |
| 本 C# 实现的保护上限 | 每段文字 4000 UTF-16 码元，分块保持代理对；MediaLimits 默认每个文件 100 MiB、声明原生语音时长 60000 毫秒，可配置 | 属本地工程约束，60 秒不是已核实的官方 Bot 时长上限；即使低于它，服务端仍可能按账号/格式拒绝 |

依据：[文字分块配置](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/channel.ts#L269)、[媒体保存上限](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/media/media-download.ts#L9)、[引用缓存预算](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/quote-store.ts#L12)。超限时应在本地拒绝或明确分块；不通过超量试探寻找风控阈值，不把这些客户端数值当作免封承诺。

### 4.7 打字状态

GetConfigAsync 调用 getconfig，传绑定者 ilink_user_id、可用时的 context_token 和 base_info，只有明确 ret=0 才使用返回配置。typing_ticket 仅按绑定身份缓存于内存，24 小时到期，不写入 DPAPI 状态、JSON 诊断或日志。SetTypingAsync 调用 sendtyping，status=1 开始、status=2 取消；沿用官方实现只判断 HTTP 成功，不要求响应体有 JSON 或 ret。这与 sendmessage 的 JSON 确认合同分别处理。

**1.2.1 生命周期修复：**腾讯固定快照的 [process-message.ts:357](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/process-message.ts#L357)向 SDK 传入 5000 毫秒保持间隔。其开发依赖固定为 OpenClaw 2026.8.1，本报告将结论限定到对应 commit ea806575e6450e4d1efdfc72c19f04be982a1b9b，不能从较宽 peer 范围推定所有 SDK 版本均相同。[typing.ts](https://github.com/openclaw/openclaw/blob/ea806575e6450e4d1efdfc72c19f04be982a1b9b/src/channels/typing.ts#L50)在初次 START 成功后定期再次调用 START，默认本地 TTL 为 60000 毫秒；在途刷新不重叠，成功清零失败计数，连续两次失败停止刷新而仍保留 TTL。60 秒是 SDK 本地保护，不能写成微信服务器配额或已保证的显示时长。一次 sendTyping 请求及最终 CANCEL 不隐式重试。研究记录见[官方生命周期合同](evidence/typing-lifecycle-official-contract.json)。

新 C# API `CreateTypingLifecycle`/`StartAsync`/`StopAsync`/`DisposeAsync`及定时 CLI、echo 路径补齐这段行为；裸 `SetTypingAsync` 和显式 stop 仍是单次原语。C# 另在停止时取消并等待在途 START，再发一次 CANCEL，避免迟到的 START 重新开启显示；官方固定实现停止定时器但不取消已在途 START，因此此并发保护属于本地工程差异。清理总预算默认 10 秒；超时与取消失败明确报告未确认，认证失效禁止继续刷新或 CANCEL。Retry-After 推迟后续固定周期，不触发即时重试或重发聊天。新版实际测试及电脑版观察在第 8 节单独登记。

以下为 **1.2.0 历史观察**。CLI 支持 listen --echo --typing，以及 typing --typing-status start --run-for 60 / stop。实机第一轮 30 秒请求用户表示“没有看到”；用户要求重试，第二轮 60 秒开始请求后明确确认“有了”。仅登记第二轮开始显示通过，不删除首次未见的事实；取消 HTTP 成功不等同手机确认已消失。依据：[官方 getConfig/sendTyping](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/api.ts)、[官方配置缓存](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/config-cache.ts)。

后续电脑版最终 EXE 测试于 **07:52:03 至 07:53:04**：开始请求被 HTTP 接受且顶部可见指示器；60 秒定时取消报“微信打字状态连接失败”，CLI 退出 **2**，没有获得自动取消的 HTTP 确认。[该轮证据](evidence/desktop-typing-evidence.json)保留失败。在本轮进程退出前、自动取消确认及显式 stop 之前，顶部已恢复 Bot 名；未记录消失的精确时刻，原因未知，不能推定服务端 TTL 或取消作用，也没有完整 60 秒持续显示的证明。随后 **07:58:33** [单独 stop](evidence/desktop-typing-stop-evidence.json)被 HTTP 接受、CLI 退出 **0**，之后 UI 仍无 typing；只登记显式 stop 的 HTTP 成功。此前手机 phoneCancelConfirmed=null 保持不变。

## 5. 稳定性机制与限制

接收流程为：**HTTPS 响应 → 过滤/去重 → 同次加密提交收件箱与游标 → 应用 handler → 确认移除收件项**。每批先持久化，避免处理失败后已经推进游标而丢掉待处理消息。崩溃可能让 handler 被调用多次；应用须以 entry.Key 去重，不能把此方案宣称为端到端 exactly-once。

发送流程为：**记录 Sending → 发一次网络请求 → 记录 Sent / Rejected / Unknown**。超时、断连、HTTP 408/5xx、响应结构错误及发送中崩溃都可能表示已发送但确认丢失。Unknown 不自动重发；服务端 client_id 幂等性没有公开承诺。Sent 仅表示 API 成功确认，仍应核对手机送达。

| 防护 | 实现 | 实际边界 |
|---|---|---|
| 读取恢复 | 网络/408/429/5xx 指数退避、抖动、参考 Retry-After，连续失败上限 8 次 | 不会无限重试认证、未知业务或结构错误；退避期间可取消 |
| 会话失效 | 401/403 或 ret/errcode=-14 停止，跳过失效凭据的停止通知 | -14 是失效会话提示，不能作为封号判断 |
| 取消 | Ctrl+C 取消长轮询与等待；已完成 handler 的确认继续落盘 | 外部取消不被当作普通空轮询而继续运行 |
| 去重 | 最近 4096 个键，保护待处理项；最大收件箱 256 | 有限窗口；无 ID 的消息使用描述符哈希，不能保证业务语义唯一 |
| 发件记录 | 最多 128 条，不淘汰未确认状态或仍关联待处理消息的记录 | 未确认记录满时停止新增发送，要求核对 |
| 状态校验 | 恢复、提交收件、处理及发送前核对身份、模式、容量、记录状态、哈希、唯一键和关联关系 | 异常状态在网络发送前拒绝；不把本地校验失败变成已发送意图 |
| 重复回复一致性 | 同一 sourceKey 已 Sent 时核对文本 SHA-256；不同文本拒绝 | 避免把不同回复误报为已发；并不赋予服务端幂等保证 |
| 媒体业务键 | 上传前核对源文件、媒体种类和语音声明摘要；同键同载荷 Sent 返回既有确认，不再 getuploadurl/上传 | 仅在本地记录有效且使用稳定业务键时生效；Unknown 仍要求人工核对 |
| Markdown 恢复 | 持久保存作业输入与分段摘要，归档确认过的分段；最多 64 段/64 个作业 | 记录满时停止，不自动删除历史；不把一段的确认套用给下一段 |
| 媒体下载 | 可信 CDN、大小与密钥校验、稳定文件名、冲突/链接保护；失败保留收件项 | 下载失败后须修复原因并重启，无独立 CDN 下载自动退避循环；下载文件为明文 |
| 生命周期通知 | 非会话失效的启动/停止通知异常只记录安全警告；失效启动凭据仍停止 | 通知是辅助状态，通知成功不证明消息可达 |
| 本地凭据 | Windows DPAPI CurrentUser、原子替换、磁盘刷新、独占锁、8 MiB 上限 | 不是对同用户恶意进程的隔离；跨用户/机器通常需重绑；断电耐久仍取决于文件系统 |
| 主机验证 | 默认仅精确 ilinkai.weixin.qq.com；关闭自动 HTTP 跳转 | 官方未给新路由名单；新域名需核实后显式 allow-host |
| 保守发送 | 每次尝试间隔至少 3 秒；每段文字最多 4000 UTF-16 码元，长文字先分块 | 自定节流与客户端分块配置，不是官方服务端配额，也不保证规避风控 |

与官方插件的有意差异：缺 context_token 不发送；不对未知业务错误循环重试；失效会话直接停止而非定时自动复活。默认收消息不触发任何系统命令。1.1.0 的 --run-for 指定监听时长，到期正常取消长轮询并处理停止通知；--qr-png 指定临时二维码 PNG，登录结束后与 HTML 一起清理。

默认 listen 不下载媒体，成功处理后不保留永久媒体描述符历史。需要留存时应先指定 --download-dir，再请手机发送或重发。DPAPI 保护会话状态，不能替代下载目录的访问控制或文件加密。

## 6. 封号与服务限制风险

**存在账号限制或封禁风险，不能保证零封号，也无法根据公开材料量化概率。**采用腾讯公开 Bot 通道、用户本人扫码、只处理绑定者消息，减少了注入、模拟个人客户端、私有接口复用等风险来源。这是工程判断，不等于腾讯对本独立 C# 客户端作了授权或生产保证。

[腾讯微信软件许可及服务协议](https://weixin.qq.com/cgi-bin/readtemplate?lang=zh_CN&t=weixin_agreement&s=default)8.2.1.6限制非授权工具和相关自动操作；8.2.2保留接口开放范围决定权；8.5.1的违规处置包括功能限制、封禁及注销。开源代码的 MIT 许可不替代账号服务条款。

[微信个人账号使用规范](https://weixin.qq.com/cgi-bin/readtemplate?&t=page/agreement/personal_account&lang=zh_CN)页面显示 2026-04-29 更新/生效；1.2.6、1.2.7涉及非授权工具和规避保护，3.4说明异常大量消息或高频异常行为可能触发冻结或限制。本次读取了这两个微信官方页面正文；网页检索器失败后使用只读 HTTP 请求核实。

| 情况 | 评估 | 本程序处理 |
|---|---|---|
| 自己扫码，正常低频与自己的助理对话 | 采用官方通道，传统客户端逆向风险来源较少；独立接入资格仍需确认 | 绑定账号过滤、正常验证、无客户端注入 |
| 高频、批量或违规内容 | 官方账号规范明确存在限制、冻结等处置风险 | 保守节流，不提供群发/营销功能；不声称存在安全发送次数 |
| token 泄露、借用凭据、绕过校验 | 账号与数据风险较高，可能违反服务规则 | DPAPI 加密，不导出 token，不读取 WorkBuddy 凭据 |
| -14、401/403、二维码验证限制 | 可能是失效、权限、配对限制；不足以证明封号 | 停止并提示核对微信状态，保留本地记录 |
| 接口调整、灰度变化、账号无插件入口 | 可用性和兼容性风险，非必然账号处罚 | 版本锁定、来源记录、未知结构显式停止 |

未取得可成功读取的官方《微信ClawBot功能使用条款》独立全文。正式启用前，应查看手机插件授权页当前专项条款。本报告不把社区抄录当作已核实的官方规则，也不作法律结论。

## 7. 已实现能力与未实现能力

实现能力按 1.2.1 说明；旧手机媒体及原生语音观察按 1.2.0 历史保留，新版 Markdown、视频和文件发送验收见第 8 节。

| 能力 | 状态 |
|---|---|
| 本人扫码绑定、配对验证码、绑定路由处理 | 已实现；实际账号已本人扫码绑定；验证码/路由分支的 fixture 结果另列 |
| 绑定者文字接收、手动回复、显式 echo 演示 | 已实现；普通 wxe2e 与精确短口令 ok7 入站、对应手机回复已确认 |
| 游标恢复、持久收件箱、有限去重、未知送达记录 | 已实现 |
| 生命周期通知与错误分类 | 已实现 |
| 打字状态与保持生命周期 | 已实现官方 5 秒刷新和本地 TTL；新版真实 START/CANCEL HTTP 接受、退出 0，持续显示及消失尚未验证；旧版定时取消失败记录保留 |
| CDN 媒体 AES 上传/下载、图片/视频/文件 | 已实现；蓝色 MP4 出站手机接收及直接播放通过；后续真实入站 MP4 下载、完整本地解码成功，u8v 口令未匹配 |
| 语音接收、原生语音发送与音频附件 | 已实现原始语音下载及音频附件；原生 voice 为实验请求接口，合成 SILK、完整元数据 SILK 及真实手机 SILK 原样回发均 API 接受但手机未收到；MP3 原生未获送达确认 |
| Markdown 文本及 C# 调用函数 | 已实现过滤、持久分段恢复、媒体上传/下载及发送函数；真实一段 Markdown 已获手机正常接收确认，全部语法/长分段渲染未逐项验收 |
| 本地 SILK 编解码 | 已实现 PCM16/WAV→腾讯 SILK 与 SILK→WAV；旧真实样本手动解码为 2000 毫秒 WAV，修复后新手机语音自动解码为 2800 毫秒 WAV，完整流水及监听结束均通过；不保证其他编码或原生出站可用 |
| 语音识别与全格式音频解码 | 不包含本地语音识别；可显示服务端 voice_item.text，但不推定其他编码的解码能力 |
| 普通好友、群聊、通讯录、全量历史聊天 | 本次范围之外，未证明该通道允许或支持 |
| 长期运行 SLA、平台配额、独立客户端生产认证 | 没有可核实的一手保证 |

腾讯官方 [Channels 文档](https://cloud.tencent.com/document/product/1831/137055)说明产品通道只接受扫码绑定者的消息；本程序也做本地绑定者过滤。协议类型里的 group_id 不能作为已支持群聊的证据。

## 8. 验证结果

### 8.1 本版 1.2.1

最终流水目录为 artifacts/tests/final-v121-typing-lifecycle-verified。证据来自实际运行，按版本另存，旧证据文件没有覆盖。检查的是明确输入下的本地行为及已记录的实机窗口，不代表所有微信服务端场景。

| 层级 | 实际结果 | 证据及边界 |
|---|---|---|
| 锁定恢复、构建、发布及再次锁定恢复 | 全流程退出 0，Release 构建 0 警告/0 错误 | 当前源码与 Windows x64 成品可构建并运行 |
| 模拟 HTTP 与真实 Windows DPAPI 单元 | 8 套件通过，94 个具名组，StateVault 套件另计 | [单元汇总](evidence/unit-summary-v121.json)；21 个新生命周期组覆盖并发、TTL、失败保护、AUTH、清理预算及忽略取消的晚到结果 |
| 固定 Node/SILK 实际编解码 | 15/15，退出 0 | [codec 汇总](evidence/voice-codec-summary-v121.json)；合成音频、本地实际子进程互操作，不是微信语音送达证明 |
| 实际启动发布 EXE 的离线进程 | 62/62，退出 0 | [进程汇总](evidence/cli-e2e-summary-v121.json)；完全离线、不创建网络连接，包含新增 5 个生命周期及 1 个取消断连后 fresh-stop 案例 |
| 真实绑定账号 typing 60 秒可见窗口 | START/CANCEL HTTP 接受，退出 0，DPAPI 字节不变，无刷新失败诊断；第 16.6/55.4 秒有指示，CLI 结束后 2.1 秒正常标题 | [本轮 typing](evidence/desktop-typing-v121-visible-evidence.json)与[UI 观察](evidence/desktop-client-v121-visible-evidence.json)；未逐次记录刷新响应，不宣称每时刻连续显示或独立取消因果 |
| 新版精确文字 m3b | 完整消息与“已收到：m3b”直接可见，唯一匹配 Sent，Inbox 0，helper 退出 0 | [实机进程](evidence/desktop-text-v121-m3b-evidence.json)与[UI 观察](evidence/desktop-client-v121-visible-evidence.json)，使用与完整流水相同的成品摘要 |
| 新版 Markdown md7 | CLI 退出 0，88 字节/52 UTF-16 码元，作业 1/1、子键唯一 Sent；标题、加粗、行内代码和列表在电脑版可见 | [命令](evidence/desktop-media-v121-r2-commands.json)、[receipt 快照](evidence/desktop-media-v121-r2-receipts.json)、[UI 观察](evidence/desktop-media-v121-r2-ui.json)、[独立复核](evidence/desktop-media-v121-r2-review.json)；仅该样本 |
| 新版合成蓝色 MP4 | CLI 退出 0，2902 字节，文件 mvhd 为 3 秒，唯一 Sent；播放器从 00:00 到 00:03 | 与上行相同四份证据；仅当前合成视频，不使用静态首帧代替播放证明 |
| 新版 FILE 文件发送 | file7.txt 为 16 字节，CLI 退出 0、唯一 Sent；用户确认收到、能打开、内容正确 | [文件命令](evidence/file-v121-r2-commands.json)、[receipt 快照](evidence/file-v121-r2-receipts.json)、[一致性复核](evidence/file-v121-r2-review.json)、[客户端确认](evidence/file-v121-r2-client-confirmation.json)；发送时原快照的未确认字段保留，后续确认另存 |
| 早轮 UI 不可用与首次 k2a 窗口 | 保留未验证和无匹配结果，未覆盖 | [早轮 typing](evidence/desktop-typing-v121-evidence.json)、[UI 不可用](evidence/desktop-client-v121-ui-unverified.json)、[k2a 监听](evidence/desktop-text-v121-evidence.json)；幻灯/播放来源和锁屏未确认，k2a 未执行 UI 发送 |
| 原生语音气泡出站 | 未通过，服务端支持条件未知 | 保留旧失败；没有新的官方可用修复，不用 FILE 附件替代 |
| 真实断网恢复、跨机器迁移及长期账号观察 | 未执行 | 没有长期稳定性或免封证明 |

| 已测本版文件 | SHA-256 |
|---|---|
| weixin.exe | CB8F7466DEE8E2338703A24585E52852D70BAAE9FED5BC4B56CDD4D99160EBBF |
| weixin.dll | 116375DF8E469AE401916414736C94F44B82FD54DEF8C966D97FA3F0BEE339B7 |
| Weixin.Protocol.dll | 5C40767CEB907E6E0659A2C3E1BDC11A92936C4B80965C5DFEFD1546F71B80FD |

最终检查的 22 个源码条目及 21 个发布程序/codec 条目均与测试汇总中的 SHA-256、字节数匹配；这是所列条目的来源核对，不将数量扩张为全部分发条目。真实 typing 使用同一发布 EXE/CLI/协议 DLL。fixture 的假账号值和模拟票据仅在测试隔离目录使用，应用日志和状态不保存票据；真实凭据仍由 DPAPI 保护。报告与分发包不包含真实声音、视频、账号 token、CDN 密钥或桌面截图。

新版媒体证据明确分开：命令文件记录实际发布 EXE 的调用时间、退出码及三项成品摘要；不带 --exe 的 LiveE2E 仅在两次发送结束后读取 DPAPI 的指定业务键投影，JSON 中 exitCode 与程序摘要为 null，不把其退出 0 当作发送或客户端显示成功。独立复核验证 Markdown 根键/子键/样本摘要、视频及文件的声明指纹、唯一 Sent、attempt 时间窗口与 stdout 的对应关系；累计 Outbox Sent 数不是本轮新增数量。UI 使用上一轮直接观察时间，不分发截图。用户按 Esc 停止 Computer Use 后没有继续界面操作；这轮仅完善文件、证据及交付内容。

首轮完整进程为 **61/62**，[原汇总](evidence/cli-e2e-summary-v121-initial.json)保留。唯一失败是取消超时案例要求异步 fixture 取消轨迹必须先落盘，而进程在 10.386 秒按清理预算退出 2，明确打印“取消尚未确认”，没有成功响应或重试。测试已修订为实际 exit 2、超时诊断、时间范围、精确请求、无成功响应、状态不变及敏感输出保护；不宣称进程退出前必有耐久取消轨迹。在途刷新案例因明确等待其终态，仍严格要求取消轨迹在最终 CANCEL 请求前。独立复核及专项 1/1 通过后，重新运行完整 62/62，生产源码没有因放宽确认标准而变更。

### 8.2 旧版 1.2.0 实机与完整流水

完整结果按原文件保留于 [1.2.0 报告](REPORT-1.2.0.html)与[验证](VALIDATION-1.2.0.md)。旧版最后一次完整流水为 73 个具名组及 StateVault、codec 15/15、进程 56/56；更早的 53/53、中间失败和不同 helper DLL 来源没有删除。

旧手机 ok7 精确回复、Markdown 接收、蓝色三秒 MP4 直接播放获得确认；真实入站语音/视频完成下载，修复后新手机语音自动生成 2800 毫秒 WAV。旧电脑版 d9k 完整文字回复和旧蓝色视频播放到 00:03 直接观察到。u8v 标记未匹配、原生 SILK 多次无气泡、旧 60 秒 typing 取消断连退出 2、随后显式 stop HTTP 接受退出 0分别记录；指示器在 stop 前已消失，不能归因此前消失。旧 24de5c9 官方媒体路由和未合并 PR 的研究边界见 4.4 节。**上述历史成功、失败和 helper 字节均不自动算作 1.2.1 线上复测。**

## 9. 真实账号验收与长期投产观察

1. 确认当前手机微信有 ClawBot/助理插件入口及相应授权条件，阅读手机显示的条款。
2. 在 Windows 成品目录运行 `weixin.exe login --open`，自己扫码并确认；若要求验证码，输入手机显示的码。
3. 运行 `weixin.exe listen`，从绑定微信发一条文字；确认终端显示，停止后运行 `weixin.exe send`，确认手机收到回复。
4. 用 `listen --echo --run-for 300` 验证编号消息的连续双向文字；重启程序后再发新的三个字母短口令，核对对应回复和持久状态。真实断网/恢复若未执行，应单列未验证。
5. 用 `status` 核对 Unknown；遇到未确认送达先在微信检查，再决定是否人工重新发送。
6. 正式长期使用前，建议按实际频率至少观察 24 小时，记录接收/发送数量、重连次数、Unknown 和权限错误；有错误保留 DPAPI 文件及脱敏结果，不公开凭据。

以上为可复现的现场验收方法；已执行部分以第 8 节及 VALIDATION.md 为准。24 小时是本报告提出的投产观察建议，原始用户没有指定该时长，本轮尚未完成，不能写成通过。实际业务接入应按 InboxEntry.Key 做幂等，并保持单实例轮询。

## 10. 交付文件索引

| 文件或目录 | 内容 |
|---|---|
| src/Weixin.Protocol | C# HTTPS 协议、消息模型、持久 Runner、DPAPI Vault |
| runtime/voice | 固定本地 Node/SILK 编解码运行时、二进制桥接及第三方原许可 |
| src/Weixin.Cli | 扫码页面、login/listen/send/status/demo/probe 命令 |
| tests/Weixin.Protocol.Tests | 无测试框架依赖的离线测试入口与故障案例 |
| tests/Weixin.Cli.E2ETests | 真实启动发布 EXE 的完全离线进程测试与可复核汇总 |
| tests/Weixin.VoiceCodec.Tests | 合成音频上的实际固定运行时编解码、逐字节互操作与进程取消测试 |
| tests/Weixin.LiveE2E | 显式真实账号验收辅助程序；--native-silk 为单次完整元数据实验模式，不属于发布 CLI 离线案例 |
| research/official-evidence.md | 官方产品、协议与账号规范的一手证据 |
| research/github-evidence.md | 固定提交、字段行链接、旧 WorkBuddy 通道分析 |
| research/upstream-snapshot | 腾讯关键源码及 MIT 许可证的固定快照 |
| research/typing-lifecycle-snapshot | 本版引用的腾讯调用器与固定 OpenClaw 生命周期原文、原许可及逐文件摘要 |
| docs/VALIDATION.md | 实际验收输出及未执行项目 |
| docs/evidence | 单元、离线进程及真实账号结果的脱敏证据 |
| scripts/Test-All.ps1 | 锁定恢复、构建、单元、本地 codec、发布、进程测试与再次锁定恢复的完整检查 |
| scripts/Make-Delivery.ps1 | 构建与源码/Windows 成品打包脚本 |
| README.md | 运行和 SDK 集成说明 |

本次交付公开协议兼容实现、单元及进程测试和真实账号联调记录；各层证据的边界独立说明。长期运行、跨设备迁移与账号限制风险仍须结合实际使用观察。
