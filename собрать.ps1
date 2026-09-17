# Собрать игру под Windows.
#
# Godot умеет экспортировать сам, из редактора, — но одну вещь за него
# приходится делать здесь: **положить рядом с exe папку `data`.**
#
# Данные лежат вне проекта Godot (`prototype/data`), потому что их читают
# и Python, и C#: так «байт в байт» выполняется само собой, а не проверкой.
# Экспорт кладёт в .pck только то, что лежит под `res://`, значит `data`
# туда не попадёт вовсе, и собранная игра не найдёт баланс. Класть данные
# внутрь `res://` тоже нельзя: `System.IO` не читает из .pck, а копия
# в проекте — это вторая правда о балансе.
#
# Отсюда правило: **одно место правды в репозитории, копия рядом с игрой.**
# `Дом.Годот/Пути.cs` ищет `res://data` первой веткой — в собранной игре
# это папка рядом с exe, и она находится.
#
# Запуск:   .\собрать.ps1
# Проверить: .\собрать.ps1 -Запустить

param(
  [switch]$Запустить,
  [string]$Пресет = "Windows Desktop",
  [switch]$Отладочная
)

$ErrorActionPreference = "Stop"

$корень  = Split-Path -Parent $MyInvocation.MyCommand.Path
$проект  = Join-Path $корень "Дом.Годот"
$сборка  = Join-Path (Split-Path -Parent $корень) "сборка"
$exe     = Join-Path $сборка "Опять здесь.exe"

$годот = "C:\Users\user\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.4.1-stable_mono_win64\Godot_v4.4.1-stable_mono_win64_console.exe"
if (-not (Test-Path $годот)) {
  throw "не найден Godot: $годот"
}

# 1. Код должен собираться — иначе экспорт соберёт вчерашнее и промолчит
Write-Host "— сборка C#"
& dotnet build (Join-Path $корень "Дом.sln") -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "C# не собрался" }

# 2. Экспорт
New-Item -ItemType Directory -Force -Path $сборка | Out-Null
$ключ = if ($Отладочная) { "--export-debug" } else { "--export-release" }
Write-Host "— экспорт ($Пресет)"
& $годот --headless --path $проект $ключ $Пресет $exe
if (-not (Test-Path $exe)) { throw "экспорт не дал exe: $exe" }

# 3. Данные рядом с игрой — то, чего Godot за нас не сделает
Write-Host "— data рядом с игрой"
$откуда = Join-Path $корень "data"
$куда   = Join-Path $сборка "data"
if (Test-Path $куда) { Remove-Item -Recurse -Force $куда }
Copy-Item -Recurse $откуда $куда

$сколько = (Get-ChildItem $куда -Filter *.json).Count

# 4. Собранная игра заводится — и не только из своей папки: у запущенной
#    с ярлыка рабочая папка другая. Первая сборка искала data от рабочей
#    папки, а проверялась запуском из prototype/, где data тоже лежит, —
#    и проверка проходила мимо ошибки. Поэтому запускаем из временной папки.
Write-Host "— пробный запуск не из своей папки"
$временная = [System.IO.Path]::GetTempPath()
$журнал = Join-Path $временная "опять_здесь_запуск.log"
Remove-Item -Force $журнал -ErrorAction SilentlyContinue
Push-Location $временная
# Игра пишет предупреждения в stderr, а PowerShell 5.1 при перенаправленном
# выводе делает из них ошибки и с «Stop» обрывает скрипт. Читаем журнал.
$было = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
  & $exe --headless --quit-after 120 --log-file $журнал 2>$null | Out-Null
} finally {
  $ErrorActionPreference = $было
  Pop-Location
}
$лог = if (Test-Path $журнал) { Get-Content -Raw -Encoding UTF8 $журнал } else { "" }
if ([string]::IsNullOrEmpty($лог) -or $лог -match "Exception|не найден каталог данных") {
  throw "собранная игра не завелась:`n$лог"
}

Write-Host "готово: $exe  (данных: $сколько json)"

if ($Запустить) { & $exe }
