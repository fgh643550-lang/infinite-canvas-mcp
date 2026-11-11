$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$releaseDir = Join-Path $projectDir '发布'
$distRoot = Join-Path $projectDir 'output'
$stage = Join-Path $distRoot '无限画布_AI插件版_1.0.0'
New-Item -ItemType Directory -Path (Join-Path $stage 'plugin') -Force | Out-Null
foreach ($filename in @('无限画布.exe','无限画布.exe.config')) {
    Copy-Item -LiteralPath (Join-Path $releaseDir $filename) -Destination (Join-Path $stage $filename) -Force
}
$pluginFiles = @('InfiniteCanvas.Mcp.exe','AI接入说明.md','生成接入配置.ps1','生成接入配置.cmd','启动AI画布.cmd')
foreach ($filename in $pluginFiles) { Copy-Item -LiteralPath (Join-Path $releaseDir ('plugin/' + $filename)) -Destination (Join-Path $stage ('plugin/' + $filename)) -Force }
$quickStart = @'
无限画布 AI 插件版 1.0.0

本软件支持外部 AI 通过本地 stdio MCP 或 HTTP REST 操作实时画布。

接入：双击 plugin/生成接入配置.cmd，将生成配置合并到 AI 客户端的 MCP 设置。
服务器命令：plugin/InfiniteCanvas.Mcp.exe 的完整路径，参数留空。
第一次调用画布工具时，会自动启动 AI 画布。

如果已打开普通画布窗口，请先点击“AI：关闭”使其变成“AI：开启”，再让 AI 调用。
普通使用：无限画布.exe。开启 AI：plugin/启动AI画布.cmd。

详细说明：plugin/AI接入说明.md。
本包不含个人存档、密钥和模型；使用旧数据请运行原项目的发布版本，或自行复制旧数据文件夹。
客户端必须支持本地 MCP 或本机工具调用；纯云端聊天网页无法直接访问本机。
'@
[System.IO.File]::WriteAllText((Join-Path $stage '快速开始.txt'), $quickStart, (New-Object System.Text.UTF8Encoding($true)))
$zipPath = Join-Path $projectDir '无限画布_AI插件版_1.0.0.zip'
Compress-Archive -LiteralPath $stage -DestinationPath $zipPath -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($archive.Entries.Count -ne 8) { throw 'Unexpected package contents' }
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match 'ai-connection|settings.json|\.canvas|\.bak|mcp-config\.(json|toml)') { throw 'Private data found in package' }
    }
    $archive.Entries | Select-Object FullName,Length
} finally { $archive.Dispose() }
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
