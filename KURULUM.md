# MedScribe.AI — Geliştirme Makinesi Kurulumu

Yeni bir Windows makinede projeyi çalıştırmak için adımları sırayla izleyin.

> **Hızlı yol:** Repo kökünde `.\kurulum.ps1` neyin kurulu olduğunu kontrol eder ve eksikleri listeler.
> `.\kurulum.ps1 -Kur` eksik Ollama'yı, `qwen2.5:14b` modelini, Whisper modelini ve `appsettings.Local.json` şablonunu kurar.
> NVIDIA sürücüsü ve Visual Studio betikle kurulmaz; aşağıdaki adımlardan elle kurulur.

## 1. Temel araçlar

1. **Visual Studio 2022** — VS Installer'da ".NET desktop development" iş yükü. .NET 8 SDK kurulu olmalı:
   ```powershell
   dotnet --list-sdks
   ```
2. **Git for Windows** — https://git-scm.com/download/win (Claude Code Git Bash'e ihtiyaç duyar).
3. **Claude Code** (isteğe bağlı) — PowerShell'de:
   ```powershell
   irm https://claude.ai/install.ps1 | iex
   ```
   Visual Studio'da *View → Terminal* açıp proje klasöründe `claude` yazın; ilk açılışta tarayıcıdan giriş yapılır.
   Proje bağlamı repodaki `CLAUDE.md` dosyasından okunur.

## 2. NVIDIA sürücüsü

- https://www.nvidia.com/drivers adresinden güncel Game Ready / Studio sürücüsü (RTX 50 serisi için 570+).
- **CUDA Toolkit gerekmez.** Whisper, sürücüyle gelen Vulkan üzerinden çalışır.
- Kontrol: `nvidia-smi`

## 3. Ollama + LLM modeli

- https://ollama.com/download/windows adresinden kurun (veya `winget install --id Ollama.Ollama -e`).
- Ollama arka planda servis olarak çalışmalı: uygulama `http://localhost:11434` adresine bağlanır
  (`MedScribe:Ollama:BaseUrl`). Tepsi simgesi yoksa `ollama serve` ile başlatın.
- Modeli indirin (~9 GB):
  ```powershell
  ollama pull qwen2.5:14b
  ```
- Kontrol: `ollama run qwen2.5:14b "merhaba"` sonrasında `ollama ps` → **100% GPU** görünmeli.
  Uygulama 16k bağlam (`NumCtx: 16384`) kullandığından model ~12 GB+ VRAM ister. Daha küçük kartta model
  kısmen CPU'ya taşar ve `ollama ps` "x% CPU/GPU" gösterir; yanıtlar belirgin şekilde yavaşlar.

## 4. Whisper ses modeli (git'e girmez, ~870 MB)

`MedScribe.AI\models\ggml-large-v3-turbo-q8_0.bin` konumuna koyun. Ya eski makineden kopyalayın ya da indirin:

https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q8_0.bin

## 5. Veritabanı bağlantısı (git'e girmez)

`MedScribe.AI\appsettings.Local.example.json` dosyasını `appsettings.Local.json` adıyla kopyalayıp bağlantı cümlesini doldurun
(veya eski makinedeki `appsettings.Local.json` dosyasını taşıyın). Makinenin MiaMed SQL sunucusuna ağ erişimi olmalı.

> ⚠️ **Şema MiaMed'e aittir.** `dotnet ef database update`, `dotnet ef migrations ...` veya `EnsureCreated`
> **asla çalıştırılmaz**; bunlar MiaMed tablolarını değiştirir. Uygulama yalnızca mevcut tablolara okur/yazar.

## 6. GPU sırası (`GpuCihazNo`)

Kodda varsayılan `MedScribe:Ses:GpuCihazNo = 1`'dir (dahili Intel GPU 0, harici NVIDIA 1 olan makineler için).
Sırayı `vulkaninfo --summary` (NVIDIA sürücüsüyle gelir) gösterir; NVIDIA kartın `GPU<n>` numarası farklıysa,
örneğin makinedeki tek GPU ise, `appsettings.Local.json` dosyasında değiştirin:

```json
{
  "ConnectionStrings": { "MiaMed": "..." },
  "MedScribe": { "Ses": { "GpuCihazNo": 0 } }
}
```

Debug modu açıkken (`MedScribe:Debug:Aktif = true`) uygulama açılışta Vulkan cihaz listesini yazar; doğru numarayı oradan görün.
Doğru GPU'da bir ifade ~1 sn'de çözülür; ~10 sn+ sürüyorsa yanlış cihaz veya CPU kullanılıyordur.
İlk çalıştırmada Vulkan shader derlemesi nedeniyle açılışta ~25 sn ısınma olur, normaldir.

## 7. Mikrofon

Windows Ayarları → Gizlilik ve güvenlik → Mikrofon → masaüstü uygulamalarının erişimine izin verin.
Mikrofon olmadan denemek için `MedScribe:Ses:Mod` değerini `"Klavye"` yapın.

## 8. Çalıştırma

Visual Studio'da `MedScribe.AI` projesini başlangıç projesi yapıp F5, veya:

```powershell
dotnet run --project MedScribe.AI
```

`appsettings.json` içinde `MedScribe:Debug:Aktif` varsayılan olarak `true` gelir: LLM'e giden bağlam (hasta geçmişi dahil)
konsola yazılır. Demo veya gerçek hasta verisiyle kullanılan makinede `appsettings.Local.json` içinde `false` yapın.
