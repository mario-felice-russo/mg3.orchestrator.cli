<#
.SYNOPSIS
    Menu per verificare i processi in errore su UiPath Orchestrator con mg3orch.

.DESCRIPTION
    Mostra i job falliti nel periodo scelto (default: ultime 24 ore) con il messaggio d'errore
    e la VM su cui sono stati eseguiti, i riepiloghi per processo e per VM, e i messaggi di errore
    scritti dai robot.

    Lo script usa il file di configurazione indicato con -Config. Per leggere l'output in modo
    affidabile ne crea una copia temporanea con "format": "json", nella cartella temporanea
    dell'utente, e la elimina all'uscita.

.PARAMETER Config
    File di configurazione di mg3orch. Default: .\mg3orch.json

.PARAMETER Exe
    Percorso di mg3orch. Se omesso, lo cerca accanto allo script, in artifacts\win-x64 e nel PATH.

.PARAMETER Since
    Periodo da esaminare, ad esempio 24h, 12h, 7d. Default: 24h

.EXAMPLE
    .\scripts\mg3orch-menu.ps1 -Config .\mg3orch.json

.EXAMPLE
    .\scripts\mg3orch-menu.ps1 -Config .\mg3orch.json -Since 7d
#>
[CmdletBinding()]
param(
    [string]$Config = '.\mg3orch.json',
    [string]$Exe,
    [string]$Since = '24h'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Preparazione -----------------------------------------------------------------------------

function Find-Mg3Exe {
    param([string]$Explicit)
    if ($Explicit) {
        if (Test-Path -LiteralPath $Explicit -PathType Leaf) { return (Resolve-Path -LiteralPath $Explicit).Path }
        throw "mg3orch non trovato in '$Explicit'."
    }
    $candidates = @()
    $root = Split-Path -Parent $PSScriptRoot
    $candidates += @(
        (Join-Path $PSScriptRoot 'mg3orch.exe'),
        (Join-Path $root 'artifacts\win-x64\mg3orch.exe'),
        (Join-Path $root 'artifacts/linux-x64/mg3orch'),
        (Join-Path $root 'artifacts/osx-arm64/mg3orch')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    $command = Get-Command mg3orch -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw "mg3orch non trovato. Indicalo con -Exe <percorso>, oppure compilalo con: dotnet publish src/Mg3.Orchestrator.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/win-x64"
}

function New-JsonConfigCopy {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Il file di configurazione '$Path' non esiste. Crealo con: mg3orch init --config $Path"
    }
    # Le righe di commento // sono ammesse da mg3orch ma non da ConvertFrom-Json di Windows PowerShell 5.1.
    $text = (Get-Content -LiteralPath $Path -Raw) -replace '(?m)^\s*//.*$', ''
    $config = $text | ConvertFrom-Json
    $config | Add-Member -NotePropertyName format -NotePropertyValue 'json' -Force
    if (-not ($config.PSObject.Properties.Name -contains 'advanced') -or $null -eq $config.advanced) {
        $config | Add-Member -NotePropertyName advanced -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    $config.advanced | Add-Member -NotePropertyName envelope -NotePropertyValue $true -Force
    $config.advanced | Add-Member -NotePropertyName verbosity -NotePropertyValue 'quiet' -Force

    $copy = Join-Path ([IO.Path]::GetTempPath()) ("mg3orch-menu-{0}.json" -f [guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($copy, ($config | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) { & chmod 600 $copy }
    return $copy
}

# --- Chiamata a mg3orch -------------------------------------------------------------------------

function Invoke-Mg3 {
    param([string[]]$Arguments)
    $output = & $script:Mg3 @Arguments --config $script:JsonConfig 2>$null
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).Trim()
    if (-not $text) {
        Write-Host "mg3orch non ha prodotto output (exit code $exitCode)." -ForegroundColor Red
        return $null
    }

    $result = $text | ConvertFrom-Json
    $names = $result.PSObject.Properties.Name
    if ($names -contains 'error' -and -not ($names -contains 'data')) {
        Write-Mg3Error $result.error
        return $null
    }

    if ($result.error) { Write-Mg3Error $result.error }
    foreach ($warning in @($result.warnings)) {
        if ($warning) { Write-Host "Avviso: $warning" -ForegroundColor Yellow }
    }
    if ($result.truncated) {
        Write-Host 'Attenzione: risultati troncati (limiti advanced.maxItems / maxPages). Riduci il periodo.' -ForegroundColor Yellow
    }

    $data = $result.data
    if ($null -eq $data) { return @() }
    if ($data.PSObject.Properties.Name -contains 'value') { return @($data.value) }
    return @($data)
}

function Write-Mg3Error {
    param($ErrorInfo)
    Write-Host ("ERRORE ({0} – {1}): {2}" -f $ErrorInfo.exitCode, $ErrorInfo.meaning, $ErrorInfo.message) -ForegroundColor Red
}

function Format-LocalTime {
    param($Value)
    if ($null -eq $Value -or $Value -eq '') { return '' }
    if ($Value -is [datetime]) {
        $date = $Value
        if ($date.Kind -eq [DateTimeKind]::Unspecified) { $date = [datetime]::SpecifyKind($date, [DateTimeKind]::Utc) }
        return $date.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')
    }
    return [DateTimeOffset]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture).LocalDateTime.ToString('yyyy-MM-dd HH:mm:ss')
}

function Show-Rows {
    param($Rows, [string]$Empty)
    $list = @($Rows)
    if ($list.Count -eq 0) {
        Write-Host $Empty -ForegroundColor Green
        return
    }
    # Out-String con larghezza ampia: la tabella resta leggibile anche quando l'output è rediretto.
    Write-Host (($list | Format-Table -AutoSize -Wrap | Out-String -Width 4096).TrimEnd())
    if ($list.Count -eq 1) { Write-Host '1 riga.' } else { Write-Host ("{0} righe." -f $list.Count) }
}

# --- Comandi del menu ---------------------------------------------------------------------------

function Get-FaultedJobs {
    $query = "where State == 'Faulted' and EndTime >= now() - $script:Since | orderby EndTime desc | select Key, EndTime, ReleaseName, HostMachineName, Info"
    $rows = Invoke-Mg3 @('jobs', 'list', '--query', $query)
    if ($null -eq $rows) { return $null }
    $index = 0
    return @($rows | ForEach-Object {
        $index++
        [pscustomobject]@{
            '#'       = $index
            Fine      = Format-LocalTime $_.EndTime
            Processo  = $_.ReleaseName
            VM        = $_.HostMachineName
            Messaggio = $_.Info
            Key       = $_.Key
        }
    })
}

function Show-FaultedJobs {
    $script:LastFaulted = Get-FaultedJobs
    if ($null -eq $script:LastFaulted) { return }
    Show-Rows ($script:LastFaulted | Select-Object '#', Fine, Processo, VM, Messaggio) "Nessun job in errore nelle ultime $script:Since."
}

function Show-FaultedSummary {
    param([string[]]$Keys, [string[]]$Headers)
    $query = "where State == 'Faulted' and EndTime >= now() - $script:Since | groupby $($Keys -join ', ') | orderby count desc"
    $rows = Invoke-Mg3 @('jobs', 'list', '--query', $query)
    if ($null -eq $rows) { return }
    $table = @($rows | ForEach-Object {
        $row = [ordered]@{}
        for ($i = 0; $i -lt $Keys.Count; $i++) { $row[$Headers[$i]] = $_.($Keys[$i]) }
        $row['Errori'] = $_.count
        [pscustomobject]$row
    })
    Show-Rows $table "Nessun job in errore nelle ultime $script:Since."
}

function Show-RobotErrors {
    param([string]$JobKey)
    $filter = "Level in ('Error', 'Fatal') and TimeStamp >= now() - $script:Since"
    if ($JobKey) { $filter = "JobKey == '$JobKey' and $filter" }
    $query = "where $filter | orderby TimeStamp desc | select TimeStamp, ProcessName, HostMachineName, Level, Message"
    $rows = Invoke-Mg3 @('robotlogs', 'list', '--query', $query)
    if ($null -eq $rows) { return }
    $table = @($rows | ForEach-Object {
        [pscustomobject]@{
            Ora       = Format-LocalTime $_.TimeStamp
            Processo  = $_.ProcessName
            VM        = $_.HostMachineName
            Livello   = $_.Level
            Messaggio = $_.Message
        }
    })
    Show-Rows $table "Nessun messaggio di errore dei robot nelle ultime $script:Since."
}

function Show-JobErrors {
    $answer = Read-Host 'Numero del job (dall''elenco del punto 1) oppure la sua Key'
    if (-not $answer) { return }
    $key = $answer
    $number = 0
    if ([int]::TryParse($answer, [ref]$number)) {
        if ($null -eq $script:LastFaulted -or $number -lt 1 -or $number -gt @($script:LastFaulted).Count) {
            Write-Host 'Numero non valido: esegui prima il punto 1.' -ForegroundColor Yellow
            return
        }
        $key = @($script:LastFaulted)[$number - 1].Key
    }
    $guid = [guid]::Empty
    if (-not [guid]::TryParse($key, [ref]$guid)) {
        Write-Host "'$key' non è una Key valida." -ForegroundColor Yellow
        return
    }
    Show-RobotErrors -JobKey $guid.ToString()
}

function Export-FaultedJobs {
    $rows = Get-FaultedJobs
    if ($null -eq $rows) { return }
    if (@($rows).Count -eq 0) {
        Write-Host "Nessun job in errore nelle ultime $script:Since: nessun file creato." -ForegroundColor Green
        return
    }
    $file = Join-Path (Get-Location) ("mg3orch-job-in-errore-{0}.csv" -f (Get-Date -Format 'yyyyMMdd-HHmm'))
    $rows | Select-Object Fine, Processo, VM, Messaggio, Key | Export-Csv -LiteralPath $file -NoTypeInformation -Encoding UTF8 -Delimiter ';'
    Write-Host "Esportati $(@($rows).Count) job in $file" -ForegroundColor Green
}

function Set-Period {
    $answer = Read-Host "Nuovo periodo (es. 12h, 24h, 7d) [attuale: $script:Since]"
    if (-not $answer) { return }
    if ($answer -notmatch '^\d+(ms|s|m|h|d|w)$') {
        Write-Host "Periodo non valido '$answer': usa un numero seguito da m, h, d o w (es. 24h)." -ForegroundColor Yellow
        return
    }
    $script:Since = $answer
    $script:LastFaulted = $null
}

function Show-Menu {
    Write-Host ''
    Write-Host "=== mg3orch — processi in errore (ultime $script:Since) ===" -ForegroundColor Cyan
    Write-Host '  1) Job in errore: messaggio e VM'
    Write-Host '  2) Riepilogo per processo e VM'
    Write-Host '  3) Riepilogo per VM'
    Write-Host '  4) Messaggi di errore dei robot (log Error/Fatal)'
    Write-Host '  5) Messaggi di errore di un singolo job'
    Write-Host '  6) Esporta i job in errore in CSV'
    Write-Host '  7) Cambia periodo'
    Write-Host '  0) Esci'
}

# --- Avvio --------------------------------------------------------------------------------------

try {
    if ($Since -notmatch '^\d+(ms|s|m|h|d|w)$') {
        throw "Periodo non valido '$Since': usa un numero seguito da m, h, d o w (es. 24h)."
    }
    $script:Mg3 = Find-Mg3Exe $Exe
    $script:JsonConfig = New-JsonConfigCopy $Config
}
catch {
    Write-Host "ERRORE: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

$script:Since = $Since
$script:LastFaulted = $null
$previousEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
try {
    while ($true) {
        Show-Menu
        $choice = Read-Host 'Scelta'
        if ($null -eq $choice) { break }
        switch ($choice.Trim()) {
            '1' { Show-FaultedJobs }
            '2' { Show-FaultedSummary -Keys @('ReleaseName', 'HostMachineName') -Headers @('Processo', 'VM') }
            '3' { Show-FaultedSummary -Keys @('HostMachineName') -Headers @('VM') }
            '4' { Show-RobotErrors }
            '5' { Show-JobErrors }
            '6' { Export-FaultedJobs }
            '7' { Set-Period }
            '0' { return }
            default { Write-Host "Scelta non valida: '$choice'." -ForegroundColor Yellow }
        }
    }
}
finally {
    Remove-Item -LiteralPath $script:JsonConfig -Force -ErrorAction SilentlyContinue
    [Console]::OutputEncoding = $previousEncoding
}
