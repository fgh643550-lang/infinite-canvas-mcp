$ErrorActionPreference = 'Stop'
$bridge = Join-Path $PSScriptRoot 'InfiniteCanvas.Mcp.exe'
if (-not (Test-Path -LiteralPath $bridge)) { throw '请把整个发布文件夹保留在一起，再运行此脚本。' }
$config = @{ mcpServers = @{ 'infinite-canvas' = @{ command = $bridge; args = @() } } }
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'mcp-config.json'), ($config | ConvertTo-Json -Depth 8), $utf8)
$toml = '[mcp_servers.infinite_canvas]' + "`r`n" + 'command = ' + ($bridge | ConvertTo-Json -Compress) + "`r`nargs = []`r`n"
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'mcp-config.toml'), $toml, $utf8)
Write-Output '已生成 mcp-config.json 和 mcp-config.toml。移动文件夹后请重新生成；将所需配置合并到 AI 客户端已有配置中。'
