#requires -Version 5.1
# Revisão R1: corrige expansão de variável antes de ':' no Windows PowerShell 5.1.
[CmdletBinding()]
param(
    [switch]$PublicarValidado
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$Repository = 'fabiomesquita01/ts-se-tool-community'
$ExpectedLogin = 'fabiomesquita01'
$BaseVersion = '0.3.11.47'
$NewVersion = '0.3.11.48'
$ReleaseTag = 'v0.3.11.48'
$ReleaseLine = 'RC5.1H22'
$BaseRuntimeName = 'TS.SE.Tool.0.3.11.47-RC5.1H21-FINAL.zip'
$BaseRuntimeSha256 = '27be29fe86ae908193171da46243f4ec10311227ddde66372d6d16db0abcfd5f'
$CandidateName = 'TS.SE.Tool.0.3.11.48-RC5.1H22-CANDIDATE.zip'
$FinalName = 'TS.SE.Tool.0.3.11.48-RC5.1H22-FINAL.zip'
$StableAssetName = 'TS.SE.Tool.latest.zip'
$StateName = 'TSSE_RC5.1H22_BUILD_STATE.json'
$Downloads = Join-Path $env:USERPROFILE 'Downloads'
$ScriptPath = $MyInvocation.MyCommand.Path

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Sha512([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash.ToUpperInvariant()
}

function Set-Utf8NoBom([string]$Path, [string]$Content) {
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [System.IO.File]::WriteAllText(
        $Path,
        ($Content -replace '\r?\n', "`r`n"),
        (New-Object System.Text.UTF8Encoding($false))
    )
}

function Quote-Arg([string]$Value) {
    if ($null -eq $Value) { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }
    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Invoke-Native {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [string]$WorkingDirectory,
        [switch]$AllowFailure,
        [switch]$Quiet
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = (($Arguments | ForEach-Object { Quote-Arg $_ }) -join ' ')
    if ($WorkingDirectory) {
        if (-not (Test-Path -LiteralPath $WorkingDirectory -PathType Container)) {
            throw "Diretório de trabalho inexistente: $WorkingDirectory"
        }
        $psi.WorkingDirectory = $WorkingDirectory
    }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [System.Threading.Tasks.Task]::WaitAll(@($stdoutTask, $stderrTask))

    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    if (-not $Quiet) {
        if ($stdout) { Write-Host $stdout.TrimEnd() }
        if ($stderr) { Write-Host $stderr.TrimEnd() }
    }

    if (-not $AllowFailure -and $process.ExitCode -ne 0) {
        throw "Falha no comando: $FilePath $($psi.Arguments) | ExitCode=$($process.ExitCode)"
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        StdOut   = $stdout
        StdErr   = $stderr
    }
}

function Resolve-MSBuild {
    $candidates = New-Object System.Collections.Generic.List[string]
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        try {
            $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
            if (-not [String]::IsNullOrWhiteSpace($found)) { [void]$candidates.Add($found) }
        }
        catch { }
    }

    foreach ($candidate in @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )) {
        if (-not [String]::IsNullOrWhiteSpace($candidate)) { [void]$candidates.Add($candidate) }
    }

    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { [void]$candidates.Add($command.Source) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return ''
}

function Resolve-NuGet {
    $command = Get-Command nuget.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }

    $candidates = @(
        (Join-Path $Downloads 'nuget.exe'),
        (Join-Path $env:LOCALAPPDATA 'NuGet\nuget.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'NuGet\nuget.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $destination = Join-Path $Downloads 'nuget.exe'
    $temporary = $destination + '.download-' + [Guid]::NewGuid().ToString('N')
    Write-Host 'NuGet CLI não encontrado. Baixando do endereço oficial dist.nuget.org...' -ForegroundColor Yellow
    try {
        Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $temporary -UseBasicParsing -TimeoutSec 120
        $signature = Get-AuthenticodeSignature -FilePath $temporary
        if ($signature.Status -ne 'Valid') {
            throw "Assinatura Authenticode do nuget.exe não é válida: $($signature.Status)"
        }
        Move-Item -LiteralPath $temporary -Destination $destination -Force
        return $destination
    }
    finally {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}

function Set-RegexSingle {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Pattern,
        [Parameter(Mandatory=$true)][string]$Replacement,
        [Parameter(Mandatory=$true)][string]$Description
    )

    $content = [System.IO.File]::ReadAllText($Path)
    $regex = New-Object System.Text.RegularExpressions.Regex(
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::Multiline
    )
    $matches = $regex.Matches($content)
    if ($matches.Count -ne 1) {
        throw "Escopo inesperado em '$Description'. Correspondências: $($matches.Count). Arquivo: $Path"
    }
    $updated = $regex.Replace($content, $Replacement, 1)
    Set-Utf8NoBom -Path $Path -Content $updated
}

function Replace-LiteralRequired {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Old,
        [Parameter(Mandatory=$true)][string]$New,
        [Parameter(Mandatory=$true)][string]$Description
    )

    $content = [System.IO.File]::ReadAllText($Path)
    if ($content.Contains($New)) { return }
    if (-not $content.Contains($Old)) {
        throw "Trecho não encontrado em '$Description'. Arquivo: $Path"
    }
    Set-Utf8NoBom -Path $Path -Content ($content.Replace($Old, $New))
}

function Assert-Contains([string]$Path, [string[]]$Markers) {
    $content = [System.IO.File]::ReadAllText($Path)
    foreach ($marker in $Markers) {
        if (-not $content.Contains($marker)) {
            throw "Marcador obrigatório ausente: $marker | Arquivo: $Path"
        }
    }
}

function Apply-H22SourcePatch([string]$RepoRoot) {
    $project = Join-Path $RepoRoot 'TS SE Tool'
    $assembly = Join-Path $project 'Properties\AssemblyInfo.cs'
    $web = Join-Path $project 'CustomClasses\Utilities\Web_Utilities.cs'
    $formMethods = Join-Path $project 'FormMethods.cs'
    $about = Join-Path $project 'Forms\FormAboutBox.cs'
    $features = Join-Path $project 'ModernFeatureCompletionH20.cs'

    foreach ($path in @($assembly,$web,$formMethods,$about,$features)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Arquivo-fonte obrigatório ausente: $path"
        }
    }

    Set-RegexSingle -Path $assembly `
        -Pattern '^\[assembly: AssemblyDescription\(".*"\)\]\r?$' `
        -Replacement '[assembly: AssemblyDescription("Community-maintained save editor validated on Euro Truck Simulator 2 1.60 / save format 97; ATS is experimental")]' `
        -Description 'descrição do assembly'
    Set-RegexSingle -Path $assembly `
        -Pattern '^\[assembly: AssemblyVersion\("0\.3\.11\.(47|48)"\)\]\r?$' `
        -Replacement '[assembly: AssemblyVersion("0.3.11.48")]' `
        -Description 'AssemblyVersion'
    Set-RegexSingle -Path $assembly `
        -Pattern '^\[assembly: AssemblyFileVersion\("0\.3\.11\.(47|48)"\)\]\r?$' `
        -Replacement '[assembly: AssemblyFileVersion("0.3.11.48")]' `
        -Description 'AssemblyFileVersion'

    $webText = [System.IO.File]::ReadAllText($web)
    $webText = $webText.Replace(
        'Copyright 2016-2022 LIPtoH <fabiomalmsteen@gmail.com>',
        'Copyright 2016-2022 LIPtoH <liptoh.codebase@gmail.com>')
    Set-Utf8NoBom -Path $web -Content $webText
    Set-RegexSingle -Path $web `
        -Pattern '^\s*internal string linkDownloadVersion\s*=\s*"[^"]+";\s*$' `
        -Replacement '        internal string linkDownloadVersion = "https://github.com/fabiomesquita01/ts-se-tool-community/releases/latest/download/TS.SE.Tool.latest.zip";' `
        -Description 'URL primária do atualizador'
    Set-RegexSingle -Path $web `
        -Pattern '^\s*internal string linkDownloadVersion2\s*=\s*"[^"]+";\s*$' `
        -Replacement '        internal string linkDownloadVersion2 = "https://github.com/fabiomesquita01/ts-se-tool-community/releases/latest/download/TS.SE.Tool.latest.zip";' `
        -Description 'URL secundária do atualizador'

    Set-RegexSingle -Path $formMethods `
        -Pattern '^\s*SupportedGameVersionETS2\s*=\s*"[^"]*";.*$' `
        -Replacement '                SupportedGameVersionETS2 = "ETS2 1.60 / save 97 tested and validated; formats 61-96 recognized"; //RC5.1H22 evidence scope' `
        -Description 'compatibilidade ETS2 dentro do programa'
    Set-RegexSingle -Path $formMethods `
        -Pattern '^\s*SupportedGameVersionATS\s*=\s*"[^"]*";.*$' `
        -Replacement '                SupportedGameVersionATS = "Experimental / not validated in RC5.1H22"; //No ATS test environment available' `
        -Description 'compatibilidade ATS dentro do programa'

    Set-RegexSingle -Path $about `
        -Pattern '^\s*labelATSversion\.Text\s*=\s*"[^"]*";\s*$' `
        -Replacement '            labelATSversion.Text = "Experimental / not validated in RC5.1H22 community release";' `
        -Description 'status ATS na tela Sobre'
    Set-RegexSingle -Path $about `
        -Pattern '^\s*"ETS2 validation: [^"]*" \+ Environment\.NewLine \+\r?$' `
        -Replacement '                                       "ETS2 validation: version 1.60 / save format 97" + Environment.NewLine +' `
        -Description 'validação ETS2 na tela Sobre'

    Set-RegexSingle -Path $features `
        -Pattern '^\s*"SUPORTADO E TESTADO \| Euro Truck Simulator 2 \| [^"]*",\s*$' `
        -Replacement '                "TESTADO E VALIDADO | Euro Truck Simulator 2 1.60 | save 97; formatos 61-96 reconhecidos, sem homologação integral",' `
        -Description 'matriz interna de compatibilidade'

    $featureText = [System.IO.File]::ReadAllText($features)
    $featureText = $featureText.Replace('RC5.1H21 extracted-def catalog indexed:', 'RC5.1H22 extracted-def catalog indexed:')
    $featureText = $featureText.Replace('RC5.1H21 final release initialized: ets2_tested=true', 'RC5.1H22 release initialized: ets2_version=1.60, ets2_tested=true')
    $featureText = $featureText.Replace('MATRIZ_RECURSOS_H21_FINAL.txt', 'MATRIZ_RECURSOS_H22_FINAL.txt')
    $featureText = $featureText.Replace('H21 — matriz final de recursos', 'H22 — matriz final de recursos')
    Set-Utf8NoBom -Path $features -Content $featureText

    Assert-Contains -Path $assembly -Markers @('AssemblyFileVersion("0.3.11.48")')
    Assert-Contains -Path $web -Markers @(
        'releases/latest/download/TS.SE.Tool.latest.zip',
        'Copyright 2016-2022 LIPtoH <liptoh.codebase@gmail.com>'
    )
    Assert-Contains -Path $formMethods -Markers @('ETS2 1.60 / save 97 tested and validated')
    Assert-Contains -Path $features -Markers @('RC5.1H22 release initialized: ets2_version=1.60')
}

function Apply-H22Documentation([string]$RepoRoot) {
    foreach ($relative in @('README.md','README.pt-BR.md')) {
        $path = Join-Path $RepoRoot $relative
        Set-RegexSingle -Path $path `
            -Pattern '^\[!\[ETS2\]\([^\r\n]+\)\]\(docs/COMPATIBILITY\.md\)\r?$' `
            -Replacement '[![ETS2](https://img.shields.io/badge/ETS2-1.60%20%7C%20save%2097%20validado-4caf50.svg)](docs/COMPATIBILITY.md)' `
            -Description "badge ETS2 em $relative"
        Set-RegexSingle -Path $path `
            -Pattern '^> \*\*Versão atual:\*\* `[^`]+`\s*\r?$' `
            -Replacement '> **Versão atual:** `0.3.11.48 — RC5.1H22 Final`  ' `
            -Description "versão em $relative"
        Set-RegexSingle -Path $path `
            -Pattern '^> \*\*Suporte validado:\*\*.*$' `
            -Replacement '> **Suporte validado:** Euro Truck Simulator 2 **1.60**, save moderno de **formato 97**  ' `
            -Description "suporte validado em $relative"
        Set-RegexSingle -Path $path `
            -Pattern '^A validação comunitária principal foi concluída.*$' `
            -Replacement 'A validação comunitária principal foi concluída no **Euro Truck Simulator 2 1.60**, usando save moderno de **formato 97**. Os formatos 61–96 continuam reconhecidos pela camada de compatibilidade, mas não foram integralmente homologados nesta release.' `
            -Description "parágrafo de compatibilidade em $relative"
        Set-RegexSingle -Path $path `
            -Pattern '^Use somente a seção \[Releases\].*$' `
            -Replacement 'Use somente a seção [Releases](https://github.com/fabiomesquita01/ts-se-tool-community/releases/latest). Verifique `SHA256SUMS.txt` antes de executar. Usuários da 0.3.11.47 precisam instalar a 0.3.11.48 manualmente uma vez; a partir da H22, o atualizador interno usa o asset estável `TS.SE.Tool.latest.zip`, publicado na release mais recente e validado por SHA-512.' `
            -Description "download em $relative"
    }

    $readmeEn = Join-Path $RepoRoot 'README.en.md'
    Set-RegexSingle -Path $readmeEn `
        -Pattern '^> \*\*Current release:\*\* `[^`]+`\s*\r?$' `
        -Replacement '> **Current release:** `0.3.11.48 — RC5.1H22 Final`  ' `
        -Description 'current release in README.en.md'
    Set-RegexSingle -Path $readmeEn `
        -Pattern '^> \*\*Validated target:\*\*.*$' `
        -Replacement '> **Validated target:** Euro Truck Simulator 2 **1.60**, modern save **format 97**  ' `
        -Description 'validated target in README.en.md'
    Set-RegexSingle -Path $readmeEn `
        -Pattern '^ETS2 was .*ATS is experimental and unvalidated.*$' `
        -Replacement 'ETS2 was tested and validated on **version 1.60** with modern save **format 97**. Save formats 61–96 remain recognized but were not fully homologated in this release. ATS is experimental and unvalidated.' `
        -Description 'compatibility in README.en.md'

    $compatibility = @'
# Compatibilidade

## Euro Truck Simulator 2

- **Versão do jogo testada e validada:** `1.60`.
- **Formato de save testado e validado:** `97`.
- **Versão do editor:** `0.3.11.48 — RC5.1H22 Final`.
- **Plataforma:** Windows com .NET Framework 4.7.2.
- Os formatos de save `61–96` continuam reconhecidos pela camada de compatibilidade, mas não foram integralmente homologados nesta release.

A indicação de compatibilidade não é uma garantia automática para versões posteriores do ETS2, DLCs ou mods. Trabalhe em uma cópia de save manual, mantenha o jogo fechado e preserve o backup criado antes da gravação.

## American Truck Simulator

O ATS permanece **experimental e não validado**, pois não houve ambiente real de teste. Relatos voluntários devem informar versão do jogo, formato do save, DLCs/mods e anexar `log.log`, `errorlog.log` e `compatibility.log`.

## Atualização do programa

O verificador consulta `latest.txt`. O download automático usa o endereço estável `releases/latest/download/TS.SE.Tool.latest.zip` e valida o pacote com SHA-512 antes de iniciar o atualizador. O download manual pela página Releases permanece disponível.
'@
    Set-Utf8NoBom -Path (Join-Path $RepoRoot 'docs\COMPATIBILITY.md') -Content $compatibility

    $validation = @'
# Validação da RC5.1H22

## Escopo

- Produto: TS SE Tool Community `0.3.11.48 — RC5.1H22 Final`.
- Base funcional preservada: `0.3.11.47 — RC5.1H21 Final`.
- Jogo validado: **Euro Truck Simulator 2 1.60**.
- Formato de save validado: **97**.
- ATS: experimental e não validado.

## Alterações da H22

- correção do endereço usado pelo atualizador automático;
- asset estável `TS.SE.Tool.latest.zip` na release mais recente;
- validação SHA-512 preservada antes da atualização;
- compatibilidade exibida no programa sem ampliar a homologação para saves antigos;
- versão incrementada para `0.3.11.48`;
- documentação, página pública, release e formulários sincronizados.

## Limites preservados

Contratar/demitir motoristas, edição bruta do grafo, colagem completa de veículos, reordenação estrutural e caminhos de acessórios não verificados continuam bloqueados.
'@
    Set-Utf8NoBom -Path (Join-Path $RepoRoot 'docs\VALIDACAO_H22.md') -Content $validation

    $testing = @'
# Testes da RC5.1H22

1. Confirmar build Release/Any CPU com zero erros.
2. Abrir `TS SE Tool.exe` e confirmar versão `0.3.11.48`.
3. Abrir a tela Sobre e confirmar `ETS2 1.60 / save 97` e ATS experimental.
4. Confirmar no log o marcador `RC5.1H22 release initialized`.
5. Carregar uma cópia de save ETS2 1.60 no formato 97 sem salvar.
6. Executar uma alteração isolada, salvar uma única vez e recarregar.
7. Abrir o save no ETS2 e validar o resultado.
8. Confirmar que `errorlog.log` contém somente erros reais e que diagnósticos ficam em `compatibility.log`.
9. Testar **Check for updates**; o download deve usar `TS.SE.Tool.latest.zip` e passar na validação SHA-512.
10. Confirmar que os recursos bloqueados por segurança permanecem indisponíveis.
'@
    Set-Utf8NoBom -Path (Join-Path $RepoRoot 'docs\TESTING.md') -Content $testing

    $releaseNotes = @'
# TS SE Tool Community 0.3.11.48 — RC5.1H22 Final

Atualização de manutenção baseada na H21 homologada.

## Correções

- atualizador automático corrigido para baixar um ZIP real pela URL estável da release mais recente;
- usuários da 0.3.11.47 devem instalar esta versão manualmente uma vez, pois o atualizador antigo apontava para uma página HTML;
- SHA-512 mantido como validação obrigatória antes da atualização;
- compatibilidade declarada de forma precisa: **ETS2 1.60 + save 97 testado e validado**;
- formatos 61–96 identificados como reconhecidos, sem alegação de homologação integral;
- tela Sobre, Central de Recursos, documentação e página pública sincronizadas;
- atribuição original de LIPtoH preservada.

## Segurança

A H22 não libera contratação/demissão de motoristas, editor bruto, colagem completa de veículos ou caminhos não verificados. Use uma cópia de save manual e mantenha backups.

## ATS

American Truck Simulator permanece experimental e não validado.
'@
    Set-Utf8NoBom -Path (Join-Path $RepoRoot 'RELEASE_NOTES_v0.3.11.48.md') -Content $releaseNotes

    $page = Join-Path $RepoRoot 'docs\index.html'
    Set-RegexSingle -Path $page `
        -Pattern '<meta name="description" content="[^"]*">' `
        -Replacement '<meta name="description" content="TS SE Tool Community 0.3.11.48, editor de saves testado e validado no Euro Truck Simulator 2 1.60 com save 97.">' `
        -Description 'meta description do GitHub Pages'
    Set-RegexSingle -Path $page `
        -Pattern '<span class="tag">[^<]+</span>' `
        -Replacement '<span class="tag">0.3.11.48 · RC5.1H22 Final</span>' `
        -Description 'versão no GitHub Pages'
    Set-RegexSingle -Path $page `
        -Pattern '<div class="warn">.*?</div>' `
        -Replacement '<div class="warn"><strong>ETS2 1.60 com save 97 testado e validado.</strong> Formatos 61–96 são reconhecidos, mas não integralmente homologados. ATS é experimental e não validado. Edite apenas cópias de saves manuais.</div>' `
        -Description 'aviso do GitHub Pages'

    $bugForm = Join-Path $RepoRoot '.github\ISSUE_TEMPLATE\bug_report.yml'
    if (Test-Path -LiteralPath $bugForm) {
        $bug = [System.IO.File]::ReadAllText($bugForm)
        $bug = $bug.Replace('0.3.11.47', '0.3.11.48')
        $bug = $bug.Replace('A versão 0.3.11.47', 'A versão 0.3.11.48')
        Set-Utf8NoBom -Path $bugForm -Content $bug
    }

    $changePath = Join-Path $RepoRoot 'CHANGELOG.md'
    $change = [System.IO.File]::ReadAllText($changePath)
    if (-not $change.Contains('## 0.3.11.48 — RC5.1H22 Final')) {
        $entry = @'
## 0.3.11.48 — RC5.1H22 Final

- Corrigido o download automático para o asset estável `TS.SE.Tool.latest.zip`.
- Mantida a validação SHA-512 antes da atualização.
- Compatibilidade documentada como ETS2 1.60 + save 97 testado e validado.
- Formatos 61–96 permanecem reconhecidos, sem homologação integral declarada.
- Tela Sobre, Central de Recursos, GitHub Pages e release sincronizados.

'@
        Set-Utf8NoBom -Path $changePath -Content ($entry + $change)
    }
}

function Regenerate-SourceManifest([string]$RepoRoot) {
    $manifestPath = Join-Path $RepoRoot 'SOURCE_MANIFEST_SHA256.txt'
    $lines = New-Object System.Collections.Generic.List[string]
    $gitPrefix = (Join-Path $RepoRoot '.git') + [System.IO.Path]::DirectorySeparatorChar
    Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -Force |
        Where-Object {
            -not $_.FullName.StartsWith($gitPrefix, [StringComparison]::OrdinalIgnoreCase) -and
            $_.FullName -ne $manifestPath -and
            $_.FullName -notmatch '[\\/](bin|obj|packages)[\\/]'
        } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($RepoRoot.Length + 1).Replace('\','/')
            [void]$lines.Add("$(Get-Sha256 $_.FullName)  $relative")
        }
    [System.IO.File]::WriteAllLines($manifestPath, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
}

function New-SourceZip([string]$RepoRoot, [string]$DestinationZip) {
    $stage = Join-Path $env:TEMP ('tsse-source-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        Get-ChildItem -LiteralPath $RepoRoot -Force |
            Where-Object { $_.Name -ne '.git' } |
            Copy-Item -Destination $stage -Recurse -Force
        Get-ChildItem -LiteralPath $stage -Directory -Recurse -Force |
            Where-Object { $_.Name -in @('bin','obj','packages') } |
            Sort-Object FullName -Descending |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $DestinationZip) { Remove-Item -LiteralPath $DestinationZip -Force }
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $stage,
            $DestinationZip,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
    }
    finally {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-GitHubTools {
    $gitCommand = Get-Command git.exe -ErrorAction Stop
    $ghCommand = Get-Command gh.exe -ErrorAction Stop
    $loginResult = Invoke-Native -FilePath $ghCommand.Source -Arguments @('api','user','--jq','.login') -Quiet
    $login = $loginResult.StdOut.Trim()
    if ($login -ne $ExpectedLogin) {
        throw "Conta GitHub incorreta. Esperado: $ExpectedLogin | Encontrado: $login"
    }
    return [pscustomobject]@{ Git=$gitCommand.Source; Gh=$ghCommand.Source; Login=$login }
}

function Wait-Workflow {
    param(
        [string]$Gh,
        [string]$Workflow,
        [string]$HeadSha,
        [int]$TimeoutSeconds = 900
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $result = Invoke-Native -FilePath $Gh -Arguments @(
            'run','list','--repo',$Repository,'--workflow',$Workflow,
            '--branch','main','--limit','10',
            '--json','databaseId,status,conclusion,url,headSha'
        ) -AllowFailure -Quiet
        if ($result.ExitCode -eq 0 -and -not [String]::IsNullOrWhiteSpace($result.StdOut)) {
            $runs = @($result.StdOut | ConvertFrom-Json)
            $run = $runs | Where-Object { $_.headSha -eq $HeadSha } | Select-Object -First 1
            if ($null -ne $run) {
                Write-Host ("{0}: {1} / {2} — {3}" -f $Workflow, $run.status, $run.conclusion, $run.url)
                if ($run.status -eq 'completed') {
                    if ($run.conclusion -ne 'success') {
                        throw "Workflow $Workflow terminou com: $($run.conclusion)"
                    }
                    return
                }
            }
        }
        Start-Sleep -Seconds 10
    } while ((Get-Date) -lt $deadline)
    throw "Tempo esgotado aguardando o workflow $Workflow para o commit $HeadSha."
}

function Build-Candidate {
    Write-Host 'TS SE Tool Community — preparação completa RC5.1H22' -ForegroundColor Cyan
    Write-Host 'Modo: BUILD CANDIDATE (não altera o GitHub)' -ForegroundColor Yellow

    Write-Step 'Validando base homologada e ferramentas'
    $tools = Get-GitHubTools
    Write-Host "GitHub autenticado: $($tools.Login)" -ForegroundColor Green

    $baseZip = Join-Path $Downloads $BaseRuntimeName
    if (-not (Test-Path -LiteralPath $baseZip -PathType Leaf)) {
        throw "ZIP homologado H21 não encontrado: $baseZip"
    }
    $baseHash = Get-Sha256 $baseZip
    if ($baseHash -ne $BaseRuntimeSha256) {
        throw "O ZIP H21 não corresponde à base homologada. Esperado: $BaseRuntimeSha256 | Encontrado: $baseHash"
    }

    $msbuild = Resolve-MSBuild
    if ([String]::IsNullOrWhiteSpace($msbuild)) {
        throw 'MSBuild 17.x não encontrado. Instale Visual Studio 2022 Build Tools com .NET Framework 4.7.2 Targeting Pack.'
    }
    $nuget = Resolve-NuGet
    if ([String]::IsNullOrWhiteSpace($nuget)) {
        throw 'nuget.exe não encontrado. Instale o NuGet CLI ou coloque nuget.exe em Downloads.'
    }

    $temp = Join-Path $env:TEMP ('tsse-h22-build-' + [Guid]::NewGuid().ToString('N'))
    $repo = Join-Path $temp 'repo'
    $runtime = Join-Path $temp 'runtime'
    $candidate = Join-Path $Downloads $CandidateName
    $statePath = Join-Path $Downloads $StateName

    try {
        New-Item -ItemType Directory -Path $temp -Force | Out-Null
        Write-Step 'Clonando a fonte comunitária atual'
        Invoke-Native -FilePath $tools.Gh -Arguments @('repo','clone',$Repository,$repo,'--','--depth','1') | Out-Null
        $baseCommit = (Invoke-Native -FilePath $tools.Git -Arguments @('rev-parse','HEAD') -WorkingDirectory $repo -Quiet).StdOut.Trim()

        $assemblyPath = Join-Path $repo 'TS SE Tool\Properties\AssemblyInfo.cs'
        Assert-Contains -Path $assemblyPath -Markers @('AssemblyFileVersion("0.3.11.47")')

        Write-Step 'Aplicando correções isoladas da H22'
        Apply-H22SourcePatch -RepoRoot $repo

        Write-Step 'Restaurando dependências'
        Invoke-Native -FilePath $nuget -Arguments @('restore','TS SE Tool.sln','-NonInteractive') -WorkingDirectory $repo | Out-Null

        Write-Step 'Compilando Release / Any CPU'
        Invoke-Native -FilePath $msbuild -Arguments @(
            'TS SE Tool.sln','/t:Rebuild','/m',
            '/p:Configuration=Release','/p:Platform=Any CPU','/v:minimal'
        ) -WorkingDirectory $repo | Out-Null

        $builtExe = Join-Path $repo 'TS SE Tool\bin\Release\TS SE Tool.exe'
        $builtConfig = Join-Path $repo 'TS SE Tool\bin\Release\TS SE Tool.exe.config'
        if (-not (Test-Path -LiteralPath $builtExe -PathType Leaf)) {
            throw "Executável compilado não encontrado: $builtExe"
        }
        $builtVersion = (Get-Item -LiteralPath $builtExe).VersionInfo.FileVersion
        if ($builtVersion -notlike '0.3.11.48*') {
            throw "Versão inesperada no executável: $builtVersion"
        }

        Write-Step 'Montando o pacote candidato sobre a base H21 homologada'
        New-Item -ItemType Directory -Path $runtime -Force | Out-Null
        [System.IO.Compression.ZipFile]::ExtractToDirectory($baseZip, $runtime)
        Copy-Item -LiteralPath $builtExe -Destination (Join-Path $runtime 'TS SE Tool.exe') -Force
        if (Test-Path -LiteralPath $builtConfig -PathType Leaf) {
            Copy-Item -LiteralPath $builtConfig -Destination (Join-Path $runtime 'TS SE Tool.exe.config') -Force
        }
        $builtLibs = Join-Path $repo 'TS SE Tool\bin\Release\libs'
        if (Test-Path -LiteralPath $builtLibs -PathType Container) {
            Copy-Item -Path (Join-Path $builtLibs '*') -Destination (Join-Path $runtime 'libs') -Recurse -Force
        }

        foreach ($logName in @('log.log','errorlog.log','compatibility.log')) {
            $log = Join-Path $runtime $logName
            if (Test-Path -LiteralPath $log -PathType Leaf) { Clear-Content -LiteralPath $log }
        }
        Get-ChildItem -LiteralPath $runtime -Filter 'README_RC5*.txt' -File -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem -LiteralPath $runtime -Filter 'TESTES_FINAIS_RC5*.txt' -File -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem -LiteralPath $runtime -Filter 'LIMITACOES_E_SUPORTE_RC5*.txt' -File -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem -LiteralPath $runtime -Filter 'RELEASE_NOTES_v0.3.11.*.md' -File -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem -LiteralPath $runtime -Filter 'MANIFEST_RC5*.txt' -File -ErrorAction SilentlyContinue | Remove-Item -Force

        $runtimeReadme = @'
TS SE Tool 0.3.11.48 RC5.1H22 — CANDIDATO PARA VALIDAÇÃO

COMPATIBILIDADE
- Euro Truck Simulator 2 1.60 + save 97: testado e validado na base H21.
- Formatos 61–96: reconhecidos, sem homologação integral declarada.
- American Truck Simulator: experimental e não validado.

CORREÇÕES H22
- Atualizador passa a baixar o ZIP estável TS.SE.Tool.latest.zip.
- Validação SHA-512 continua obrigatória antes da atualização.
- Tela Sobre e Central de Recursos exibem o escopo correto de compatibilidade.
- Recursos bloqueados por segurança permanecem bloqueados.

ANTES DE PUBLICAR
- Execute os testes descritos em TESTES_FINAIS_RC5.1H22.txt.
- Não use save principal.
- Salve uma única vez em cada teste controlado.
'@
        Set-Utf8NoBom -Path (Join-Path $runtime 'README_RC5.1H22_CANDIDATE.txt') -Content $runtimeReadme

        $tests = @'
TESTES FINAIS RC5.1H22

1. Abrir o programa e confirmar versão 0.3.11.48.
2. Confirmar ETS2 1.60 / save 97 na tela Sobre e na Central de Recursos.
3. Confirmar ATS como experimental / não validado.
4. Carregar uma cópia de save sem salvar.
5. Executar uma alteração isolada, salvar uma única vez e recarregar.
6. Abrir o save no ETS2 1.60.
7. Confirmar log RC5.1H22 release initialized.
8. Confirmar errorlog.log vazio durante uso normal.
9. Confirmar diagnósticos em compatibility.log.
10. Testar Check for updates somente após a release H22 existir; o download deve usar TS.SE.Tool.latest.zip e validar SHA-512.
11. Confirmar que contratar/demitir, editor bruto, colagem completa e caminhos não verificados continuam bloqueados.
'@
        Set-Utf8NoBom -Path (Join-Path $runtime 'TESTES_FINAIS_RC5.1H22.txt') -Content $tests

        $manifest = @"
TS SE Tool 0.3.11.48 RC5.1H22 — manifesto do candidato
Base runtime: $BaseRuntimeName
SHA-256 da base: $baseHash
Commit-base do repositório: $baseCommit
Executável compilado: $builtVersion
SHA-256 do executável: $(Get-Sha256 $builtExe)
MSBuild: $msbuild
NuGet: $nuget
Data local: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
"@
        Set-Utf8NoBom -Path (Join-Path $runtime 'MANIFEST_RC5.1H22_CANDIDATE.txt') -Content $manifest

        $checksumPath = Join-Path $runtime 'SHA256SUMS.txt'
        $checksumLines = New-Object System.Collections.Generic.List[string]
        Get-ChildItem -LiteralPath $runtime -Recurse -File |
            Where-Object { $_.FullName -ne $checksumPath } |
            Sort-Object FullName |
            ForEach-Object {
                $relative = $_.FullName.Substring($runtime.Length + 1)
                [void]$checksumLines.Add("$(Get-Sha256 $_.FullName)  $relative")
            }
        [System.IO.File]::WriteAllLines($checksumPath, $checksumLines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))

        if (Test-Path -LiteralPath $candidate) { Remove-Item -LiteralPath $candidate -Force }
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $runtime,
            $candidate,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)

        $candidateSha256 = Get-Sha256 $candidate
        $candidateSha512 = Get-Sha512 $candidate
        $scriptSha256 = if ($ScriptPath -and (Test-Path -LiteralPath $ScriptPath)) { Get-Sha256 $ScriptPath } else { '' }
        $state = [ordered]@{
            schema = 1
            version = $NewVersion
            releaseLine = $ReleaseLine
            repository = $Repository
            baseCommit = $baseCommit
            baseRuntime = $BaseRuntimeName
            baseRuntimeSha256 = $baseHash
            candidate = $CandidateName
            candidateSha256 = $candidateSha256
            candidateSha512 = $candidateSha512
            scriptSha256 = $scriptSha256
            builtAt = (Get-Date).ToString('o')
        }
        Set-Utf8NoBom -Path $statePath -Content ($state | ConvertTo-Json -Depth 4)

        Write-Host ''
        Write-Host 'CANDIDATO RC5.1H22 CRIADO' -ForegroundColor Green
        Write-Host "ZIP: $candidate"
        Write-Host "SHA-256: $candidateSha256"
        Write-Host "SHA-512: $candidateSha512"
        Write-Host "Estado: $statePath"
        Write-Host 'O GitHub não foi alterado. Valide o candidato antes da publicação.' -ForegroundColor Yellow
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Publish-Validated {
    Write-Host 'TS SE Tool Community — publicação completa RC5.1H22' -ForegroundColor Cyan
    Write-Host 'Modo: PUBLICAR CANDIDATO VALIDADO' -ForegroundColor Yellow

    Write-Step 'Validando candidato, estado e autenticação'
    $tools = Get-GitHubTools
    Write-Host "GitHub autenticado: $($tools.Login)" -ForegroundColor Green

    $candidate = Join-Path $Downloads $CandidateName
    $statePath = Join-Path $Downloads $StateName
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Candidato ausente: $candidate" }
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) { throw "Estado de build ausente: $statePath" }
    $state = [System.IO.File]::ReadAllText($statePath) | ConvertFrom-Json
    if ($state.version -ne $NewVersion -or $state.repository -ne $Repository) {
        throw 'O arquivo de estado não pertence à RC5.1H22 esperada.'
    }
    $candidateSha256 = Get-Sha256 $candidate
    $candidateSha512 = Get-Sha512 $candidate
    if ($candidateSha256 -ne $state.candidateSha256 -or $candidateSha512 -ne $state.candidateSha512) {
        throw 'O ZIP candidato foi alterado após o build.'
    }

    $temp = Join-Path $env:TEMP ('tsse-h22-publish-' + [Guid]::NewGuid().ToString('N'))
    $repo = Join-Path $temp 'repo'
    $releaseFiles = Join-Path $temp 'release'
    $verifyRuntime = Join-Path $temp 'verify-runtime'
    New-Item -ItemType Directory -Path $releaseFiles -Force | Out-Null

    try {
        Write-Step 'Clonando e verificando a mesma base usada no build'
        Invoke-Native -FilePath $tools.Gh -Arguments @('repo','clone',$Repository,$repo,'--','--depth','1') | Out-Null
        $currentHead = (Invoke-Native -FilePath $tools.Git -Arguments @('rev-parse','HEAD') -WorkingDirectory $repo -Quiet).StdOut.Trim()
        if ($currentHead -ne $state.baseCommit) {
            throw "O repositório mudou após o build. Commit do build: $($state.baseCommit) | Atual: $currentHead. Gere novamente o candidato."
        }

        New-Item -ItemType Directory -Path $verifyRuntime -Force | Out-Null
        [System.IO.Compression.ZipFile]::ExtractToDirectory($candidate, $verifyRuntime)
        $candidateExe = Join-Path $verifyRuntime 'TS SE Tool.exe'
        if (-not (Test-Path -LiteralPath $candidateExe -PathType Leaf)) { throw 'Executável ausente no candidato.' }
        $candidateVersion = (Get-Item -LiteralPath $candidateExe).VersionInfo.FileVersion
        if ($candidateVersion -notlike '0.3.11.48*') { throw "Versão inesperada no candidato: $candidateVersion" }

        Write-Step 'Aplicando fonte e documentação finais'
        Apply-H22SourcePatch -RepoRoot $repo
        Apply-H22Documentation -RepoRoot $repo

        if ($ScriptPath -and (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
            Copy-Item -LiteralPath $ScriptPath -Destination (Join-Path $repo 'tools\Aplicar-Atualizacao-Completa-TSSE-RC5.1H22-R1.ps1') -Force
        }

        Write-Step 'Finalizando o pacote runtime validado'
        Get-ChildItem -LiteralPath $verifyRuntime -Filter '*CANDIDATE*' -File -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem -LiteralPath $verifyRuntime -Filter 'README_RC5*.txt' -File -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem -LiteralPath $verifyRuntime -Filter 'TESTES_FINAIS_RC5*.txt' -File -ErrorAction SilentlyContinue | Remove-Item -Force
        Get-ChildItem -LiteralPath $verifyRuntime -Filter 'MANIFEST_RC5*.txt' -File -ErrorAction SilentlyContinue | Remove-Item -Force
        Remove-Item -LiteralPath (Join-Path $verifyRuntime 'SHA256SUMS.txt') -Force -ErrorAction SilentlyContinue

        $finalRuntimeReadme = @'
TS SE Tool 0.3.11.48 RC5.1H22 FINAL

COMPATIBILIDADE
- Euro Truck Simulator 2 1.60 + save 97: testado e validado.
- Formatos 61–96: reconhecidos, sem homologação integral declarada.
- American Truck Simulator: experimental e não validado.

CORREÇÕES H22
- Atualizador usa o asset estável TS.SE.Tool.latest.zip.
- Usuários da 0.3.11.47 precisam instalar a H22 manualmente uma vez.
- Validação SHA-512 continua obrigatória antes da atualização.
- Tela Sobre, Central de Recursos, documentação e página pública sincronizadas.
- Recursos bloqueados por segurança permanecem bloqueados.

PROJETO E SUPORTE
- Repositório: https://github.com/fabiomesquita01/ts-se-tool-community
- Issues: https://github.com/fabiomesquita01/ts-se-tool-community/issues
- Suporte: fabiomalmsteen@gmail.com
- Instagram: @fabio.meskita — https://www.instagram.com/fabio.meskita/
- Projeto original: LIPtoH — https://github.com/LIPtoH/TS-SE-Tool
- Licença: Apache License 2.0.
'@
        Set-Utf8NoBom -Path (Join-Path $verifyRuntime 'README_RC5.1H22_FINAL.txt') -Content $finalRuntimeReadme

        $finalTests = @'
TESTES FINAIS RC5.1H22

1. Confirmar versão 0.3.11.48.
2. Confirmar ETS2 1.60 / save 97 na tela Sobre e na Central de Recursos.
3. Confirmar ATS experimental / não validado.
4. Carregar uma cópia de save ETS2 1.60 no formato 97.
5. Executar uma alteração isolada, salvar uma única vez e recarregar.
6. Abrir o save no ETS2.
7. Confirmar errorlog.log sem falsos positivos e compatibility.log separado.
8. Testar Check for updates e confirmar download de TS.SE.Tool.latest.zip com SHA-512 válido.
9. Confirmar que recursos inseguros continuam bloqueados.
'@
        Set-Utf8NoBom -Path (Join-Path $verifyRuntime 'TESTES_FINAIS_RC5.1H22.txt') -Content $finalTests

        $finalManifest = @"
TS SE Tool 0.3.11.48 RC5.1H22 FINAL — manifesto de publicação
Candidato validado: $CandidateName
SHA-256 do candidato: $candidateSha256
SHA-512 do candidato: $candidateSha512
Commit-base do build: $($state.baseCommit)
Executável: $candidateVersion
Repositório: https://github.com/$Repository
ETS2 validado: 1.60 / save 97
ATS validado: não
Data local da publicação: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
"@
        Set-Utf8NoBom -Path (Join-Path $verifyRuntime 'MANIFEST_RC5.1H22_FINAL.txt') -Content $finalManifest

        $runtimeChecksumPath = Join-Path $verifyRuntime 'SHA256SUMS.txt'
        $runtimeChecksumLines = New-Object System.Collections.Generic.List[string]
        Get-ChildItem -LiteralPath $verifyRuntime -Recurse -File |
            Where-Object { $_.FullName -ne $runtimeChecksumPath } |
            Sort-Object FullName |
            ForEach-Object {
                $relative = $_.FullName.Substring($verifyRuntime.Length + 1)
                [void]$runtimeChecksumLines.Add("$(Get-Sha256 $_.FullName)  $relative")
            }
        [System.IO.File]::WriteAllLines($runtimeChecksumPath, $runtimeChecksumLines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))

        $finalZip = Join-Path $releaseFiles $FinalName
        $stableZip = Join-Path $releaseFiles $StableAssetName
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $verifyRuntime,
            $finalZip,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)
        Copy-Item -LiteralPath $finalZip -Destination $stableZip -Force
        $finalSha256 = Get-Sha256 $finalZip
        $finalSha512 = Get-Sha512 $finalZip

        $latestLine = "TS.SE.Tool.0.3.11.48.zip`t$finalSha512"
        Set-Utf8NoBom -Path (Join-Path $repo 'latest.txt') -Content ($latestLine + "`r`n")
        Regenerate-SourceManifest -RepoRoot $repo

        Write-Step 'Criando commit e enviando a branch main'
        Invoke-Native -FilePath $tools.Git -Arguments @('config','user.name','Fabio Mesquita') -WorkingDirectory $repo | Out-Null
        Invoke-Native -FilePath $tools.Git -Arguments @('config','user.email','fabiomalmsteen@gmail.com') -WorkingDirectory $repo | Out-Null
        Invoke-Native -FilePath $tools.Git -Arguments @('add','--all') -WorkingDirectory $repo | Out-Null
        $diff = Invoke-Native -FilePath $tools.Git -Arguments @('diff','--cached','--quiet') -WorkingDirectory $repo -AllowFailure -Quiet
        if ($diff.ExitCode -eq 0) { throw 'Nenhuma alteração foi detectada para a H22.' }
        Invoke-Native -FilePath $tools.Git -Arguments @('commit','-m','Publica TS SE Tool Community 0.3.11.48 RC5.1H22') -WorkingDirectory $repo | Out-Null
        Invoke-Native -FilePath $tools.Git -Arguments @('push','origin','main') -WorkingDirectory $repo | Out-Null
        $headSha = (Invoke-Native -FilePath $tools.Git -Arguments @('rev-parse','HEAD') -WorkingDirectory $repo -Quiet).StdOut.Trim()

        Write-Step 'Gerando pacote-fonte e checksums da release'
        $sourceZip = Join-Path $releaseFiles 'TS.SE.Tool.0.3.11.48-RC5.1H22-SOURCE.zip'
        New-SourceZip -RepoRoot $repo -DestinationZip $sourceZip
        $releaseScript = $null
        if ($ScriptPath -and (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
            $releaseScript = Join-Path $releaseFiles 'Aplicar-Atualizacao-Completa-TSSE-RC5.1H22-R1.ps1'
            Copy-Item -LiteralPath $ScriptPath -Destination $releaseScript -Force
        }
        $sha256File = Join-Path $releaseFiles 'SHA256SUMS.txt'
        $sha512File = Join-Path $releaseFiles 'SHA512SUMS.txt'
        $sha256Lines = New-Object System.Collections.Generic.List[string]
        $sha512Lines = New-Object System.Collections.Generic.List[string]
        foreach ($file in Get-ChildItem -LiteralPath $releaseFiles -File | Where-Object { $_.Name -notin @('SHA256SUMS.txt','SHA512SUMS.txt') } | Sort-Object Name) {
            [void]$sha256Lines.Add("$(Get-Sha256 $file.FullName)  $($file.Name)")
            [void]$sha512Lines.Add("$(Get-Sha512 $file.FullName)  $($file.Name)")
        }
        [System.IO.File]::WriteAllLines($sha256File, $sha256Lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
        [System.IO.File]::WriteAllLines($sha512File, $sha512Lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))

        Write-Step 'Criando ou atualizando a release v0.3.11.48'
        $releaseNotes = Join-Path $repo 'RELEASE_NOTES_v0.3.11.48.md'
        $probe = Invoke-Native -FilePath $tools.Gh -Arguments @('release','view',$ReleaseTag,'--repo',$Repository) -AllowFailure -Quiet
        $assets = @($finalZip,$stableZip,$sourceZip,$sha256File,$sha512File)
        if ($releaseScript) { $assets += $releaseScript }
        if ($probe.ExitCode -eq 0) {
            Invoke-Native -FilePath $tools.Gh -Arguments @('release','edit',$ReleaseTag,'--repo',$Repository,'--title','TS SE Tool Community 0.3.11.48 — RC5.1H22 Final','--notes-file',$releaseNotes,'--latest') | Out-Null
            Invoke-Native -FilePath $tools.Gh -Arguments (@('release','upload',$ReleaseTag,'--repo',$Repository,'--clobber') + $assets) | Out-Null
        }
        else {
            Invoke-Native -FilePath $tools.Gh -Arguments (@('release','create',$ReleaseTag,'--repo',$Repository,'--target','main','--title','TS SE Tool Community 0.3.11.48 — RC5.1H22 Final','--notes-file',$releaseNotes,'--latest') + $assets) | Out-Null
        }

        Write-Step 'Atualizando metadados públicos e disparando Pages'
        Invoke-Native -FilePath $tools.Gh -Arguments @(
            'repo','edit',$Repository,
            '--description','TS SE Tool Community 0.3.11.48: editor de saves testado e validado no ETS2 1.60 com save 97. Projeto original de LIPtoH.',
            '--homepage','https://fabiomesquita01.github.io/ts-se-tool-community/'
        ) | Out-Null
        Invoke-Native -FilePath $tools.Gh -Arguments @('workflow','run','pages.yml','--repo',$Repository) -AllowFailure | Out-Null

        Write-Step 'Aguardando Build e GitHub Pages'
        Wait-Workflow -Gh $tools.Gh -Workflow 'build.yml' -HeadSha $headSha
        Wait-Workflow -Gh $tools.Gh -Workflow 'pages.yml' -HeadSha $headSha

        Write-Step 'Verificando release e download estável'
        Invoke-Native -FilePath $tools.Gh -Arguments @(
            'release','view',$ReleaseTag,'--repo',$Repository,
            '--json','name,tagName,url,isDraft,isPrerelease,publishedAt,assets'
        ) | Out-Null
        $stableUrl = "https://github.com/$Repository/releases/latest/download/$StableAssetName"
        try {
            $response = Invoke-WebRequest -Uri $stableUrl -Method Head -UseBasicParsing -MaximumRedirection 10 -TimeoutSec 30
            Write-Host "Download estável: HTTP $($response.StatusCode) — $stableUrl"
        }
        catch {
            throw "A release foi criada, mas o download estável não respondeu corretamente: $stableUrl | $($_.Exception.Message)"
        }

        Write-Host ''
        Write-Host 'PUBLICAÇÃO RC5.1H22 CONCLUÍDA' -ForegroundColor Green
        Write-Host "Repositório: https://github.com/$Repository"
        Write-Host "Release: https://github.com/$Repository/releases/tag/$ReleaseTag"
        Write-Host 'Página: https://fabiomesquita01.github.io/ts-se-tool-community/'
        Write-Host "Asset do atualizador: $stableUrl"
        Write-Host "SHA-256 do runtime final: $finalSha256"
        Write-Host 'A release H21 anterior foi preservada.' -ForegroundColor DarkGray
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($PublicarValidado) {
    Publish-Validated
}
else {
    Build-Candidate
}
