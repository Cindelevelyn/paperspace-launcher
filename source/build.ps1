param([switch]$Test, [switch]$ProcessTest, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'O compilador do .NET Framework não foi encontrado.' }
$destination = if ($OutputDirectory) { $OutputDirectory } else { Split-Path $PSScriptRoot -Parent }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+',('/out:' + (Join-Path $destination 'PaperSpaces.exe')),('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
('/resource:' + (Join-Path $PSScriptRoot 'Theme.xaml') + ',Theme.xaml'),('/resource:' + (Join-Path $PSScriptRoot 'Main.xaml') + ',Main.xaml'))
if (Test-Path (Join-Path $PSScriptRoot 'PaperSpaces.ico')) {
 $compilerArgs += '/win32icon:' + (Join-Path $PSScriptRoot 'PaperSpaces.ico')
 $compilerArgs += '/resource:' + (Join-Path $PSScriptRoot 'PaperSpaces.ico') + ',PaperSpaces.ico'
}
foreach ($reference in @('System.dll','System.Core.dll','System.Xaml.dll','System.Runtime.Serialization.dll','System.Xml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationTypes.dll')) {
 $compilerArgs += '/reference:' + (Join-Path $framework $reference)
}
foreach ($file in @('Models.cs','App.cs','Tests.cs','AssemblyInfo.cs')) { $compilerArgs += Join-Path $PSScriptRoot $file }
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Falha de compilação.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PaperSpaces.exe.config') -Destination $destination -Force
Write-Output ('Executável: ' + (Join-Path $destination 'PaperSpaces.exe'))
if ($Test -or $ProcessTest) {
 if ($ProcessTest) {
 & $compiler /nologo /target:winexe ('/out:' + (Join-Path $destination 'LaunchProbe.exe')) /reference:System.Core.dll (Join-Path $PSScriptRoot 'LaunchProbe.cs')
 if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o teste de abertura.' }
 }
 $testDirectory = Join-Path $destination 'test-results'
 $testMode = if ($ProcessTest) { '--self-test-process' } else { '--self-test' }
 $process = Start-Process -FilePath (Join-Path $destination 'PaperSpaces.exe') -ArgumentList @($testMode, ('"' + $testDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
 Get-Content -LiteralPath (Join-Path $testDirectory 'results.txt')
 if ($process.ExitCode -ne 0) { throw 'Os testes falharam.' }
}
