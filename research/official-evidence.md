# 腾讯官方微信连接证据

核验日期：2026-10-04（Asia/Shanghai）。本文件只将腾讯官网、腾讯云产品文档、CodeBuddy/WorkBuddy 官方文档和 Tencent 官方 GitHub 仓库作为事实依据。腾讯云开发者社区用户文章不自动等于腾讯官方承诺。

本版另于北京时间 08:42 复核正式 main、稳定 v2.4.9 与 npm latest，08:48 复核 PR #282 的不同发送入口；完整地址、提交与文件 SHA-256 保存在[语音复核记录](../docs/evidence/official-native-voice-refresh-v121.json)。正式 main/stable 的音频媒体仍走 FILE；未合并 PR 的 channel audio→TEXT、直接媒体 audio→FILE 和残留 VOICE helper 分别记录，不将提案代码当作正式支持合同，也不推断全部账户均不支持。

| 编号 | 标题与 URL | 页面日期 | 核验事实 |
|---|---|---|---|
| O1 | [WorkBuddy 接入微信助理指南](https://www.codebuddy.cn/docs/workbuddy/WeixinBot-Guide) | 未显示可读更新日期 | 支持手机微信向本地 WorkBuddy 发任务并接收结果；要求 WorkBuddy ≥4.6.4、微信 ≥8.0.70，账号同一或关联，扫码绑定，无需 App ID/App Secret；电脑须持续运行。 |
| O2 | [助理（远程任务）](https://www.codebuddy.cn/docs/workbuddy/From-Beginner-to-Expert-Guide/Function-Description/Assistant) | 未显示可读更新日期 | 微信助理、微信客服号、企微助理是分别列出的接入方式；远程指令有来源校验，删除文件、改系统配置和命令审批等须确认；解绑删除本地接入配置但保留任务记录。 |
| O3 | [微信 ClawBot 接入指南](https://cloud.tencent.com/document/product/1831/137450) | 2026-09-07 10:38:31 | CodeBuddy IDE 提供微信 ClawBot 扫码接入，微信 ≥8.0.70，无需开发者凭证。此文是 CodeBuddy 产品文档，不应把其产品行为全部等同于 WorkBuddy 或裸 API。 |
| O4 | [Channels [Beta]](https://cloud.tencent.com/document/product/1831/137055) | 本次未记录明确页面更新时间 | CodeBuddy Code 内置微信 channel，经 ClawBot 双向收发文本、图片、文件；微信端在“我→设置→插件→ClawBot”启用；只有扫码绑定账号的消息被接受，其他人消息静默丢弃。 |
| O5 | [Tencent/openclaw-weixin](https://github.com/Tencent/openclaw-weixin) / [中文 README](https://github.com/Tencent/openclaw-weixin/blob/main/README.zh_CN.md) | main 动态分支，须以锁定提交为正式基线 | 腾讯官方公开微信渠道插件源码；扫码授权后本地存凭证；长轮询；文本及媒体；单 Gateway 多账号及会话隔离；自定义 bot_agent 用于观测，不是鉴权或路由凭证。 |
| O6 | [微信后端 API 协议](https://github.com/Tencent/openclaw-weixin/blob/main/docs/protocol_zh_CN.md) | main 动态分支，须以锁定提交为正式基线 | 官方已公开插件的 HTTP/JSON 后端协议与源码索引；明确区分客户端行为、数据格式与接入建议，警告客户端类型不能代表完整服务端契约。 |
| O7 | [MIT License](https://github.com/Tencent/openclaw-weixin/blob/main/LICENSE) | main 动态分支 | 仓库采用 MIT；分发源码时保留版权与许可。开源代码许可证不能替代微信服务使用条款或免除账号规则。 |
| O8 | [腾讯微信软件许可及服务协议](https://weixin.qq.com/cgi-bin/readtemplate?lang=zh_CN&t=weixin_agreement&s=default) | 本次未发现明确页面更新时间 | 8.2.1.6 限制非腾讯开发/授权的工具注册、登录、使用、自动化或访问读取控制微信；8.2.2 腾讯保留服务和接口开放范围决定权；8.5.1 违规处置可包括限制功能、账号封禁、注销和回收。 |
| O9 | [微信个人账号使用规范](https://weixin.qq.com/cgi-bin/readtemplate?&t=page/agreement/personal_account&lang=zh_CN) | 更新及生效：2026-04-29 | 1.2.6 包括未经授权工具及自动化限制；1.2.7 禁止规避或破坏安全保护；3.4 异常大量信息或高频异常行为可触发冻结或其他限制。 |

## 已确认的边界

1. 个人微信目前有腾讯提供的 ClawBot/微信助理连接路线，不能继续沿用“个人微信完全没有官方 Bot 通道”的旧结论。
2. WorkBuddy 的微信助理功能用于绑定者控制自己的电脑上任务。没有上述官方文档证据支持把它理解为任意好友/群聊代发、好友通讯录读取、普通微信全部聊天记录查询或客户端自动化。
3. 已有官方公开协议文档和源码；从这些资料实现 C# HTTP 客户端的技术路线比解包 WorkBuddy 获得私有实现更可复核。该结论是工程选择，不构成腾讯对所有独立客户端或所有用途的授权声明。
4. 官方协议文档明确：group_id 等字段的存在不能证明插件已支持相应功能；发送器缺少 context_token 时继续发送也不能证明服务端接受。源码兼容和真实账号服务端验证必须分开表述。
5. 微信客服/服务号、企业微信 Bot ID/Secret/WebSocket 路线是其他服务，不能用其公开性证明个人微信 ClawBot 的任意能力。

## 风险判断与未确认事项

- 使用官方 ClawBot 通道、正常扫码授权并仅处理绑定账号自己的消息，可减少传统客户端注入、协议模拟、自动操作客户端造成的风险来源；这不是“零封号”保证。
- 高发送频率、异常大量消息、违规内容、绕过校验、泄露或借用凭证、超越开放范围等行为仍有被限制、阻断或处置的风险。没有一手公开统计足以计算封号概率。
- 没有找到并成功读取《微信ClawBot功能使用条款》的官方独立页面。GitHub 社区抄录不能作为本报告已核实的一手条款；正式启用前应以手机插件授权页展示的当前专项条款为准。
- 未确认公开速率配额、SLA、独立 C# 实现的生产级认可、普通好友/群聊任意收发，以及 Android 账号当前具体放量范围。旧的“仅 iOS 灰度”文章不能替代当前账号入口验证。
- 本文件记录最初的公开资料核验阶段；该阶段没有扫码或账号消息测试。后续独立 C# 客户端已在本人扫码授权的绑定账号执行测试，准确结果和未通过项见 docs/VALIDATION.md，不能将这里的研究阶段范围当成整个项目的当前验收状态。

## 抓取说明

O1–O7 由网页检索/直接打开核验。O8、O9 的网页检索器报内部错误，但同一官方 URL 的只读 PowerShell Invoke-WebRequest 返回 HTTP 200 并读取到条款正文；O9 读取到明确的 2026-04-29 日期。不能将网页检索器失败误报成官网不存在。
# 1.2.1 补充：官方打字生命周期

腾讯固定提交 24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c 的 [process-message.ts:357](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/process-message.ts#L357)指定 keepaliveIntervalMs=5000。插件 package.json 将开发依赖固定到 OpenClaw 2026.8.1；本次核对该 tag 对应 commit ea806575e6450e4d1efdfc72c19f04be982a1b9b，不能从 peer 范围推定全部版本一致。

该固定 SDK 的 [typing.ts](https://github.com/openclaw/openclaw/blob/ea806575e6450e4d1efdfc72c19f04be982a1b9b/src/channels/typing.ts#L50)在初次 START 成功后启动周期刷新，默认本地 TTL=60000ms；[定时器](https://github.com/openclaw/openclaw/blob/ea806575e6450e4d1efdfc72c19f04be982a1b9b/src/channels/typing-lifecycle.ts#L32)不重叠在途请求，[连续失败保护](https://github.com/openclaw/openclaw/blob/ea806575e6450e4d1efdfc72c19f04be982a1b9b/src/channels/typing-start-guard.ts#L41)在成功后清零，两次连续失败停刷新；停止只发送一次 CANCEL。TTL 是 SDK 本地保护，不是微信服务器时长限制。官方停止循环不取消已经在途的 START；C# 取消并等待在途请求属于本地并发保护差异。

完整原文、原许可、通知与逐文件摘要见 [生命周期快照清单](typing-lifecycle-snapshot/manifest.json)；本次没有执行或安装上游 SDK。这些是第一方源码证据，运行验收在 docs/VALIDATION.md 单独记录。

