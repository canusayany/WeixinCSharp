# 第三方来源与许可

协议实现参考 Tencent/openclaw-weixin 2.4.9。腾讯固定源代码快照保留官方 MIT 许可；随附原始许可：docs/Tencent-LICENSE.txt，源码包另有 research/upstream-snapshot/LICENSE。

官方源：https://github.com/Tencent/openclaw-weixin

1.2.0 的 MarkdownFormatting.cs 移植官方 src/messaging/markdown-filter.ts 的流式状态机；媒体请求、CDN AES-128-ECB/PKCS#7、随机 client_id 依据同一固定提交 24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c 的公开实现。扩充的官方 MIT 快照随源码包提供，原版权声明及许可保留。C# 实现不是腾讯出品或对封号风险的保证。

1.2.1 的打字生命周期研究另固定 OpenClaw 2026.8.1 对应提交 ea806575e6450e4d1efdfc72c19f04be982a1b9b。源码包 research/typing-lifecycle-snapshot 保存所引用的腾讯调用器与 OpenClaw 生命周期文件、各自完整 MIT 许可、OpenClaw 原第三方通知及逐文件来源/哈希。它们是研究快照；本程序没有执行或安装 OpenClaw，也未捆绑这些通知中列出的整套应用依赖。C# 生命周期独立实现，停止时取消并等待在途请求的保护与该官方 SDK 有明确差异。

CLI 二维码生成依赖 QRCoder 1.7.0（MIT）。随附许可：docs/QRCoder-LICENSE.txt。

项目：https://github.com/Shane32/QRCoder

QRCoder 1.7.0 的传递依赖包括 System.Drawing.Common 6.0.0 和 Microsoft.Win32.SystemEvents 6.0.0（MIT）；PNG 渲染使用 QRCoder 字节输出，不调用 System.Drawing 渲染。实际 NuGet 包的原始许可与第三方声明分别随附于 docs/System.Drawing.Common-LICENSE.txt、docs/System.Drawing.Common-THIRD-PARTY-NOTICES.txt、docs/Microsoft.Win32.SystemEvents-LICENSE.txt、docs/Microsoft.Win32.SystemEvents-THIRD-PARTY-NOTICES.txt。

Windows 成品包含 .NET 10.0.3 运行时；该实际 runtime NuGet 包的原始许可及第三方声明随附于 docs/DOTNET-LICENSE.txt、docs/DOTNET-THIRD-PARTY-NOTICES.txt。

1.2.0 本地语音转换随附 Node.js 24.19.0 Windows x64 运行时及 silk-wasm 3.7.1。Node 原始许可及内置第三方声明位于 runtime/voice/Node-LICENSE.txt；silk-wasm 原始 MIT 许可位于 runtime/voice/silk-wasm/LICENSE。底层 Skype SILK SDK、编解码封装及内嵌 WAV 解码组件的许可分别保留于 runtime/voice/Skype-SILK-SDK-LICENSE.txt、Skype-SILK-LICENSE.txt、wav-file-decoder-LICENSE.md。研究下载记录与完整性校验见 research/runtime-dependencies。

腾讯固定源码使用 silk-wasm 做入站解码；本项目调用同一依赖的公开 encode API 制作 SILK 是独立扩展，不代表腾讯官方已提供原生语音外发路由。转换依赖均随包提供，运行时无需 npm 安装或下载依赖。

旧 WorkBuddy 逆向项目及其他无明确 LICENSE 的 demo 仅作公开机制分析，不复制或分发其代码；见研究来源说明。
