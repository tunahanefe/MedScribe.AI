<#
.SYNOPSIS
    MedScribe.AI geliştirme ortamını kontrol eder; -Kur ile eksikleri kurar (bkz. KURULUM.md).
.DESCRIPTION
    Kontrol: .NET 8, Git, NVIDIA sürücüsü/VRAM, Vulkan GPU sırası, Ollama + qwen2.5:14b, Whisper modeli,
    appsettings.Local.json, disk alanı.
    -Kur: Ollama (winget), qwen2.5:14b (ollama pull), Whisper modeli (Hugging Face), appsettings.Local.json şablonu.
    NVIDIA sürücüsü, Visual Studio ve .NET SDK betikle kurulmaz.
.EXAMPLE
    .\kurulum.ps1
    .\kurulum.ps1 -Kur
#>
param([switch]$Kur)

$ErrorActionPreference = 'Continue'
$proje       = Join-Path $PSScriptRoot 'MedScribe.AI'
$llmModel    = 'qwen2.5:14b'
$whisperYol  = Join-Path $proje 'models\ggml-large-v3-turbo-q8_0.bin'
$whisperUrl  = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q8_0.bin'
$localJson   = Join-Path $proje 'appsettings.Local.json'
$ornekJson   = Join-Path $proje 'appsettings.Local.example.json'
$ollamaUrl   = 'http://localhost:11434'
$eksikler    = New-Object System.Collections.Generic.List[string]

function Tamam($m)  { Write-Host "  [OK]    $m" -ForegroundColor Green }
function Uyari($m)  { Write-Host "  [UYARI] $m" -ForegroundColor Yellow }
function Eksik($m)  { Write-Host "  [EKSIK] $m" -ForegroundColor Red; $script:eksikler.Add($m) }
function Baslik($m) { Write-Host "`n== $m" -ForegroundColor Cyan }
function Var($komut) { [bool](Get-Command $komut -ErrorAction SilentlyContinue) }

function OllamaYoluYenile {
    # winget kurulumundan sonra PATH bu oturumda güncellenmez.
    $ollamaKlasor = Join-Path $env:LOCALAPPDATA 'Programs\Ollama'
    if ((Test-Path $ollamaKlasor) -and ($env:Path -notlike "*$ollamaKlasor*")) { $env:Path += ";$ollamaKlasor" }
}

function OllamaCalisiyor {
    try { Invoke-RestMethod "$ollamaUrl/api/tags" -TimeoutSec 3 } catch { $null }
}

function OllamaBaslat {
    if (OllamaCalisiyor) { return $true }
    Write-Host "  Ollama servisi başlatılıyor..."
    Start-Process ollama -ArgumentList 'serve' -WindowStyle Hidden
    for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 500; if (OllamaCalisiyor) { return $true } }
    return $false
}

# ---------------------------------------------------------------- .NET / Git
Baslik '.NET ve Git'
$runtime8 = dotnet --list-runtimes 2>$null | Select-String '^Microsoft\.NETCore\.App 8\.'
if ($runtime8) { Tamam ".NET 8 runtime: $(($runtime8 | Select-Object -Last 1).ToString().Split(' ')[1])" }
else { Eksik '.NET 8 runtime yok (https://dotnet.microsoft.com/download/dotnet/8.0)' }
$sdk = dotnet --list-sdks 2>$null | Where-Object { [int]($_.Split('.')[0]) -ge 8 }
if ($sdk) { Tamam ".NET SDK: $(($sdk | Select-Object -Last 1).Split(' ')[0])" } else { Eksik '.NET 8+ SDK yok' }
if (Var git) { Tamam (git --version) } else { Eksik 'Git for Windows yok (https://git-scm.com/download/win)' }

# ---------------------------------------------------------------- NVIDIA / Vulkan
Baslik 'NVIDIA sürücüsü ve Vulkan'
if (Var nvidia-smi) {
    $gpu = (nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader,nounits | Select-Object -First 1).Split(',').Trim()
    Tamam "$($gpu[0]) — sürücü $($gpu[1]), VRAM $([math]::Round([int]$gpu[2] / 1024)) GB"
    if ([int]$gpu[2] -lt 12000) { Uyari "VRAM 12 GB altında: $llmModel 16k bağlamla kısmen CPU'ya taşabilir." }
} else { Eksik 'NVIDIA sürücüsü yok (https://www.nvidia.com/drivers) — elle kurulmalı' }

$onerilenGpu = $null
if (Var vulkaninfo) {
    $ozet = vulkaninfo --summary 2>$null
    $no = $null
    foreach ($satir in $ozet) {
        if ($satir -match '^GPU(\d+):') { $no = [int]$Matches[1] }
        elseif ($satir -match 'deviceName\s*=\s*(.+)$') {
            Write-Host "          Vulkan GPU$no = $($Matches[1].Trim())"
            if ($null -eq $onerilenGpu -and $Matches[1] -match 'NVIDIA') { $onerilenGpu = $no }
        }
    }
    if ($null -ne $onerilenGpu) {
        if ($onerilenGpu -eq 1) { Tamam 'NVIDIA Vulkan sırası 1 — varsayılan GpuCihazNo doğru' }
        else { Uyari "NVIDIA Vulkan sırası $onerilenGpu — appsettings.Local.json içinde MedScribe:Ses:GpuCihazNo = $onerilenGpu yapın" }
    } else { Uyari 'Vulkan listesinde NVIDIA cihaz yok; Whisper CPU''da çalışır (~10 sn+/ifade).' }
} else { Uyari 'vulkaninfo bulunamadı; GPU sırasını uygulamanın debug çıktısından kontrol edin.' }

# ---------------------------------------------------------------- Ollama
Baslik "Ollama ve $llmModel"
OllamaYoluYenile
if (-not (Var ollama) -and $Kur) {
    Write-Host '  Ollama kuruluyor (winget)...'
    winget install --id Ollama.Ollama -e --silent --accept-package-agreements --accept-source-agreements
    OllamaYoluYenile
}
if (Var ollama) {
    Tamam "Ollama: $((ollama --version 2>$null) -join ' ')"
    if (OllamaBaslat) {
        Tamam "Servis çalışıyor ($ollamaUrl)"
        $modeller = (OllamaCalisiyor).models.name
        if ($modeller -contains $llmModel) { Tamam "$llmModel indirilmiş" }
        elseif ($Kur) {
            Write-Host "  $llmModel indiriliyor (~9 GB)..."
            ollama pull $llmModel
            if ((OllamaCalisiyor).models.name -contains $llmModel) { Tamam "$llmModel indirildi" } else { Eksik "$llmModel indirilemedi" }
        } else { Eksik "$llmModel indirilmemiş (ollama pull $llmModel)" }
    } else { Eksik "Ollama servisi başlatılamadı ($ollamaUrl)" }
} else { Eksik 'Ollama kurulu değil (winget install --id Ollama.Ollama -e)' }

# ---------------------------------------------------------------- Whisper modeli
Baslik 'Whisper modeli'
if (-not (Test-Path $whisperYol) -and $Kur) {
    New-Item -ItemType Directory -Force (Split-Path $whisperYol) | Out-Null
    Write-Host '  Whisper modeli indiriliyor (~870 MB)...'
    curl.exe -L --fail -o "$whisperYol.part" $whisperUrl
    if ($LASTEXITCODE -eq 0) { Move-Item "$whisperYol.part" $whisperYol -Force } else { Remove-Item "$whisperYol.part" -ErrorAction SilentlyContinue }
}
if (Test-Path $whisperYol) {
    $mb = [math]::Round((Get-Item $whisperYol).Length / 1MB)
    if ($mb -lt 800) { Eksik "Whisper modeli eksik/bozuk görünüyor ($mb MB) — silip yeniden indirin" } else { Tamam "models\ggml-large-v3-turbo-q8_0.bin ($mb MB)" }
} else { Eksik "Whisper modeli yok: $whisperYol" }

# ---------------------------------------------------------------- appsettings.Local.json
Baslik 'Veritabanı ayarı'
if (-not (Test-Path $localJson) -and $Kur) {
    Copy-Item $ornekJson $localJson
    if ($null -ne $onerilenGpu -and $onerilenGpu -ne 1) {
        (Get-Content $localJson -Raw -Encoding UTF8) -replace '"GpuCihazNo": 1', "`"GpuCihazNo`": $onerilenGpu" |
            Set-Content $localJson -Encoding UTF8
    }
    Write-Host '  appsettings.Local.json şablondan oluşturuldu.'
}
if (Test-Path $localJson) {
    if ((Get-Content $localJson -Raw -Encoding UTF8) -match '<sunucu>|"MiaMed"\s*:\s*""') {
        Eksik 'appsettings.Local.json içindeki MiaMed bağlantı cümlesi doldurulmamış'
    } else { Tamam 'appsettings.Local.json mevcut, bağlantı cümlesi dolu' }
} else { Eksik 'appsettings.Local.json yok (appsettings.Local.example.json dosyasından kopyalayın)' }

# ---------------------------------------------------------------- Disk
Baslik 'Disk'
$bos = [math]::Round((Get-PSDrive ($PSScriptRoot.Substring(0, 1))).Free / 1GB, 1)
if ($bos -lt 15) { Uyari "Boş alan $bos GB — model indirmeleri için ~15 GB önerilir" } else { Tamam "Boş alan $bos GB" }

# ---------------------------------------------------------------- Özet
Write-Host ''
if ($eksikler.Count -eq 0) {
    Write-Host 'Ortam hazır. Çalıştırmak için: dotnet run --project MedScribe.AI' -ForegroundColor Green
} else {
    Write-Host "$($eksikler.Count) eksik var:" -ForegroundColor Red
    $eksikler | ForEach-Object { Write-Host "  - $_" }
    if (-not $Kur) { Write-Host "`nOtomatik kurulabilenler için: .\kurulum.ps1 -Kur" -ForegroundColor Yellow }
    exit 1
}
