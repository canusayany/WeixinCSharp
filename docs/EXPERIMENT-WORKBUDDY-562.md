# WorkBuddy 5.6.2 安装包逆向核查

核查日期：2026-10-04，Asia/Shanghai。输入是用户提供的 `WorkBuddy-win32-x64-user-5.6.2.39298511-37a65c0b.exe`。本次完成安装包解包、微信调用链审阅，以及原始函数的局部离线执行。

**这个安装包中已追踪的个人微信助理发送路径没有实现原生 VOICE 外发。MP3、WAV、SILK 都作为文件附件发送。** 包内的企业微信语音发送、桌面语音输入和 TTS 属于其他路径，不能用来证明微信助理能发语音气泡。

## 安装包和提取范围

| 项目 | 核对结果 |
| --- | --- |
| 安装器 | 530,927,840 字节；Windows Authenticode 状态 Valid，签名者为 Tencent Technology (Shenzhen) Company Limited |
| 安装器 SHA-256 | `627e5a565436d0876740af69c2747759648662c52958d2a5df1ba330a82c3025` |
| 包装 | NSIS-3 Unicode → 内嵌 app-64.7z → Electron app.asar |
| 应用 manifest | `@genie/workbuddy-desktop`，version `5.6.2`，main `main/index.js` |
| app.asar | 317,114,821 字节；SHA-256 `c4304eec1f8849ea16b9492f02dcc5d1e93be4a7aed184b7effaabd2f06c9d22` |
| 解包工具 | [7-Zip 官方 26.03](https://github.com/ip7z/7zip/releases/tag/26.03)，下载文件与官方 release digest 一致 |
| ASAR 检查 | 17,800 个内部文件、2,753 个外置文件；内部文件的内嵌 SHA-256 全部通过 |

完整构建号和尾部 commit 来自安装器文件名；应用 manifest 与版本资源实际确认到 5.6.2，没有把文件名当成独立验证的源码提交。安装清单的生成时间为 2026-09-21，官方[更新日志](https://www.workbuddy.cn/docs/workbuddy/Changelog)也将 5.6.2 列于该日。

43 个外置文件与 ASAR 索引声明的大小及 hash 不一致，包含 `.node`、`.exe` 和 `package.json`。提取保留安装包的实际字节，没有修正或替换；各项构建原因未逐个确定。核心 `main/server.js` 属于内部文件，其 ASAR 内嵌 hash、提取清单 hash 和实际文件 hash 三者一致。

没有运行安装器、完整 WorkBuddy、附带 CLI 或原生插件，也没有读取 WorkBuddy 用户配置、登录缓存或聊天记录。原安装包和专有代码仅保留在本地研究目录，不进入开源仓库或交付包。

## 微信助理怎样连接

实际微信助理插件是 `weixinClawBot`，由 `ensureBuiltinPluginsRegistered` 注册。主要实现集中在 `main/server.js`，带有原始模块路径标记：`weixin-types`、`weixin-api`、`weixin-auth`、`weixin-media`、`weixin-client`、`weixin-plugin`。

连接流程是 iLink Bot：扫码取得自己的 `bot_token`、Bot ID、用户 ID 和 API base URL，随后 `getupdates` 长轮询；回复使用入站 `context_token`，发往绑定用户。默认 API 为 `https://ilinkai.weixin.qq.com`，媒体 CDN 为 `https://novac2c.cdn.weixin.qq.com/c2c`。

| 调用点 | 本包显式实现 |
| --- | --- |
| 取得二维码 | GET `get_bot_qrcode?bot_type=3` |
| 等待确认 | GET `get_qrcode_status`；显式设置 `iLink-App-ClientVersion: 1` |
| 业务 API | POST getupdates / sendmessage / getconfig / sendtyping |
| 业务请求头 | JSON、AuthorizationType=`ilink_bot_token`、Bearer token、Content-Length、随机 X-WECHAT-UIN |
| X-WECHAT-UIN | 随机 uint32 转十进制 UTF-8 文本，再 Base64 |
| base_info | `channel_version=workbuddy-desktop-1.0.0`，此模块不添加 bot_agent |
| getuploadurl | 独立 helper；显式构造 JSON、AuthorizationType、Content-Length 和可选 Bearer，不设置 X-WECHAT-UIN |
| 消息信封 | from_user_id 空字符串、绑定 to_user_id、client_id、message_type=2、message_state=2、context_token、item_list |

上表是函数调用点的构造结果，没有对完整应用做实机抓包，不概括 Electron、全局网络包装或 HTTP 栈可能添加的所有请求头。WorkBuddy 的二维码方法、版本元数据及应用标识与公开 openclaw-weixin 2.4.9 不完全相同；它不是该 npm 包的逐字副本。两者使用同类 iLink 接口和媒体结构，但仅凭这些相似性不能证明内部代码复用或权限相同。

包内还分别注册了 `wechatkf`、`wechatmp` 等通道，并包含 Centrifugo 和 COPILOT_RESPONSE 后端回复逻辑。早期第三方 WorkBuddy 逆向所描述的后台 WebSocket 路线，不能代替这版 `weixinClawBot` 的直接 iLink 实现。微信客服号、小程序和个人微信助理需要分别看待。

## 音频如何发出去

桌面发送链已经逐层追踪：

`WeixinClawBotOutboundAdapter.send → sendReply → sendMediaReply → inferMediaType → uploadMediaFile → getUploadUrl / CDN → buildMediaMessageItem → WeixinApi.sendMessage`

`sendReply` 合并文本中提取的文件与任务产物，先发送文字，再逐个上传媒体。`inferMediaType` 只有 image、video、file 三种结果。MP3、OGG、WAV 的 MIME 虽然是 audio/*，仍落到 file；SILK 的 MIME 是通用二进制，同样落到 file。

| 桌面判定 | 上传 media_type | 消息项 | 主要字段 |
| --- | --- | --- | --- |
| 图片 | 1 | IMAGE=2 | image_item.media、mid_size |
| 视频 | 2 | VIDEO=5 | video_item.media、video_size |
| 其他，含音频/SILK | 3 | FILE=4 | file_item.media、file_name、len 字符串 |

上传使用 AES-128-ECB / PKCS#7。消息的 AES key 是 `Base64(UTF8(32位十六进制密钥文本))`，下载参数取自 CDN 的 `x-encrypted-param`。这些行为与现有 C# 文件、视频路径的实现一致。

代码定义了入站 VOICE=3 和上传 VOICE=4 枚举，但上传映射和外发 item 工厂没有对应语音分支。追踪过的微信发送路径没有构造 `voice_item`，也没有出站 SILK 编码器。不能把枚举存在当作能力已经实现。

附带 `cli/dist/codebuddy-headless.js` 也做了独立核查：`WeChatReply → WechatChannelBridge.sendFileReply → WechatClient.sendFileReply → uploadMedia`。其非图片文件，包括音频和视频，统一上传 3、发送 FILE=4；没有找到另一条原生语音出口。这个 CLI 是否被桌面微信助理实际选用，未通过完整应用执行确认。

同一个 bundle 中的 `WecomAiBotOutboundAdapter` 确实把 amr/mp3/wav/ogg/silk 归为 voice，使用企业微信 WebSocket 的 uploadMedia / sendMediaMessage。它的通道是 `wecomaibot`，不能把这段逻辑套进个人微信 iLink。

## 局部执行：六组离线验证

从已校验的 server.js 按模块边界原样取得 types、api、media、client 四段代码，在 Node v24.19.0 中执行。只追加导出探针，不修改原始函数。构造 client 和 api 后注入合成会话，没有调用 connect、登录或轮询。

fetch 完全替换为模拟响应，不委托真实网络；原始模块只能读取四个合成样本，mkdir 使用虚拟实现，写文件调用会报错。crypto 使用真实 Node 实现，CDN 捕获的密文被重新解密并与输入逐字节比较。

| 执行案例 | 结果 |
| --- | --- |
| 原版 sendMediaReply 发两秒 MP3 | 上传 3 / FILE=4，无 voice_item |
| 原版 sendMediaReply 发两秒 SILK | 上传 3 / FILE=4，无 voice_item |
| 原版 sendMediaReply 发两秒 WAV | 上传 3 / FILE=4，无 voice_item |
| 原版 sendMediaReply 发短视频 | 上传 2 / VIDEO=5 |
| 入站 VOICE 无文字，调用原版 downloadMediaItem | 返回 null，未发下载请求 |
| 入站 VOICE 带合成 ASR 文字，调用原版 extractText | 提取已有文字，没有解码音频 |

六组断言通过，十二个模拟 HTTP 调用，真实网络请求为零。这确认了本包代码实际产生的分支和消息结构，**不是六次真实微信送达或播放验收**。没有新增 WorkBuddy 实机发送结果，也没有覆盖此前 C# / 官方 Node 试发未见语音气泡的记录。

## 入站语音、文字和限制

桌面 `extractText` 会直接取 `voice_item.text`。上层虽能识别没有文字的语音媒体，实际 `downloadMediaItem` 仅处理 IMAGE、VIDEO、FILE，最后返回 null；无其他文字或成功附件时，后续调度跳过该消息。CLI 副本可以保存原始 SILK，但没有对应解码流程。官方[微信助理指南](https://www.workbuddy.cn/docs/workbuddy/WeixinBot-Guide)所说的语音指令，也明确依赖微信语音转文字；它没有承诺外发语音气泡。

| 项目 | 本包值和边界 |
| --- | --- |
| 桌面文字分段 | 3800 UTF-8 字节，段间 300 ms |
| Markdown | 插件声明 sendMarkdown；普通插件回复保留 Markdown 后分段，媒体 caption 转纯文本 |
| 桌面媒体大小 | 明文最多 104857600 字节（100 MiB），来自客户端 guard |
| getuploadurl | 15 秒超时 |
| CDN 上传 | 最多 3 次；4xx 不重试，该 helper 未设置 CDN 请求超时或退避 |
| 音频/视频时长 | 未找到此路径的专门上限 |
| 语音采样率、位深 | 未找到此路径的校验，也没有原生外发字段 |

这些值是该版本的客户端实现，不能当作微信服务器的统一硬上限。3800 UTF-8 字节也不同于 openclaw-weixin 2.4.9 的 4000 UTF-16 码元配置；报告保留两种来源和单位，不把它们混写。

还有一个结果边界：`sendMediaReply` 失败时返回 false，上层没有检查这个返回值，所以插件返回 success 不能证明每个附件已送达。HTTP 成功同样不能证明客户端出现气泡或能播放。

## 复查索引和对 C# 项目的影响

| 原始文件 | SHA-256 |
| --- | --- |
| main/server.js | `ba99f1dc4ca233d25d5cb1f68273b1c07e3fce96b151bed0f524ba5adcf1fef6` |
| cli/dist/codebuddy-headless.js | `83d19d8462e5dfc7e6ffb02528df5d89a2a502354270ccf5f5f456a4b068cfbb` |

[结构化证据](evidence/workbuddy-562.json)保留模块 SHA、函数的零起始 UTF-16 偏移、路由、限制及脱敏模拟请求。关键桌面位置：uploadMediaFile 2640342、buildMediaMessageItem 2644898、inferMediaType 2645646、sendMediaReply 2653843、外发 adapter 2681902。偏移只适用于上列精确文件，不是行号。

本次没有发现可移植的个人微信原生语音发送方法，因此没有加入一套声称能够发气泡的 C# 实现，也没有把企业微信的语音接口混进来。现有程序继续按腾讯公开 iLink 实现工作，保持纯 C#。WorkBuddy 的专有 bundle、原生组件和研究运行时不发布；已有 1.3.0 ZIP 与版本标签保留原字节。

WorkBuddy 自己的版本或 client_id 标识不能证明第三方获得相同授权、权限或风控待遇，复制字段也不能保证不封号。此包将 errcode=-14 视为会话失效，不是封号结论；本次未连接真实微信账号，也没有长期运行样本，无法评估封号概率。

结论只覆盖这个安装包中已追踪的本地微信助理与 CLI 实现。服务端是否存在另行授权的 VOICE 能力、其他版本如何实现，仍未确定。
