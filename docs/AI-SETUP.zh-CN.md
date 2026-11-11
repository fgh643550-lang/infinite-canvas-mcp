# 无限画布 · AI 插件版

保留原 Windows 无限画布，新增外部 AI 可调用的 10 个工具。支持 MCP 标准输入输出（stdio）和本机 HTTP REST API。插件 EXE 使用 Windows 自带 .NET Framework，无须安装 Python、Node、NuGet 或模型密钥。

## 接入 MCP 客户端

1. 解压完整发布包，保留 `无限画布.exe`、`无限画布.exe.config` 和 `plugin` 文件夹的相对位置。
2. 双击 `plugin/生成接入配置.cmd`，生成适用于当前存放位置的配置。
3. 如果 AI 客户端允许添加本地 stdio MCP 服务器，把服务器命令设为 `plugin/InfiniteCanvas.Mcp.exe` 的**完整路径**，参数留空。也可以将生成的 `mcp-config.json` 中 `mcpServers` 项合并到客户端已有配置；TOML 形式见 `mcp-config.toml`。
4. 重新加载该客户端的 MCP 工具，再让 AI：“读取当前无限画布，在右侧添加一个方案说明卡，保留现有内容。”

MCP 连接可以先握手、列工具，第一次调用画布工具时自动启动 AI 画布。多个 MCP 客户端使用同一数据目录时，共享同一个已开启接入服务的窗口。结束 AI 客户端不会关闭画布。

**已经打开了普通画布窗口时，先点击窗口里的 `AI：关闭`，使其显示 `AI：开启`，再让 AI 调用。** 否则插件会另外启动一个 AI 画布窗口。请避免同时打开多个使用同一数据目录的画布窗口；若需要独立会话，使用下面的数据目录参数。

移动整个发布文件夹后，请重新生成配置。不自动修改或覆盖任何客户端配置，不自动安装到账号或插件市场。

## 启动、关闭与数据

- 双击 `plugin/启动AI画布.cmd`：启动画布并开启接入；也可以执行 `无限画布.exe --ai`。
- 窗口按钮 `AI：开启` / `AI：关闭` 控制服务。普通启动默认关闭。
- 存档和恢复草稿仍在 `数据/存档`、`数据/自动恢复`，兼容已有 `.canvas` 文件。
- AI 启动时不弹出恢复询问，避免阻塞接入；已有恢复草稿仍保留，可随时点“恢复草稿”。
- 独立数据目录：应用和 MCP 插件都接受 `--data-root "D:\我的画布数据"`；两个程序必须指向同一目录。此参数可以配置进 MCP 的 args 数组。
- 接入文件 `数据/ai-connection.json` 只在服务开启时存在，包含当前端口、进程号和密钥。密钥随每次服务启动更换，勿转发此文件。

## 工具

| 工具 | 作用 |
|---|---|
| `canvas_get` | 当前文档、版本、选择与未保存状态 |
| `canvas_list` | 列出存档目录中的画布 |
| `canvas_new` / `canvas_open` | 新建 / 打开；未保存时拒绝切换 |
| `canvas_save` | 无弹窗保存，保留上一版 `.bak` |
| `canvas_apply` | 批量添加文字/图片、修改位置尺寸样式、删除、连线/移除连线、重命名 |
| `canvas_undo` / `canvas_redo` | 撤销 / 重做 |
| `canvas_view` | 显示全部或修改视角 |
| `canvas_preview` | 返回可见画布的 PNG（MCP 返回图片内容） |

调用 `canvas_get` 后，将返回的 `revision` 作为 `expected_revision` 传入修改请求，可防止覆盖中途发生的其他修改。版本冲突时重新读取。用户拖动、编辑文字或打开模态对话框期间，AI 调用会拒绝，请稍后重试。

`canvas_apply` 一次接受 1–200 个操作，整批校验成功后提交，并占一个撤销步骤；无效请求不会部分执行。坐标是画布世界坐标，尺寸单位为像素。`update` 仅修改提供的字段。删除卡片会一并删除连接它的箭头。`rename` 的新名称放在 `text` 字段。

图片通过 `data_base64` 传入原始图片字节的 Base64（不要加 data URL 前缀），每次请求内该字符串不超过 16 MiB，约 12 MiB 图片。AI 不能通过工具读取任意本地文件或访问图片网址；本地图片应由 AI 客户端读取后编码传入。画布存档原有 64 MiB 单张限制不变。预览仅包含当前可见区域，完整画布先调用 `canvas_view` 的 `fit:true`。

## HTTP REST 接入

服务仅监听 `127.0.0.1`，在 17653–17752 中选取可用端口。以实际 `ai-connection.json` 为准。所有端点必须带 `Authorization: Bearer <token>`。拒绝网页 Origin 和非本机 Host，不开放 CORS。

```powershell
$c = Get-Content '.\数据\ai-connection.json' -Raw | ConvertFrom-Json
$headers = @{ Authorization = 'Bearer ' + $c.token }
Invoke-RestMethod ($c.url + '/v1/tools') -Headers $headers

$request = @{
  name = 'canvas_apply'
  arguments = @{
    operations = @(
      @{ op='add_text'; id='idea'; text='新的方案'; x=100; y=100 },
      @{ op='add_text'; id='result'; text='预期效果'; x=600; y=100 },
      @{ op='connect'; start_id='idea'; end_id='result' }
    )
  }
} | ConvertTo-Json -Depth 8
Invoke-RestMethod ($c.url + '/v1/call') -Method Post -Headers $headers `
  -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($request))
```

端点：`GET /health`、`GET /v1/tools`、`POST /v1/call`。成功返回 `{"result":...}`，错误返回 `{"error":"..."}`。HTTP/1.1 JSON 请求必须有 Content-Length，上限 20 MiB，不接受分块传输。工具参数结构与 MCP 一致，可从 `/v1/tools` 获取。

`canvas_save.filename`、`canvas_open.filename` 只能是存档目录下的单个 `.canvas` 文件名。保存不能覆盖另一个已经存在的文件；先打开目标存档后可以保存该文件，或选择新文件名。用户从外部目录打开的文件，需要传入新 filename 在存档目录保存副本。

## 兼容范围与限制

“通用”表示公开工具协议，不表示任何 AI 聊天网页都能安装：客户端必须允许本地 stdio MCP，或允许自定义工具通过本机 HTTP 调用。纯云端 AI 无法直接访问你的 `127.0.0.1`。本版没有远程 HTTPS 托管、OAuth、HTTP MCP、自动聊天或内置模型接口。

MCP 实现依据官方 [生命周期](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle) 和 [Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools) 规范；协议协商支持 2024-11-05、2025-03-26、2025-06-18、2025-11-25。未对每种第三方客户端逐一认证。

源码构建：`./build.ps1`。核心测试：`./build.ps1 -Test`。原生窗口回归：`./build.ps1 -AiTest`。端到端测试（开发者需 Node）：`node --test tests/ai-integration.test.cjs`。
