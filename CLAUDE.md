# MedScribe.AI

Hekim muayenesi için sesli medikal asistan: .NET 8 konsol uygulaması. Konuşma Whisper ile yazıya çevrilir,
Ollama (`qwen2.5:14b`) ile anamnez/tanı/tetkik çıkarılır, sonuç MiaMed HBYS veritabanına yazılır.
Kurulum adımları: `KURULUM.md` (ortam kontrolü: `.\kurulum.ps1`).

## Komutlar ve yapı

- Derleme: `dotnet build`; çalıştırma: `dotnet run --project MedScribe.AI`. Test projesi yok.
- `Agent/` sesli komut/cevap çözümleme ve Türkçe metin normalizasyonu · `App/` konsol arayüzü, muayene oturumu, hekim soruları ·
  `Services/` LLM (Ollama), hasta geçmişi, tanı, tetkik, muayene kaydı · `Speech/` Whisper + NAudio ·
  `Data/` + `Domain/` MiaMed EF eşlemeleri · `Configuration/` `MedScribe:*` ayarları.
- Ollama: `NumCtx 16384`, `Temperature 0.1`; bağlam taşması/kesilme şüphesinde ilk bakılacak yer `MedScribe:Ollama`.

## Veritabanı (MiaMed_ULU)

- Şema MiaMed'e aittir. Bu bir **test DB**'dir, insert yapılabilir; ama **asla EF migration veya `EnsureCreated` çalıştırma**
  (MiaMed'in tablolarını değiştirir).
- DB uyumluluk seviyesi 120 → `UseCompatibilityLevel(120)` gerekli.
- Bağlantı cümlesi git'e girmeyen `MedScribe.AI/appsettings.Local.json` içinde; asla commit etme (şablon: `appsettings.Local.example.json`).
- Büyük tablolar (`LaboratuarSonuc` ~430M, `Hizmet` ~420M satır): sorgularda her zaman `Hasta_ID` ile filtrele.
- Entity eşlemeleri: Basvuru → `HastaKart`, Hizmet → `Tetkik`, BasvuruHizmet → `Hizmet`, Tani → ICD sözlüğü (`SkrsKodu` üzerinden).

## Terminoloji

- Başvuru No = `HastaKart.ID`; Hasta No / dosya no = `Hasta.ID`.
- Poliklinik varyantları (POL-1, POL-2, RETİNA POL.) aynı `Birim.UzmanlikDal_ID`'yi paylaşır.
- Zengin geçmişli test hastası: Başvuru 23676696 (Hasta 28018), protokol [2] Nefroloji RUTİN POL-1.

## Akışlar

- **Muayeneyi tamamla** (hekim onayından sonra): anamnez → `Muayene_MuayeneSonuc` (`Muayene.ID = Protokol.ID`),
  tanılar → `HastaTani` (`TaniTip_ID = 1`). `Muayene.Tamamlandi` şimdilik bilinçli olarak yazılmıyor; gündeme getirme.
- **Tetkik ekleme** doğrudan `dbo.Hizmet`'e yazılır (simülasyon bayrağı kullanıcı isteğiyle kaldırıldı).
- **Tanı önerisi**: hasta geçmişinden kapalı aday listesi (`HastaGecmisiService`: aynı UzmanlikDal vizitleri, `HastaTani`,
  `ReceteIlac2 → Ilac`, `LaboratuarSonuc`). Z00–Z13 `TaniAnalizi:HaricIcdAraliklari` ile elenir.
- **Vitaller** → `Muayene_MuayeneGenelBilgiler`. ID 6 = sistolik, 8 = diastolik (isimleri ters olsa da kullanıcı teyit etti);
  VKİ (7) yazılmıyor. `Deger` şu an decimal(18,0) → yuvarlanıyor; kolon 18,2 olunca `MuayeneGenelBilgi.DegerOndalikBasamak = 2` yapılacak.
- Sesli onaylar: `HekimSorusu` + `CevapCozucu`. `MedScribeApp` menü seçimleri bilinçli olarak klavyeyle (mikrofon orada kapalı).
- `Debug:Aktif` LLM'e giden bağlamı ve elenen tanıları konsolda gösterir.

## Ses (Whisper)

- Whisper.net 1.9.1 **Vulkan** runtime (CUDA Toolkit gerekmez) + NAudio 2.2.1; model `models/ggml-large-v3-turbo-q8_0.bin` (git'e girmez).
- `Ses:GpuCihazNo` Vulkan cihaz sırasıdır (varsayılan 1); makineye göre `appsettings.Local.json` içinde ayarlanır.
- İlk Vulkan çalışması shader derler (~25 sn); `HazirlaAsync` bunu açılışta ısınma olarak yapar.

## Açık konular

- Test DB'de `Hasta.Adi` maskeli ("N*****") ama serbest metin anamnezde gerçek isimler var; isim maskeleme burada doğrulanamıyor.
- Debug logları hasta verisi içerebilir (`logs/` git'e girmez).
