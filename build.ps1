$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw 'The .NET Framework 4.x C# compiler was not found.'
}

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    ('/win32icon:' + (Join-Path $root 'assets\app.ico')),
    ('/out:' + (Join-Path $root 'AiDesktopMulti.exe')),
    '/reference:System.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Management.dll',
    (Join-Path $root 'src\AiDesktopMulti.cs')
)

& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $root 'AiDesktopMulti.exe'
"Built $exe ($((Get-Item $exe).Length) bytes)"
