# 无限画布 · Infinite Canvas MCP

Windows 离线无限画布，让外部 AI 通过 **MCP（stdio）或本机 HTTP REST API** 操作实时画布。

支持图片、可编辑文字卡片、箭头连线、缩放平移、小地图、独立存档和撤销重做。AI 的操作直接出现在桌面窗口中。

**A Windows desktop infinite canvas with an MCP stdio server and authenticated localhost REST API. No Python, Node.js, npm or model API key is required to run the application.**

## 下载与运行

**[下载 Windows x64 便携版 v1.0.0](downloads/InfiniteCanvas-MCP-v1.0.0-windows-x64.zip)**

1. 下载 ZIP，完整解压到一个可写文件夹。
2. 双击 `无限画布.exe`，即可当作普通离线画布使用。
3. 要让 AI 操作画布，请按下方 MCP 或 HTTP 接入步骤配置。

运行环境：Windows x64、.NET Framework 4.7.2 或更高版本。使用 WPF 原生窗口，需要可交互的 Windows 桌面。无需安装 Python、Node 或 NuGet 包。

> “通用 AI 接入”指客户端可使用公开工具协议；客户端必须允许本地 stdio MCP 或自定义工具调用。纯云端聊天网页不能直接访问电脑的 `127.0.0.1`。

## MCP 接入：推荐方式

1. 双击 `plugin/生成接入配置.cmd`。
2. 将生成的 `mcp-config.json` 中对应服务器配置合并到 AI 客户端的 MCP 设置；使用 TOML 的客户端可参考同目录的 `mcp-config.toml`。
3. 重新加载该客户端的工具。第一次调用画布工具时，插件会自动启动 AI 画布窗口。

也可手动添加本地 MCP 服务器：

- **传输方式**：stdio
- **命令**：解压文件夹中 `plugin/InfiniteCanvas.Mcp.exe` 的完整路径
- **参数**：留空

JSON 示例（请替换成实际路径）：

```json
{
  "mcpServers": {
    "infinite-canvas": {
      "command": "C:\\Tools\\InfiniteCanvas\\plugin\\InfiniteCanvas.Mcp.exe",
      "args": []
    }
  }
}
```

TOML 示例：

```toml
[mcp_servers.infinite_canvas]
command = 'C:\Tools\InfiniteCanvas\plugin\InfiniteCanvas.Mcp.exe'
args = []
```

**如果普通画布窗口已经打开，请先点窗口工具栏里的 `AI：关闭`，切换为 `AI：开启`，再让 AI 调用。** 否则插件会另开一个 AI 画布窗口。移动整个文件夹后，请重新生成配置。

可以对 AI 说：

> 先读取当前无限画布，保留已有内容，在右侧添加“目标、方案、下一步”三张说明卡，并用箭头连接。修改后显示全部内容，先不要保存。

多个 MCP 客户端使用相同数据目录时，会连接到同一个已开启接入的窗口。退出 AI 客户端不会关闭画布。请避免手动开启多个使用同一数据目录的画布窗口。

## AI 能操作什么

| 工具 | 作用 |
|---|---|
| `canvas_get` | 获取当前画布、版本标识、选择和未保存状态 |
| `canvas_list` | 列出存档目录中的画布 |
| `canvas_new` | 新建空白画布；有未保存修改时拒绝切换 |
| `canvas_open` | 打开指定存档；有未保存修改时拒绝切换 |
| `canvas_save` | 无弹窗保存，保留前一版本 `.bak` |
| `canvas_apply` | 批量添加文字/图片、修改布局/样式、删除卡片、连接/移除箭头、重命名 |
| `canvas_undo` | 撤销一步 |
| `canvas_redo` | 重做一步 |
| `canvas_view` | 显示全部，或调整缩放和平移 |
| `canvas_preview` | 返回当前可见画布的 PNG，供 AI 检查结果 |

一次 `canvas_apply` 可包含 1–200 个操作。整批先校验，成功后一次性提交，占一个撤销步骤。失败不会部分修改画布。

建议先调用 `canvas_get`，把返回的 `revision` 放入后续修改请求的 `expected_revision`。发生版本冲突时重新读取，避免覆盖用户或另一个 AI 刚做的修改。用户正在拖动、编辑文字或打开模态对话框时，AI 调用会被拒绝，应稍后重试。

## HTTP REST 接入

先双击 `plugin/启动AI画布.cmd`，或在窗口中开启 AI 接入。

服务仅监听 `127.0.0.1`，从 17653–17752 中选择可用端口。实际地址和密钥见 `数据/ai-connection.json`。每次服务启动会生成新密钥，所有请求都必须使用 Bearer 鉴权。

```powershell
# 在解压目录运行
$c = Get-Content '.\数据\ai-connection.json' -Raw | ConvertFrom-Json
$headers = @{ Authorization = 'Bearer ' + $c.token }

# 查看工具和参数结构
Invoke-RestMethod ($c.url + '/v1/tools') -Headers $headers

# 添加两张卡片和一条箭头
$body = @{
  name = 'canvas_apply'
  arguments = @{
    operations = @(
      @{ op='add_text'; id='idea'; text='方案'; x=100; y=100 },
      @{ op='add_text'; id='result'; text='结果'; x=600; y=100 },
      @{ op='connect'; start_id='idea'; end_id='result' }
    )
  }
} | ConvertTo-Json -Depth 8

Invoke-RestMethod ($c.url + '/v1/call') -Method Post -Headers $headers `
  -ContentType 'application/json; charset=utf-8' `
  -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

| 端点 | 用途 |
|---|---|
| `GET /health` | 服务版本和进程信息 |
| `GET /v1/tools` | 工具目录、参数 JSON Schema 和调用指导 |
| `POST /v1/call` | 以 `{"name":"工具名","arguments":{...}}` 调用工具 |

HTTP 成功返回 `{"result":...}`，失败返回 `{"error":"..."}`。请求使用 HTTP/1.1、UTF-8 JSON 和 Content-Length，最大 20 MiB，不接受分块传输。拒绝网页 Origin 和非本机 Host，不开放 CORS。

图片通过 `add_image` 的 `data_base64` 传入，内容是原始图片字节的 Base64，不加 data URL 前缀。该字符串最多 16 MiB，约 12 MiB 原图。工具不提供任意本地文件读取或图片网址下载功能；图片由调用客户端读取并编码。

完整参数和更多细节见 **[详细接入说明](docs/AI-SETUP.zh-CN.md)**。

## 数据和操作说明

- `数据/存档`：正式 `.canvas` 存档，图片内嵌其中。
- `数据/自动恢复`：独立恢复草稿，不能替代正式保存。
- `数据/ai-connection.json`：服务开启时的连接信息，包含密钥，不要公开分享。
- `Ctrl+S` 保存；`Ctrl+O` 打开；`Ctrl+N` 新建；`Ctrl+Z` / `Ctrl+Y` 撤销/重做。
- `Ctrl+V` 粘贴图片或文字；双击文字编辑；滚轮缩放；拖动空白处平移；`Ctrl+0` 显示全部。
- AI 启动时暂缓恢复询问，已有草稿保留，可手动点“恢复草稿”。

应用和 MCP EXE 均接受独立数据目录：

```text
--data-root "D:\MyCanvasData"
```

需要隔离会话时，把这两个参数放入 MCP 配置的 `args` 数组。连接同一窗口的客户端必须指向同一数据目录。

AI 存档操作仅使用存档目录内的单个 `.canvas` 文件名。保存不会直接覆盖另一个已存在的画布；先打开目标画布再保存，或选择新文件名。已有外部目录画布可通过指定新文件名保存副本。

## 从源码构建

```powershell
./build.ps1
```

使用系统自带 C# 编译器和 .NET Framework/WPF，无需 SDK、NuGet 或联网。输出在 `发布/`。打包：

```powershell
./package-ai.ps1
```

测试：

```powershell
./build.ps1 -Test
./build.ps1 -AiTest
node --test tests/ai-integration.test.cjs
```

最后一条仅供开发验证，需安装 Node.js；运行软件本身无需 Node。

```text
src/                  WPF 画布、存档、AI 控制器、HTTP 和工具定义
plugin/               MCP 桥接源码、启动和配置脚本
tests/                核心、原生窗口与真实 MCP/HTTP 集成测试
docs/                 接入说明与版本记录
downloads/            可直接使用的 Windows x64 便携版 ZIP
app.config            .NET Framework 启动配置
build.ps1             编译入口
package-ai.ps1        便携版打包入口
```

## 验证与已知限制

v1.0.0：17 项核心测试、3 项原生窗口回归，以及 12 项（含分组）真实 MCP/HTTP 集成测试通过。发布 ZIP 解压到不同中文路径后也完成同套接入测试。

- 支持本地 stdio MCP 和本机 HTTP REST；不提供远程 HTTP MCP、OAuth 或云端托管。
- 未对每种第三方 AI 客户端逐一认证。
- 原有置顶原生标志检查在当前测试环境中失败，改造前备份也有相同失败；未将其宣称为通过。
- 编译保留原 `FormattedText` 过时 API 警告，无编译错误。

便携版 SHA-256：

```text
81A05BC7530DE4BE11B20C55182F07BE85D80B4E975EDFD14EA9467BEC3BFB0B
```
