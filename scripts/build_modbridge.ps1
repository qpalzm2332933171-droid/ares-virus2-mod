param(
    [string]$GameManagedDir = "",
    [string]$OutDll = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrEmpty($GameManagedDir)) {
    throw "Please specify -GameManagedDir <path to dumped AOT DLLs (mscorlib.dll, System.dll, UnityEngine.CoreModule.dll, AotDll.dll)>"
}
if ([string]::IsNullOrEmpty($OutDll)) {
    $OutDll = Join-Path $PSScriptRoot "..\AresModMenu\app\src\main\assets\ModBridge.dll"
}
$csc = Get-ChildItem "C:\Windows\Microsoft.NET\Framework64" -Recurse -Filter "csc.exe" |
       Select-Object -Last 1 -ExpandProperty FullName
if (-not $csc) { throw "csc.exe not found under Framework64" }

$stubDir = Join-Path $PSScriptRoot "..\AresModMenu\bridge\stubs"
$work = Join-Path $env:TEMP "ares_modbridge_refs"
New-Item -ItemType Directory -Force -Path $work | Out-Null

# Build minimal compile-time stubs (never referenced at runtime except Collider2D in Physics2DModule)
& $csc /nologo /nostdlib /noconfig /target:library `
    /out:"$work\UnityEngine.Physics2DModule.dll" `
    /r:"$GameManagedDir\mscorlib.dll" `
    /r:"$GameManagedDir\UnityEngine.CoreModule.dll" `
    "$stubDir\UnityEngine.Physics2DModule.cs"

& $csc /nologo /nostdlib /noconfig /target:library `
    /out:"$work\UnityEngine.UI.dll" `
    /r:"$GameManagedDir\mscorlib.dll" `
    "$stubDir\UnityEngine.UI.cs"

$pubAsm = Join-Path $work "Assembly-CSharp.pub.dll"
if (-not (Test-Path $pubAsm)) {
    throw "Missing publicized Assembly-CSharp: generate via patch_assembly_v2213.py --out-pub $pubAsm"
}

& $csc /nologo /unsafe /nostdlib /noconfig /target:library `
    /out:"$OutDll" `
    /r:"$GameManagedDir\mscorlib.dll" `
    /r:"$GameManagedDir\System.dll" `
    /r:"$GameManagedDir\UnityEngine.CoreModule.dll" `
    /r:"$GameManagedDir\UnityEngine.AndroidJNIModule.dll" `
    /r:"$GameManagedDir\AotDll.dll" `
    /r:"$GameManagedDir\Mirror.dll" `
    /r:"$pubAsm" `
    /r:"$work\UnityEngine.Physics2DModule.dll" `
    /r:"$work\UnityEngine.UI.dll" `
    "$PSScriptRoot\..\AresModMenu\bridge\ModBridge.cs"

Write-Host "Built ModBridge.dll -> $OutDll"
