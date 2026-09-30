using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedScribe.AI.Configuration;
using Microsoft.Extensions.Options;

namespace MedScribe.AI.Services;

public sealed class LlmAgentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Yerel Ollama sunucusu (RTX 5080 üzerinde qwen2.5:14b) ile muayene transkriptinden yapılandırılmış medikal JSON üretir.
/// Hasta geçmişi de yalnızca bu yerel modele gider; veri makineden çıkmaz.
/// </summary>
public sealed class LlmAgentService(HttpClient http, IOptions<OllamaOptions> options)
{
    public static readonly JsonSerializerOptions JsonAyarlari = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string SistemTalimati = """
        Sen Türkçe konuşan bir klinik dokümantasyon asistanısın. Sana bir hekimin muayene sırasında
        dikte ettiği ham transkript ve hastanın hastane kayıtlarından çıkarılmış geçmiş özeti verilecek.
        Görevin YALNIZCA aşağıdaki şemaya birebir uyan tek bir JSON nesnesi döndürmektir. Açıklama, markdown veya ek alan ekleme.

        {
          "durum": "basarili",
          "vital_bulgular": {
            "boy_cm": null, "kilo_kg": null, "vki": null, "ates": null, "nabiz": null, "solunum_sayisi": null,
            "kan_basinci_sistolik": null, "kan_basinci_diastolik": null
          },
          "engel_durumu": { "zihinsel": false, "bedensel": false },
          "anamnez": { "sikayet": "", "hikayesi": "", "ozgecmis": "", "soygecmis": "", "fiziki_muayene": "", "bulgular": "", "tedavi_plani": "" },
          "onerilen_tanilar": [
            { "tani_id": null, "icd_kodu": "", "tani_adi": "<tanı adı>", "tani_turu": "Ön Tanı", "guven": 0.0,
              "kaynak": "<dikte|gecmis>", "gerekce": "<kısa gerekçe>" }
          ]
        }

        Kurallar:
        - vital_bulgular, engel_durumu ve anamnez YALNIZCA transkriptten doldurulur. Hasta geçmişindeki bilgiyi bu alanlara kopyalama.
        - Transkriptte açıkça söylenmeyen şikayet, bulgu veya tanı yazma; tahmin yürütme.
        - Şemadaki açılı parantezli değerler yer tutucudur; asla aynen kopyalama. Önerecek tanı yoksa onerilen_tanilar boş dizi [] olur.
        - Sayısal alanlar sayı olmalı (metin değil). "120'ye 80" → sistolik 120, diastolik 80. Ateş °C. solunum_sayisi dakikadaki solunum.
        - Boy ve kilo varsa vki = kilo / (boy_m^2), bir ondalık.
        - engel_durumu yalnızca transkriptte açıkça zihinsel/bedensel engel belirtilmişse true olur.
        - anamnez alanlarını tıbbi, kısa ve düzgün Türkçe cümlelerle doldur; bilgi yoksa boş string bırak. Alanların anlamı:
          sikayet: hastanın başvuru nedeni/yakınması. hikayesi: şikayetin başlangıcı, süresi, seyri.
          ozgecmis: hastanın KENDİ geçmiş hastalıkları, ameliyatları, sürekli ilaçları, alerjileri, alışkanlıkları.
          soygecmis: AİLE öyküsü (anne, baba, kardeş, akraba hastalıkları; akraba evliliği).
          fiziki_muayene: hekimin muayenede gördüğü/yaptığı (genel durum, inspeksiyon, palpasyon, oskültasyon, sistem muayeneleri).
            Vital değerler (tansiyon, nabız, ateş, solunum, boy, kilo) buraya YAZILMAZ; yalnızca vital_bulgular'a girer.
          bulgular: laboratuvar, görüntüleme, EKG gibi tetkik sonuçları ve bunların yorumu.
          tedavi_plani: reçete, önerilen tedavi, istenen tetkikler, kontrol zamanı.
        - Köşeli parantezli etiketle başlayan satırlar hekimin belirlediği alana aittir ve YALNIZCA o alana yazılır:
          [ŞİKAYETİ]→sikayet, [HİKAYESİ]→hikayesi, [ÖZGEÇMİŞ]→ozgecmis, [SOYGEÇMİŞ]→soygecmis,
          [FİZİKİ MUAYENE]→fiziki_muayene, [BULGULAR]→bulgular, [TEDAVİ PLANI]→tedavi_plani. Etiketin kendisini metne yazma.
          Etiketli satırdaki vital değerler yine vital_bulgular'a da girer.
        - onerilen_tanilar yalnızca iki kaynaktan gelebilir, en fazla 5 tanı, güvene göre azalan sırada:
          1) kaynak "dikte": Hekim transkriptte tanıyı/ön tanıyı açıkça söyledi. Tanı ADAY TANILAR listesinde varsa
             tani_id ve icd_kodu listeden aynen alınır; yoksa tani_id null, icd_kodu "" bırakılır ve tani_adi standart Türkçe tanı adı olur.
          2) kaynak "gecmis": ADAY TANILAR listesindeki bir tanı, transkriptteki şikayet veya bulguyla doğrudan ilişkili.
             tani_id ve icd_kodu listeden AYNEN kopyalanır. Listede olmayan bir tanıyı bu kaynakla önermek yasaktır.
        - Hekimin söylediği tanıyı önce ADAY TANILAR listesinde ara; halk dili ve tıbbi terim aynı tanıdır
          (örn. "idrar yolu enfeksiyonu" = "Üriner sistem enfeksiyonu", "tansiyon/yüksek tansiyon" = "Esansiyel hipertansiyon",
          "şeker hastalığı" = "Diabetes mellitus"). Karşılığı listedeyse tani_id'yi listeden ver.
        - Semptomlardan yeni tanı türetme: hekim söylemediyse ve aday listesinde yoksa tanı önerme.
        - ICD kodunu asla kendin üretme; icd_kodu yalnızca ADAY TANILAR listesinden gelir.
        - gerekce tek cümle olur ve transkriptteki ilgili ifadeye veya geçmiş kaydına dayanır.
        - tani_turu yalnızca "Ön Tanı" veya "Kesin Tanı" olabilir. Hekim tanıyı kesin ifade etmediyse "Ön Tanı".
          guven 0 ile 1 arasında bir sayıdır.
        - Transkript klinik değerlendirme için yetersizse (örn. "test", anlamsız veya tıbbi olmayan metin) "durum": "yetersiz_veri" döndür;
          anamnez alanları boş string, vital bulgular null, onerilen_tanilar [] olur.
        """;

    private readonly OllamaOptions _options = options.Value;

    public string ModelAdi => _options.Model;

    /// <summary>Hasta geçmişini modele verilecek düz metne çevirir (debug modunda aynen gösterilir).</summary>
    public static string GecmisMetni(HastaGecmisi g)
    {
        var sb = new StringBuilder();

        sb.AppendLine("HASTA GEÇMİŞİ (aynı uzmanlıktaki son vizitler; yalnızca tanı seçimi için bağlamdır):");
        if (g.Vizitler.Count == 0) sb.AppendLine("- kayıt yok");
        foreach (var v in g.Vizitler)
        {
            sb.AppendLine($"- {v.Tarih:dd.MM.yyyy} {v.Birim}");
            if (v.Sikayet is not null) sb.AppendLine($"  Şikayeti: {v.Sikayet}");
            if (v.Hikayesi is not null) sb.AppendLine($"  Hikayesi: {v.Hikayesi}");
            if (v.TedaviPlani is not null) sb.AppendLine($"  Tedavi planı: {v.TedaviPlani}");
        }

        sb.AppendLine().AppendLine("SON İLAÇLAR (reçetelerden):");
        if (g.Ilaclar.Count == 0) sb.AppendLine("- kayıt yok");
        foreach (var i in g.Ilaclar)
            sb.AppendLine($"- {i.IlacAdi}{(string.IsNullOrEmpty(i.EtkenMadde) ? "" : $" ({i.EtkenMadde})")}, son {i.SonTarih:dd.MM.yyyy}");

        sb.AppendLine().AppendLine("REFERANS DIŞI LABORATUVAR SONUÇLARI (her testin son değeri):");
        if (g.AnormalLablar.Count == 0) sb.AppendLine("- kayıt yok");
        foreach (var l in g.AnormalLablar)
            sb.AppendLine($"- {l.Tarih:dd.MM.yyyy} {l.Test}: {l.Sonuc} {l.OlcuBirimi} ({(l.Yuksek ? "YÜKSEK" : "DÜŞÜK")}; ref {l.AltLimit:0.##}-{l.UstLimit:0.##})");

        sb.AppendLine().AppendLine("ADAY TANILAR (hastaya daha önce konmuş; tani_id | icd_kodu | tanı adı | kullanım):");
        if (g.AdayTanilar.Count == 0) sb.AppendLine("- yok");
        foreach (var t in g.AdayTanilar)
            sb.AppendLine($"- {t.TaniId} | {t.IcdKodu} | {t.TaniAdi} | {t.Adet} kez ({t.AyniUzmanlikAdet} bu uzmanlıkta), son {t.SonTarih:dd.MM.yyyy}");

        return sb.ToString();
    }

    public async Task<(MedikalRapor Rapor, string HamJson)> MedikalRaporOlusturAsync(
        string transkript, HastaGecmisi gecmis, CancellationToken ct)
    {
        var istek = new OllamaGenerateRequest(
            Model: _options.Model,
            System: SistemTalimati,
            Prompt: $"{GecmisMetni(gecmis)}\nMUAYENE TRANSKRİPTİ:\n\"\"\"\n{transkript}\n\"\"\"\n\nYalnızca JSON döndür.",
            Format: "json",
            Stream: false,
            Options: new OllamaModelOptions(_options.Temperature, _options.NumCtx));

        OllamaGenerateResponse? yanit;
        try
        {
            using var httpYanit = await http.PostAsJsonAsync("api/generate", istek, ct);
            if (!httpYanit.IsSuccessStatusCode)
            {
                var hata = await httpYanit.Content.ReadAsStringAsync(ct);
                throw new LlmAgentException($"Ollama HTTP {(int)httpYanit.StatusCode}: {hata}");
            }

            yanit = await httpYanit.Content.ReadFromJsonAsync<OllamaGenerateResponse>(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmAgentException(
                $"Ollama sunucusuna ulaşılamadı ({http.BaseAddress}). 'ollama serve' çalışıyor ve '{_options.Model}' indirilmiş mi?", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmAgentException($"Ollama {_options.TimeoutSaniye} sn içinde yanıt vermedi.", ex);
        }

        var hamJson = yanit?.Response?.Trim();
        if (string.IsNullOrEmpty(hamJson))
            throw new LlmAgentException("Ollama boş yanıt döndürdü.");

        MedikalRapor? rapor;
        try
        {
            rapor = JsonSerializer.Deserialize<MedikalRapor>(hamJson, JsonAyarlari);
        }
        catch (JsonException ex)
        {
            throw new LlmAgentException($"Model şemaya uygun JSON üretmedi:\n{hamJson}", ex);
        }

        if (rapor is null) throw new LlmAgentException("Model yanıtı çözümlenemedi.");

        rapor.VitalBulgular ??= new VitalBulgular();
        rapor.EngelDurumu ??= new EngelDurumu();
        rapor.Anamnez ??= new Anamnez();
        rapor.OnerilenTanilar ??= [];
        rapor.VitalBulgular.VkiHesapla();

        return (rapor, hamJson);
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaModelOptions Options);

    private sealed record OllamaModelOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_ctx")] int NumCtx);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("done")] bool Done);
}
