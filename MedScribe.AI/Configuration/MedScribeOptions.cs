namespace MedScribe.AI.Configuration;

public sealed class OllamaOptions
{
    public const string Section = "MedScribe:Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:14b";
    public int TimeoutSaniye { get; set; } = 300;
    public double Temperature { get; set; } = 0.1;
    public int NumCtx { get; set; } = 8192;
}

public sealed class MuayeneOptions
{
    public const string Section = "MedScribe:Muayene";

    public int SonBasvuruAdedi { get; set; } = 10;
    public int VarsayilanTaniAdedi { get; set; } = 10;
}

public sealed class TaniAnaliziOptions
{
    public const string Section = "MedScribe:TaniAnalizi";

    /// <summary>Modele bağlam olarak verilecek, aynı uzmanlıktaki son vizit sayısı.</summary>
    public int GecmisVizitAdedi { get; set; } = 3;

    /// <summary>Hastanın geçmiş tanılarından modele sunulacak en fazla aday sayısı.</summary>
    public int AdayTaniAdedi { get; set; } = 15;

    public int IlacGunSayisi { get; set; } = 180;
    public int LabGunSayisi { get; set; } = 90;

    /// <summary>Vizit metinleri modele bu uzunlukta kırpılarak verilir.</summary>
    public int VizitMetniMaksimumKarakter { get; set; } = 600;

    /// <summary>
    /// Aday listesinden çıkarılacak ICD aralıkları ("Z00-Z13") veya tekil 3 karakterli kodlar ("R69").
    /// Varsayılan: Z00–Z13 — muayene/kontrol/tarama gibi hastalık bildirmeyen temas kodları.
    /// Neden: rutin vizitlerde sık girildikleri için sıklığa göre sıralanan aday listesinin başına geçip
    /// gerçek hastalıkları aşağı itiyorlardı (Hasta 28018'de Z00.8, 68 kez ile en sık 2. tanıydı).
    /// Klinik değeri olan Z kodları (Z99.2 diyaliz, Z94.0 nakil, Z79 uzun süreli ilaç) bu aralığın dışındadır.
    /// </summary>
    public List<string> HaricIcdAraliklari { get; set; } = ["Z00-Z13"];

    /// <summary>
    /// false (varsayılan): HaricIcdAraliklari uygulanır. true: filtre kapatılır, bu kodlar da analize dahil edilir.
    /// Listeyi silmeden filtreyi kapatabilmek için ayrı bir anahtar; elemenin öneri kalitesine etkisini
    /// karşılaştırmak veya bu kodlara ihtiyaç duyan bir birim için kullanılır.
    /// </summary>
    public bool HaricKodlariDahilEt { get; set; }

    /// <summary>
    /// Sözlük aramasından önce tanı adında yapılacak ifade değişimleri
    /// (örn. "idrar yolu" → "üriner sistem"); MiaMed Tani.Adi tıbbi terimleri kullanır.
    /// </summary>
    public Dictionary<string, string> EsAnlamlilar { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SesOptions
{
    public const string Section = "MedScribe:Ses";

    /// <summary>"Klavye": her satır bir ifade (mikrofon simülasyonu). "Mikrofon": Whisper ile gerçek ses; klavye de çalışır.</summary>
    public string Mod { get; set; } = "Klavye";

    public bool MikrofonModu => Mod.Equals("Mikrofon", StringComparison.OrdinalIgnoreCase);

    /// <summary>Göreli yol uygulama klasörüne (bin) göre çözülür.</summary>
    public string ModelYolu { get; set; } = "models/ggml-large-v3-turbo-q8_0.bin";

    public string Dil { get; set; } = "tr";

    /// <summary>
    /// Vulkan GPU sırası (debug modunda listelenir). Bu makinede 0 = Intel Iris Xe (dahili, ~10 sn/ifade),
    /// 1 = RTX 5080 (~0,9 sn/ifade). Yanlış cihaz seçilirse Whisper çalışır ama çok yavaş olur.
    /// </summary>
    public int GpuCihazNo { get; set; } = 1;

    /// <summary>NAudio WaveIn cihaz numarası; debug modunda başlangıçta cihaz listesi yazılır.</summary>
    public int MikrofonNo { get; set; }

    /// <summary>
    /// Konuşma sayılacak en düşük RMS (0–1). Gerçek eşik = max(bu değer, ortam gürültüsü × GurultuCarpani);
    /// böylece sessiz odada da gürültülü poliklinikte de çalışır.
    /// </summary>
    public double EnerjiEsigi { get; set; } = 0.012;
    public double GurultuCarpani { get; set; } = 3.0;

    /// <summary>Bu kadar sessizlik ifadeyi kapatır.</summary>
    public int SessizlikMs { get; set; } = 900;

    /// <summary>Konuşma başlamadan önceki bu kadar ses de ifadeye eklenir (ilk hece kesilmesin).</summary>
    public int OnKayitMs { get; set; } = 300;

    /// <summary>Bundan kısa sesler (öksürük, tık) atılır.</summary>
    public int MinKonusmaMs { get; set; } = 400;

    /// <summary>Whisper 30 sn pencereyle çalışır; uzun konuşma bu sürede bölünür.</summary>
    public int MaxIfadeSaniye { get; set; } = 25;

    /// <summary>Whisper'a verilen ön bilgi: alan terimlerini ve komut ifadelerini doğru yazmasına yardım eder.</summary>
    public string Prompt { get; set; } = "";
}

public sealed class DebugOptions
{
    public const string Section = "MedScribe:Debug";

    /// <summary>Modele giden bağlamı, elenen tanıları ve ham yanıtı konsolda gösterir.</summary>
    public bool Aktif { get; set; }

    /// <summary>Debug kayıtlarını ayrıca LogKlasoru altına günlük dosyaya yazar. Dosyalar hasta verisi içerir.</summary>
    public bool DosyayaYaz { get; set; }

    /// <summary>Göreli yol uygulama klasörüne (bin) göre çözülür.</summary>
    public string LogKlasoru { get; set; } = "logs";
}

public sealed class KlinikHizmetOptions
{
    public const string Section = "MedScribe:KlinikHizmet";

    /// <summary>HizmetDurumTur.ID — 2 = "Beklemede".</summary>
    public int HizmetDurumTurId { get; set; } = 2;

    public decimal Miktar { get; set; } = 1;

    /// <summary>Boş bırakılırsa seçilen protokolün Doktor_ID değeri kullanılır.</summary>
    public int? IsteyenDoktorId { get; set; }

    public int? KaydedenKullaniciId { get; set; }

    /// <summary>Sesli ifade → Tetkik adında aranacak metin (örn. "tam kan sayımı" → "hemogram").</summary>
    public Dictionary<string, string> EsAnlamlilar { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
