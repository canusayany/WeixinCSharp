# 1.2.1 实际验证记录

日期：2026-10-04（Asia/Shanghai）。本机 Windows x64，.NET SDK 10.0.103，Runtime 10.0.3。此记录对应 **1.2.1** 的生产源码与发布程序。1.2.0 的完整测试及实机细节保存在 [历史验证记录](VALIDATION-1.2.0.md)，旧记录与归档保持原样。

本版修复 typing 生命周期，已完成新的统一流水：**94 个具名单元测试组加 StateVault 套件，八个套件通过；本地 codec 15/15；发布 EXE 离线进程端到端 62/62**。新的真实 60 秒 typing 在约 16.6 秒和 55.4 秒两次 UI 样本中可见，结束后约 2.1 秒恢复正常标题；开始/最终取消获 HTTP 接受、退出 0，不推断逐毫秒持续显示或取消的独立因果。新版 m3b 已精确收到、对应唯一 Sent 回复，CLI/辅助程序退出 0、Inbox=0，电脑版可见完整回复；初轮 k2a 未发送验收文字、未匹配记录保留。新版 md7 Markdown 卡完整可见，新的三秒合成视频实际重播到结束，命令及持久回执分别记录。原生 VOICE 出站仍没有成功证据，不能宣称全部目标完成。

## 构建与成品来源

本版实际完整执行：

```powershell
& .\scripts\Test-All.ps1 -OutputDirectory artifacts/tests/final-v121-typing-lifecycle-verified
```

完整流程退出 0：锁定依赖恢复 → 全 solution Release 构建（0 警告、0 错误）→ 单元与 StateVault 套件 → 本地 codec → Windows x64 自包含发布 → 发布 EXE 的离线 CLI 进程测试 → 发布后再次锁定恢复。后一次 locked restore 通过，确认发布未造成锁文件漂移。

最终进程汇总于 **2026-10-04 08:32:51.9375273 北京时间**完成。[本版进程证据](evidence/cli-e2e-summary-v121.json)记录实际启动的发布文件与源码 provenance；22 个跟踪源码文件及 21 个发布文件的哈希、发布文件大小已实际核对一致。

| 文件 | 本版 SHA-256 |
|---|---|
| weixin.exe | CB8F7466DEE8E2338703A24585E52852D70BAAE9FED5BC4B56CDD4D99160EBBF |
| weixin.dll | 116375DF8E469AE401916414736C94F44B82FD54DEF8C966D97FA3F0BEE339B7 |
| Weixin.Protocol.dll | 5C40767CEB907E6E0659A2C3E1BDC11A92936C4B80965C5DFEFD1546F71B80FD |

旧版 07:22:01 的 73/15/56 及旧生产摘要只见 [1.2.0 记录](VALIDATION-1.2.0.md)，不作为本版验收。新公开 JSON 使用 -v121 名称，不覆盖旧证据。

复现时使用新的 OutputDirectory，避免覆盖已有证据。测试项目是 Console 程序，执行 dotnet run 才会运行检查，dotnet test 不执行这些检查；CLI 进程测试必须指向实际发布的 weixin.exe。

## 单元与本地 codec

[单元证据](evidence/unit-summary-v121.json)记录本版于 **08:31:08 至 08:31:13**执行，八个套件全部通过，退出 0。

| 套件 | 本版结果 |
|---|---|
| ILinkClientTests | 12 个具名组通过 |
| PersistentBotRunnerTests | 12 个具名组通过 |
| RunnerRegressionTests | 10 个具名组通过 |
| MediaModelAndMarkdownTests | 19 个具名组通过 |
| MediaClientTests | 7 个具名组通过 |
| TypingClientTests | 13 个具名组通过 |
| TypingLifecycleTests | 21 个具名组通过 |
| StateVaultTests | 套件通过，未逐组输出计数 |

前七项合计 **94 个具名测试组**，StateVault 另计为已通过套件，不编造其中的组数。单元测试使用模拟 HTTP 和真实 Windows DPAPI，不连接微信账号。既有结果码、身份与收件过滤、原子保存、业务摘要及 Unknown 恢复、媒体 AES/大小/CDN、Markdown 分段和 typing primitive 检查本版重新执行。

新增 21 个生命周期组覆盖 START/STOP 重复调用、5 秒默认刷新、跳过重叠 tick、成功清零及两次连续失败停止刷新、Retry-After、60 秒本地 TTL、停止在途刷新并等待、总清理预算、鉴权失败停止、不合作 CANCEL 的有界等待和晚到成功不能改写确认。状态、默认序列化及诊断不暴露票据。

[本版 codec 证据](evidence/voice-codec-summary-v121.json)于 **08:31:15**完成，**15/15 通过，0 失败，退出 0**，独立于上述 94 组。实际运行固定 Node.js 24.19.0/silk-wasm 3.7.1，使用合成音频，账号状态访问为 false、网络请求为 0。覆盖 PCM/WAV 编码、SILK 解码、固定上游 API 的独立逐字节互操作、末帧/短输入补齐、格式/容量/时长保护、运行时错误、实际子进程取消与超时。来源和完整许可保留在 runtime/voice/provenance.json；本地编解码通过不证明原生语音气泡可送达。

## 发布 EXE 的离线进程端到端

[本版最终进程汇总](evidence/cli-e2e-summary-v121.json)为 **62/62 通过，0 失败，退出 0**；原始目录为 artifacts/tests/final-v121-typing-lifecycle-verified/cli-e2e。汇总明确 realWeChatDeliveryVerified=false，不能将 fixture 结果当作真实服务端或手机验收。

测试实际启动发布 EXE，通过 stdin、UTF-8 文件、进程退出、Windows DPAPI 与持久状态断言，重新执行原有扫码状态机、文字过滤/echo、重启去重、client_id、发送中强制终止及 Unknown、不重发、锁与会话失效、媒体上传下载、CDN/大小保护、Markdown 恢复、typing 和账号无关 codec 案例。

本版增加六项进程回归：

- **CANCEL 断连后显式恢复**：短于 5 秒的窗口中 START 受理，finally CANCEL 模拟断连，CLI 退出 2，安全连接失败诊断且无取消成功确认。新 CLI 进程复用同一专用假 DPAPI 绑定，只发一次显式 CANCEL、退出 0；无聊天、隐式重试或重新 START，状态摘要与 Outbox 不变，票据不入状态或应用日志。
- **5 秒 keepalive**：6.2 秒窗口内验证初始 START、约 5 秒的刷新和最终一次 CANCEL；fixture 字段、顺序及时间断言共同区分旧单次 START。
- **停止在途刷新**：受控延迟中的刷新实际取消，取消观察先于最终 CANCEL，没有成功完成的旧刷新或后续 START；清理等待有界。
- **两次连续刷新失败**：16.2 秒窗口内两次模拟断连后关闭后续 tick，最后仅一次 CANCEL，没有额外 HTTP 重试。
- **刷新鉴权失败**：退出 2，不隐式 CANCEL 或发聊天，不吞错误返回成功。
- **CANCEL 清理预算**：超时退出 2，不输出成功确认或重试，最终请求与状态安全断言成立。

本版首次完整进程测试 **61/62 通过**，原失败保存在 [首次汇总](evidence/cli-e2e-summary-v121-initial.json)。唯一失败为 typing_cancel_deadline_exits_two_without_success_or_retry：生产 CLI 已因 CANCEL 预算耗尽而退出 2，测试却要求异步 fixture 的 canceled 轨迹必须在进程退出前落盘。修正的是测试观测合同，生产代码没有改变；经过独立复核、该案单独 1/1 通过，再完整运行得到 62/62。原失败保留，不将修正后成功改写为首次全部通过。

1.2.0 中间的 41/42、52/53、旧 DLL 占用构建失败，以及之后的历史成功都在 [旧版验证记录](VALIDATION-1.2.0.md)保留，不混入本版总数。

## 官方合同与本地修复

[官方生命周期合同证据](evidence/typing-lifecycle-official-contract.json)属于 primary-source 研究，明确不是运行验收。固定腾讯插件 commit 24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c、插件版本 2.4.9，以及其开发依赖 OpenClaw 2026.8.1 的 commit ea806575e6450e4d1efdfc72c19f04be982a1b9b；peer 版本范围不证明所有版本行为相同。原文、MIT 许可及 manifest 保存于 research/typing-lifecycle-snapshot。

腾讯回调对应的 OpenClaw 生命周期默认每 **5000 毫秒**以 START 刷新，跳过重叠 tick，成功清零失败计数，连续两次失败后停止刷新，并由停止阶段调用 CANCEL。其 **60000 毫秒 TTL 是 SDK 本地生命周期保护**，不是已核实的微信服务端配额、显示 TTL 或最大连续发送时长；HTTP primitive 不自动重试，也没有公开的 CANCEL 幂等性/安全重试保证。

本 C# 版本额外补充并发保护：停止时取消并等待在途 START/刷新；若鉴权正常且清理预算允许，再发最多一次最终 CANCEL，避免 CANCEL 之后迟到 START。**总清理预算为本地 10 秒**，同时约束等待在途工作及 CANCEL。transport 忽略取消时也不能无限等待；逾期明确失败，晚成功不能改写 CancelAccepted 或取消确认。鉴权/会话错误不触发隐式后续网络请求，Stop/Dispose 不把错误吞成成功。

官方记录 officialStopCancelsInflightStartRequest=false，因此取消并等待及有界清理是本实现的工程保护，不冒称逐行复刻。离线回归证明本版这些行为，不推定未知服务器对取消的幂等性或重试语义。

## 新版真实 typing 与文字状态

最终发布 EXE 实际运行 60 秒 typing，时间为 **08:33:03.985 至 08:34:04.611 北京时间**。[新版实机证据](evidence/desktop-typing-v121-evidence.json)的 EXE/CLI/协议 DLL 摘要与上表一致：开始和最终取消请求被 HTTP 接受、CLI 退出 0、DPAPI 状态字节未变、未观察到刷新失败诊断。

individualLiveRefreshResponsesInstrumented=false：本次没有逐次记录真实刷新请求的响应，不能由“没有失败诊断”推出每一次刷新均获得单独验收。实际 5 秒请求周期和清理行为已有上述发布 EXE 离线回归，但这些不是手机显示证据。

此轮三次捕获呈现幻灯/播放画面、聊天不可见，未确认遮挡来源或锁屏；不将原因记为已核实的“屏保”。[UI 未验证记录](evidence/desktop-client-v121-ui-unverified.json)仅保存观察状态，不含截图。该轮持续显示和消失均未验收，后续可用窗口另列，不能覆盖这次记录。

后续最终发布 EXE 的 60 秒窗口为 **08:43:00.825 至 08:44:01.488 北京时间**，[新可见窗口请求证据](evidence/desktop-typing-v121-visible-evidence.json)的三个程序哈希均与本版最终产物一致，记录 START/最终 CANCEL 获 HTTP 接受、CLI 退出 0、DPAPI 状态字节不变、无刷新失败诊断，individualLiveRefreshResponsesInstrumented=false。没有逐次插桩记录真实刷新响应，这一边界仍保留。

微信 **4.1.15.13** 的 [可见窗口 UI 观察](evidence/desktop-client-v121-visible-evidence.json)在 **08:43:17.467** 和 **08:43:56.233** 两次样本中显示“对方正在输入…”，分别距开始约 **16.6 秒、55.4 秒**；**08:44:03.576**，距 CLI 结束约 **2.1 秒**，标题恢复正常。两次后期显示及结束后恢复已有直接观察，不宣称整个窗口逐毫秒持续显示；恢复标题发生在结束后也不足以独立证明是 CANCEL 导致，不能排除其他生命周期/客户端行为。

初轮 k2a 监听为 **08:44:41.210 至 08:45:26.739**，结束前没有执行 UI 发送，expectedMessageObserved=false、匹配回复为 0，记录见 [初轮文字证据](evidence/desktop-text-v121-evidence.json)。CLI 退出 0 不能将该未完成窗口登记为口令验收通过；保留原失败窗口，换新标记独立测试。

后续 m3b 监听为 **08:46:54.910 至 08:47:40.388**；**08:47:13.302** 发送短口令 **m3b**，**08:47:23.378** 的电脑版观察可见自身完整 m3b 和 Bot 完整“已收到：m3b”。[监听结束证据](evidence/desktop-text-v121-m3b-evidence.json)记录 expectedMessageObserved=true，expectedReplyReceipts 恰好一条且状态 Sent，内容 SHA-256 与完整回复对应、Inbox=0、CLI 退出 0；[综合 UI 记录](evidence/desktop-client-v121-visible-evidence.json)另确认辅助程序退出 0 及完整回复可见。Seen=10、Outbox Sent=14 是既有状态累计值，不是本次新增消息/回复数量。这次三项程序哈希均与本版最终产物一致，实机文字验证覆盖本版生产字节。

旧 d9k、ok7 文字确认、媒体接收/播放和自动 WAV 解码属于 1.2.0 实机，详见 [历史验证记录](VALIDATION-1.2.0.md)，不能移作新版成品的实机成功。新版 UI 样本仅保存获准状态、时间和摘要，不含截图、联系人或聊天媒体。

## 新版 Markdown 与视频实机（r2）

本版最终发布 CLI 实际分别发送一个 md7 Markdown 作业及一个新的蓝色三秒 MP4 视频。北京时间 Markdown 命令为 **09:37:54.720 至 09:37:55.637**，视频命令为 **09:37:55.677 至 09:37:58.549**；[命令证据](evidence/desktop-media-v121-r2-commands.json)记录两次退出 0，EXE/CLI/协议 DLL 均与本版最终产物一致。[持久回执](evidence/desktop-media-v121-r2-receipts.json)记录根 Markdown 作业 1/1 完成，子业务键恰好一条 Sent，视频业务键也恰好一条 Sent，Inbox=0。两次实机命令不增加或改写上述 94/15/62 的自动化总数。

回执 JSON 只是两次发送后的 metadata-only 状态快照，其 exitCode 与三个程序摘要均为 null；不能单凭快照证明实际启动或送达。真实发送的退出码和程序来源取自命令证据，显示及播放另取 UI 观察；快照中的累计 Sent=16 也不是本次新增 16 次发送。

微信 **4.1.15.13** 的 [直接 UI 观察](evidence/desktop-media-v121-r2-ui.json)于 **09:38:09.966**完整显示 md7 的标题、加粗、行内代码、列表样式以及新蓝色视频。该 Markdown 结论仅覆盖本测试卡，不推定全部语法或手机表现。

视频于 **09:38:20.732**下载、**09:38:47.266**打开，在 **09:39:20.495**使用 Space 重新播放时显示 00:00 与暂停控件，**09:39:30.978**显示 00:03 与播放控件，证明这个样本实际播放到结束。视频为合成内容，**2902 字节**，SHA-256 为 **BC2A2080C24C88CB0ED666922875D5BE54D3C55354BB5C70B1F2C8D5BA147ADC**；没有使用或公开私有媒体。此结论不扩展为全部视频编码或长视频稳定。

观察过程中一次 UI 操作因检测到用户输入而拒绝，重新观察后播放操作受理；收尾收到用户实际 Esc 停止信号后不再操作 UI，未修改或发送聊天草稿。公开 JSON 仅保存获准元数据、结果和观察时间，不含截图、联系人或私有媒体。API 接受、Sent、界面显示与实际播放分别作为证据，不互相替代。

新版真实入站语音和视频没有重新实测，不能将本次出站视频样本或旧版自动 WAV 成功登记为新版入站通过；原生 VOICE 出站仍没有成功证据，音频 FILE 不作替代。

## 新版文件发送实机（r2）

文件发送使用已有生产接口，MediaKind.File 对应上传媒体类型 3、消息项类型 4；CLI 为 --kind file，普通文件项包含官方 file_name 与 len 字段。这次没有更改已测生产字节，也没有重跑或增加 94+StateVault、15/15、62/62 的自动化计数。

本版最终发布 CLI 于 **09:45:59.934 至 09:46:01.019 北京时间**实际一次发送合成 **file7.txt**，内容为“文件验证 f7”及末尾换行，**16 字节**、源文件 SHA-256 为 **6209916E97C20BD3622C1FB2BE051EF8586E8056238FE5867FEA885AC0BED3AB**。[文件命令证据](evidence/file-v121-r2-commands.json)记录实际进程退出 0，三项程序摘要与本版最终产物一致；[文件持久回执](evidence/file-v121-r2-receipts.json)对应恰好一条 Sent、Inbox=0，仅作为发送后状态快照，exitCode 与三个程序摘要为 null。真实进程来源取自命令证据，不能凭状态快照推断客户端打开成功；累计 Sent=17 也不是本次新增 17 次发送。

[独立核对记录](evidence/file-v121-r2-review.json)验证精确业务键、源文件和声明摘要、回执时间位于命令窗口及 client_id/16 字节与 stdout 一致。独立检查加载当前发布协议 DLL，在本地构造消息项 type=4、file_name=file7.txt、字符串 len="16"，不是原始线上包抓取，也不单独证明客户端送达；独立检查未访问账号网络或 UI。

命令时的客户端下载/显示未观察字段保留原样，代表当时证据状态。随后 [客户端确认](evidence/file-v121-r2-client-confirmation.json)记录用户明确“已收到，能打开且内容正确”，本文件样本的手机接收、打开及内容核对通过；不将后来确认改写进早先命令快照，也不宣称已核对手机端逐字节哈希、全部文件类型、大文件或长期稳定。

默认 **100 MiB** 是本地媒体保护值，CLI --max-media-mib 可设 **1 至 512 MiB**，不是官方统一上传配额。首次 SDK 发送为 UploadBoundFileAsync → ToMessageItem → SendBoundItemAsync；业务须保存上传描述符和稳定 64 位 SHA-256 业务键，恢复前检查 Sent/Unknown。重新上传生成新描述符，SDK 不自动避免重复上传，不能把它直接按同键作为原消息重试。CLI 对同显式业务键、相同文件及声明的 Sent 记录已有上传前返回保护；这与 SDK 两阶段恢复不同。

一次 CLI 调用不证明只有一次 CDN HTTP 尝试，上传内部可使用稳定字段最多尝试 3 次；上传完成后、聊天意图持久化前崩溃可能留下无对应发送意图的上传。Sent 前置去重只保护已完成且一致的持久记录，不保证全过程零重复上传。

## 实机历史与尚未完成的目标

1.2.0 的扫码和普通文字收发、Markdown、视频、真实入站语音/视频与自动 SILK→WAV 均有各自记录；合成 SILK、补齐采样率/位深的 SILK、本人真实语音原样上传回发，以及保留原字段/原 CDN 的回发均未取得原生气泡显示。API 接受不等于手机/电脑版送达；样本结果不证明所有账号/格式均不支持。所有时间、摘要、helper 与生产 DLL 差异和确认细节只引用 [1.2.0 验证记录](VALIDATION-1.2.0.md)，不分发真实媒体本体或私有路径。

本版 typing 修复不解决原生 VOICE 出站失败，没有新的原生送达成功。音频 FILE 附件、编解码或入站自动 WAV 不替代原生发送验收。24 小时以上运行、真实断网恢复、跨设备迁移、全部灰度/路由、长期账号限制或封禁情况仍未验证。24 小时是报告提出的正式长期使用前观察建议，不是原始用户指定的时长，不记为通过。

## 隔离、隐私与交付核对

--offline-fixture 必须配合专用 --state。夹具与状态位于带匹配哨兵的 fixture-UUID 目录，只接受虚构测试凭据；handler 不创建 socket，没有夹具错误或耗尽后回退真实网络的路径，不读取默认绑定或真实媒体。模拟发送中的实际强制终止也不是在真实账号发送时终止的证据。

fixture trace 记录声明的假请求合同，可能含 mock typing_ticket；应用 stdout/stderr、默认序列化与持久状态不得保存或输出票据、token、二维码或原始 API 返回。真实绑定由当前 Windows 用户 DPAPI 加密；下载媒体及解码 WAV 为明文，DPAPI 不自动加密下载目录。公开实机 JSON 只保存获准标量、摘要与结果，不公开账号、上下文、CDN/AES、声音、视频或截图。

公开字段和请求头按腾讯默认值对齐，bot_agent=OpenClaw 的日志监控用途不证明官方身份、鉴权优势或免封；不规避必要服务端鉴权或风控。不能保证零封号、永久稳定或全部目标完成。

交付文件摘要见 SHA256SUMS.txt，可用 Get-FileHash -Algorithm SHA256 核对。应按允许清单排除 .git、bin/obj、二维码、DPAPI 状态、聊天及未经许可的参考源码；保留本版和历史文档、来源及完整许可。最终 HTML 渲染、打包与隐私扫描由根代理完成，包条目和哈希不能代替上述运行验收。
