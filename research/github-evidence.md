# 微信 iLink 与 WorkBuddy 微信通道：源码证据

核查日期：2026-10-04（Asia/Shanghai）。研究范围：公开源码和公开 npm 元数据；未执行第三方项目、未扫码、未读取用户凭据、未向真实微信账号收发消息。下述“已确认”指源码事实，不等于服务器端完整契约或实际账号联调通过。

## 1. 结论

当前优先实现 **腾讯公开的 iLink Bot HTTPS/JSON 协议**，以官方 npm `@tencent-weixin/openclaw-weixin@2.4.9` 和 `Tencent/openclaw-weixin` 固定提交交叉核对。它已包含个人微信 Bot 的扫码、收消息、发消息的代码，无需为了这条协议下载或反编译 WorkBuddy 安装包。

真正与微信收发有关的 WorkBuddy 逆向参考 `HenryXiaoYang/wechat-openclaw-channel` 采用另一条通道：**CodeBuddy OAuth + 微信客服绑定 + Centrifugo WebSocket + COPILOT_RESPONSE HTTP 回包**。不能把这条旧通道当成 iLink 的另一种鉴权形式，也不能套用其 OAuth token 到 iLink。大量名称含 WorkBuddy 的 `workbuddy-gateway` 仓库实现的是大模型 API 反代，未作为微信收发协议证据。

## 2. 固定版本及来源

| 来源 | 已记录快照 | 发布/提交时间 | 许可证与用途 |
|---|---|---|---|
| [Tencent/openclaw-weixin](https://github.com/Tencent/openclaw-weixin) | `24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c`，package 2.4.9 | 2026-09-21 22:23:14 +08:00 | 有实际 LICENSE 文件，Tencent Copyright 2026，MIT；主要依据 |
| [官方 npm 元数据](https://registry.npmjs.org/@tencent-weixin%2Fopenclaw-weixin) | `dist-tags.latest=2.4.9` | registry `time[2.4.9]=2026-09-17T04:20:17.142Z`，北京时间 2026-09-17 12:20:17 | package author Tencent；发布者 `zengyi1001`，maintainers 中包含多个 `@tencent.com` 邮箱；repository/gitHead 字段未提供，不能单靠这两个字段验证来源 |
| [WorkBuddy 微信逆向参考](https://github.com/HenryXiaoYang/wechat-openclaw-channel) | `8b8b13434fb30a589a8ebd7c68e233c1b49e3f41`，package 1.1.1 | 2026-03-21 23:17:36 +08:00 | package.json 自声明 MIT，但快照未找到 LICENSE；只作机制对照，不复制代码 |
| [独立 iLink demo](https://github.com/x1ah/wechat-ilink-demo) | `248efbee46be7efe3c3890279d22ea2c0a59907a`，package 1.0.0 | 2026-08-22 18:42:15 +08:00 | 未找到 LICENSE，package.json 未声明许可证；只作旧教程差异对照，不复制代码 |

下载的 npm tarball：[openclaw-weixin-2.4.9.tgz](https://registry.npmjs.org/@tencent-weixin/openclaw-weixin/-/openclaw-weixin-2.4.9.tgz)。本地 `references/tencent-openclaw-weixin.tgz`。

- SHA-256：`467e8047f7114e45944961fcd3eda9421843c9c65db61ea24176e252ab800ee4`
- SHA-1：`d74a3ad07cc43f27eb3ec48f3eb1c0945cae2384`，与 registry `dist.shasum` 匹配。
- SRI：`sha512-SfaYehR1Cwq2VV5HxJBp9sVilMms420VfZlMbF4YjRbWomr5+GxfXp9HkeU6y5TbnOc4Ysq0qPw1yBvJwbenBA==`，已实际计算并与 `dist.integrity` 匹配。
- npm tarball 与固定 GitHub 提交的 `src/api/api.ts`、`src/api/types.ts`、`src/auth/login-qr.ts`、`src/messaging/send.ts`、`src/monitor/monitor.ts` 在统一 LF/CRLF 后逐字相同。未安装依赖，未运行包脚本。

## 3. 当前 iLink 请求头与元数据

依据：[官方 api.ts](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/api.ts#L89)，[官方协议说明](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/docs/protocol_zh_CN.md#L32)。本地 npm `src/api/api.ts:89-107, 202-249`。

| Header/JSON 字段 | 2.4.9 源码行为 |
|---|---|
| `iLink-App-Id` | `bot`，来自 package.json.ilink_appid |
| `iLink-App-ClientVersion` | `132105`，公式 `major << 16 | minor << 8 | patch`，每段限低 8 位；2.4.9 即 `0x00020409` |
| `Content-Type` | JSON POST 使用 `application/json` |
| `AuthorizationType` | JSON POST 使用 `ilink_bot_token` |
| `Authorization` | 有非空 token 的业务 POST 使用 `Bearer <bot_token>` |
| `X-WECHAT-UIN` | 每次 JSON POST：随机 uint32 → 十进制 UTF-8 字符串 → Base64；不是随机 4 个字节直接 Base64，也不是实际微信 UIN。源码不证明它在服务端的安全作用，不能把“防重放”当作已验证事实 |
| `SKRouteTag` | 仅配置存在时发送，可选 |
| `base_info.channel_version` | 官方包发 `2.4.9`；表示客户端版本，不是协商得到的服务端协议号 |
| `base_info.bot_agent` | 官方默认 `OpenClaw`，可自声明应用身份，例如 `WeChatIlinkDotNet/1.0.0`；ASCII UA 风格 token，清洗后最多 256 字节，源码注释说明只用于观测 |

**两类扫码请求不一样：**扫码状态 GET 只含公共应用头 `iLink-App-Id`、`iLink-App-ClientVersion`、可选 `SKRouteTag`；不带 `AuthorizationType`、Bearer、`X-WECHAT-UIN`。获取二维码 POST 含 JSON POST 头、`AuthorizationType`、随机 `X-WECHAT-UIN`，但不带 Bearer，也不带 `base_info`。

## 4. 当前扫码登录线协议

依据：[login-qr.ts](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/auth/login-qr.ts#L95)，本地 npm `src/auth/login-qr.ts:31-65, 95-105, 128-157, 334-474`。

固定起始地址：`https://ilinkai.weixin.qq.com`。

```http
POST /ilink/bot/get_bot_qrcode?bot_type=3
Content-Type: application/json
AuthorizationType: ilink_bot_token
iLink-App-Id: bot
iLink-App-ClientVersion: 132105
X-WECHAT-UIN: <Base64(UTF8(uint32 decimal))>

{"local_token_list":[]}
```

响应字段：`qrcode`、`qrcode_img_content`。后者供展示二维码 URL/内容；前者用于状态请求，不能把两者混用。官方会从自己的已登录账号索引取最近最多 10 个 token 到 `local_token_list`；首次登录发送 `[]` 即可。

```http
GET /ilink/bot/get_qrcode_status?qrcode=<URL encoded qrcode>
iLink-App-Id: bot
iLink-App-ClientVersion: 132105
```

需要验证码时追加 `&verify_code=<URL encoded human entered code>`，不能猜码或自动重试错码。源码 QR 单次长轮询客户端超时 35000 ms，超时返回等待；登录流程的轮询间隔约 1 秒，验证码输入后立即下一轮。

| status 的精确拼写 | 客户端行为 |
|---|---|
| `wait` | 继续轮询 |
| `scaned` | 已扫码，继续等待验证；携带验证码后收到该状态则清除待提交验证码 |
| `need_verifycode` | 提示用户输入手机微信显示的数字，下轮 GET 携带 `verify_code` |
| `verify_code_blocked` | 多次错误提示，源码有有限刷新次数；可靠客户端可停止并要求重新登录，不能无限重试 |
| `expired` | 有界刷新二维码，次数用尽停止 |
| `scaned_but_redirect` | 有 `redirect_host` 时改为 `https://<redirect_host>` 继续状态轮询 |
| `binded_redirect` | 已绑定到当前实例，源码返回 `alreadyConnected=true`；并不是返回新 token 的确认成功 |
| `confirmed` | 保存 `bot_token`、`ilink_bot_id`、`baseurl`、`ilink_user_id` |

`confirmed` 结构：

```json
{
  "status":"confirmed",
  "bot_token":"<secret>",
  "ilink_bot_id":"<bot id>",
  "baseurl":"https://<API host>",
  "ilink_user_id":"<scanning user id>"
}
```

**地址可信范围：**官方文档只列 `ilinkai.weixin.qq.com` 为 API 默认地址、`novac2c.cdn.weixin.qq.com` 为 CDN 默认地址；`baseurl` 示例只是占位符，未提供实际 IDC `redirect_host` 清单。源码直接使用服务器返回的 host，这不是域名安全保证。工程建议：默认只信任精确 `ilinkai.weixin.qq.com`；任何新 host 需明确配置允许，强制 HTTPS、拒绝 userinfo/非默认端口，不跟随携带 Bearer 的跨 host HTTP redirect。默认允许 `*.weixin.qq.com` 是可选工程策略，无法从当前资料证明所有子域都应受信任。

## 5. 长轮询收消息

依据：[api.ts getUpdates](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/api.ts#L438)、[monitor.ts](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/monitor/monitor.ts#L86)。

```http
POST /ilink/bot/getupdates
Authorization: Bearer <bot_token>
```

```json
{
  "get_updates_buf":"",
  "base_info":{"channel_version":"2.4.9","bot_agent":"WeChatIlinkDotNet/1.0.0"}
}
```

响应字段：`ret`、`errcode`、`errmsg`、`msgs` 数组、`get_updates_buf`、`longpolling_timeout_ms`。字段可能缺省。`msgs[]` 为下面的统一消息信封。

- 首次 cursor 为 `""`；下一次原样回传完整 `get_updates_buf`，不要拆解或自行生成。官方只在成功响应且新 cursor 非空时推进并保存。
- `sync_buf` 在类型中仅为废弃兼容字段，当前构造和 monitor 不读取它作为 fallback。
- 官方默认单次客户端长轮询超时 35000 ms；返回正数 `longpolling_timeout_ms` 后下一轮采用这个值。工程上应限定合理范围，并确保 `HttpClient.Timeout` 不小于长轮询请求的实际预算。
- 客户端超时是正常空轮询，不清空 cursor，不当成登录失效。用户取消要停止，不应该被超时恢复逻辑吞掉。
- `ret != 0` 或 `errcode != 0` 为业务失败。`ret == -14` 或 `errcode == -14` 在源码表示 stale/expired Bot token，官方暂停账号所有入站/出站请求一小时。它不是账号封禁的证据。
- 其他失败，官方前两次延迟 2 秒；累计 3 次延迟 30 秒并重置失败计数。可靠客户端可加 jitter、尊重 HTTP 429 Retry-After；这些是工程措施。
- 官方先落盘 cursor 后处理消息，可能在处理前崩溃造成业务消息丢失。更可靠的 C#实现建议在一份原子持久化状态中同时写入新 cursor 和已接收 inbox，再处理 inbox，成功后 ack；重启恢复 inbox。该措施不改变线协议。
- 每个 Bot 只运行一个 poller，避免同 token 多进程 cursor 竞争。按 `(accountId,message_id)` 去重；消息没有 ID 时只能使用保守 fallback，不能称服务端严格 exactly-once。

## 6. 文本发送

依据：[send.ts](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/send.ts#L56)，[api.ts sendMessage](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/api.ts#L579)。

```http
POST /ilink/bot/sendmessage
Authorization: Bearer <bot_token>
```

```json
{
  "msg":{
    "from_user_id":"",
    "to_user_id":"<inbound from_user_id>",
    "client_id":"<unique id per send>",
    "message_type":2,
    "message_state":2,
    "context_token":"<inbound context_token>",
    "item_list":[{"type":1,"text_item":{"text":"你好"}}]
  },
  "base_info":{"channel_version":"2.4.9","bot_agent":"WeChatIlinkDotNet/1.0.0"}
}
```

`run_id` 在提供时额外携带；不是普通文本必须字段。响应类型包含 `ret`、`errmsg`、`message_id`；`{}` 被当前官方接受，非零 `ret` 抛错；空字符串不是当前官方成功响应示例，因为源码要解析 JSON。HTTP 200 不代表消息成功，必须检查业务返回值。

`context_token` 的边界：官方 `send.ts:109-110` 在缺省时记录 warning 然后继续发送，builder 省略此字段。官方协议文档也明确**这不能证明服务器一定接受缺省请求**。稳健默认是只给已有入站会话回复，按 `(botId,userId)` 保存最近上下文，并原样返回对应消息的 token；不跨账号、跨用户复用。不能承诺任意好友主动推送、群聊控制或完整好友列表。

源码没有说明 `client_id` 的服务端去重/幂等保证。发送超时后结果未知，默认不要盲目重发，以免重复投递；本地记录 unknown 状态并交由调用方明确处理。

## 7. 消息类型与 C# 数据解析

依据：[types.ts](https://github.com/Tencent/openclaw-weixin/blob/24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/api/types.ts#L64)，本地 `src/api/types.ts:64-85, 191-244`。

- `message_type`: 0 NONE，1 USER，2 BOT。
- `message_state`: 0 NEW，1 GENERATING，2 FINISH。
- `item_list[].type`: 1 文本、2 图片、3 语音、4 文件、5 视频、11 工具开始、12 工具结果。支持结构定义不等于测试过所有媒体能力。
- 信封字段：`seq`、`message_id`、`from_user_id`、`to_user_id`、`client_id`、毫秒时间戳、`session_id`、`group_id`、`message_type`、`message_state`、`item_list`、`context_token`、`run_id`。所有字段类型声明可选；程序应容忍未知扩展字段。
- **message_id 是 uint64 线字段**，官方新解析器把 `message_id/msg_id/svr_id` 保存为字符串以避免 JavaScript number 精度丢失。C#可用 `JsonElement` 对 Number `GetRawText()` / String `GetString()`，或 uint64/string 兼容转换器，不能先转 double 或 signed Int64 再做 ID 去重。
- 文本位置是 `item_list[].text_item.text`，可能有多个文本项，不是顶层 `content`。
- `getconfig` 请求是 `{ilink_user_id,context_token?,base_info}`，响应有 `typing_ticket`；`sendtyping` 请求 `{ilink_user_id,typing_ticket,status,base_info}`，status 1 输入中、2 取消。两者默认超时 10000 ms。
- 生命周期 `POST /ilink/bot/msg/notifystart` 和 `.../notifystop`，body `{base_info}`，默认超时10000 ms。官方将失败视为 warning，不阻断启动/停止。

## 8. WorkBuddy 逆向参考具体区别

依据：[codebuddy-api.ts](https://github.com/HenryXiaoYang/wechat-openclaw-channel/blob/8b8b13434fb30a589a8ebd7c68e233c1b49e3f41/auth/codebuddy-api.ts#L1)、[centrifuge-client.ts](https://github.com/HenryXiaoYang/wechat-openclaw-channel/blob/8b8b13434fb30a589a8ebd7c68e233c1b49e3f41/websocket/centrifuge-client.ts#L1)。代码注释声称逆向自 WorkBuddy.app；本研究未下载 WorkBuddy 安装包独立确认其二进制出处。

| 项目 | WorkBuddy 参考通道 | 当前 iLink |
|---|---|---|
| 起始服务 | `https://copilot.tencent.com` | `https://ilinkai.weixin.qq.com` |
| 登录 | `POST /v2/plugin/auth/state?platform=ide` → 浏览器 OAuth → `/v2/plugin/auth/token?state=...`，有 refresh token | 微信 Bot 二维码与手机确认，bot_token |
| 微信绑定 | `/v2/backgroundagent/wechatkfProxy/link`、`.../bindStatus`、`.../bind` | `get_bot_qrcode/get_qrcode_status` |
| workspace 注册 | `/v2/agentos/localagent/registerWorkspace` 返回 url/connectionToken/channel/subscriptionToken | 无此步骤 |
| 接收 | Centrifugo subscription publication，消息 `{chatId,msgId,content,msgType,user,timestamp,...}` | HTTPS getupdates `msgs[]` + cursor |
| 回复 | `/v2/backgroundagent/wecom/local-proxy/receive` `COPILOT_RESPONSE` | `/ilink/bot/sendmessage` `msg` |
| 会话 | host/workspace sessionId + chatId | from_user_id + context_token |

WorkBuddy `COPILOT_RESPONSE` 字段：`{type:"COPILOT_RESPONSE",msgId,chatId,success,message,metadata:{sessionId,requestId,state}}`。这说明逆向项目是微信客服中转通道，不是微信原生私有客户端协议，不支持据此推论能够读取账号全部私人聊天。

## 9. 第三方旧教程中已看到的差异

`x1ah/wechat-ilink-demo` 当前提交虽然 README 最近更新，`bot.mjs:6,16` 仍说明根据官方 1.0.2。它获取二维码用 GET、缺少新版 iLink 应用头、用旧 `bot_id` 而非 `ilink_bot_id` 读取字段、没有新验证码状态、cursor 只在内存、`errcode` 检查依赖非零 ret 分支；不能原样照抄作为稳定实现。

其他第三方文档对发送成功的描述可能是 HTTP 200/空响应，或把 `context_token` 缺省说成从某版本开始允许。当前应以官方 2.4.9 的 JSON 响应检查与明确限制为依据，不能把第三方推断升格为协议保证。

## 10. 可证明的完成范围

公开源码足以实现扫码、授权保存、单 Bot 长轮询、持久化 cursor、文本会话回复和错误处理。源码不能证明未来不变、无限制主动群发、无封号风险，也不能替代用户实际扫码后的端到端验收。封号/违规风险应另查微信条款及官方限制，并明确区分 Bot token 失效、验证失败、HTTP 错误与真正账号封禁。
