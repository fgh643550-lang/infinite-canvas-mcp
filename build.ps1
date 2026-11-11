$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$bin = Join-Path $projectDir '发布'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectDir 'app.config') -Destination (Join-Path $bin '无限画布.exe.config') -Force
if (-not (Test-Path -LiteralPath (Join-Path $projectDir 'app.ico'))) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new(256,256)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(244,243,241))
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(29,95,191),20)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddBezier(45,128,45,65,90,65,128,128)
    $path.AddBezier(128,128,166,191,211,191,211,128)
    $path.AddBezier(211,128,211,65,166,65,128,128)
    $path.AddBezier(128,128,90,191,45,191,45,128)
    $graphics.DrawPath($pen,$path)
    $pngStream = [System.IO.MemoryStream]::new()
    $bitmap.Save($pngStream,[System.Drawing.Imaging.ImageFormat]::Png)
    $png = $pngStream.ToArray()
    $iconStream = [System.IO.File]::Create((Join-Path $projectDir 'app.ico'))
    $writer = [System.IO.BinaryWriter]::new($iconStream)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22); $writer.Write($png)
    $writer.Dispose(); $pngStream.Dispose(); $path.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$refs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$uiRefs = @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { $candidate=Join-Path $wpf $_; if(-not(Test-Path -LiteralPath $candidate)){$candidate=Join-Path $framework $_}; '/r:'+$candidate }
if ($args -contains '-AiTest') {
    $sources = Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | ForEach-Object FullName
    & $compiler /nologo /utf8output /target:exe /platform:x64 /main:AiUiTests ('/out:'+(Join-Path $projectDir 'tests\AiUiTests.exe')) $refs $uiRefs $sources (Join-Path $projectDir 'tests\AiUiTests.cs')
    if($LASTEXITCODE -ne 0){throw 'AI UI test compilation failed'}
    & (Join-Path $projectDir 'tests\AiUiTests.exe')
    if($LASTEXITCODE -ne 0){throw 'AI UI tests failed'}
} elseif ($args -contains '-Test') {
    & $compiler /nologo /utf8output /target:exe ('/out:'+(Join-Path $projectDir 'tests\CoreTests.exe')) $refs (Join-Path $projectDir 'src\Core.cs') (Join-Path $projectDir 'tests\CoreTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
    & (Join-Path $projectDir 'tests\CoreTests.exe')
    if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
} else {
    $existing = Join-Path $bin '无限画布.exe'
    $original = Join-Path $bin '无限画布_改造前_20261111.exe.bak'
    if ((Test-Path -LiteralPath $existing) -and -not (Test-Path -LiteralPath $original)) { Copy-Item -LiteralPath $existing -Destination $original }
    $sources = Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | ForEach-Object FullName
    & $compiler /nologo /utf8output /target:winexe /platform:x64 /optimize+ ('/win32icon:'+(Join-Path $projectDir 'app.ico')) ('/win32manifest:'+(Join-Path $projectDir 'app.manifest')) ('/out:'+(Join-Path $bin '无限画布.exe')) $refs $uiRefs $sources
    if($LASTEXITCODE -ne 0){throw 'Application compilation failed'}
    $pluginBin = Join-Path $bin 'plugin'
    New-Item -ItemType Directory -Path $pluginBin -Force | Out-Null
    & $compiler /nologo /utf8output /target:exe /platform:x64 /optimize+ ('/out:'+(Join-Path $pluginBin 'InfiniteCanvas.Mcp.exe')) $refs (Join-Path $projectDir 'src\Core.cs') (Join-Path $projectDir 'src\AiTools.cs') (Join-Path $projectDir 'plugin\McpProgram.cs')
    if($LASTEXITCODE -ne 0){throw 'MCP plugin compilation failed'}
    Get-ChildItem -LiteralPath (Join-Path $projectDir 'plugin') -File | Where-Object { $_.Extension -ne '.cs' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $pluginBin $_.Name) -Force
    }
    # Windows PowerShell 5.1 requires a BOM to decode Chinese source reliably.
    $configScript = Join-Path $pluginBin '生成接入配置.ps1'
    [System.IO.File]::WriteAllText($configScript, [System.IO.File]::ReadAllText($configScript), (New-Object System.Text.UTF8Encoding($true)))
}
