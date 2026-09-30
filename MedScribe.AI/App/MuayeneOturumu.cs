using System.Diagnostics;
using System.Text.Json;
using MedScribe.AI.Agent;
using MedScribe.AI.Configuration;
using MedScribe.AI.Services;
using MedScribe.AI.Speech;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedScribe.AI.App;

/// <summary>
/// "Sesli Muayene Modu": dikteyi transkripte ekler, reaktif/ajan komutlarını yürütür,
/// "muayeneyi tamamla" ile transkripti LLM'e göndererek medikal JSON üretir.
/// </summary>
public sealed class MuayeneOturumu(
    ISpeechInput ses,
    HekimSorusu soru,
    KomutCozucu komutCozucu,
    TaniService taniService,
    KlinikHizmetService hizmetService,
    LlmAgentService llmAgent,
    MuayeneKayitService kayitService,
    HastaGecmisiService gecmisService,
    DebugGunlugu debug,
    IOptions<MuayeneOptions> muayeneOptions)
{
    private readonly int _varsayilanTaniAdedi = muayeneOptions.Value.VarsayilanTaniAdedi;

    public async Task<MedikalRapor?> BaslatAsync(MuayeneBaglami baglam, CancellationToken ct)
    {
        var transkript = new TranskriptTamponu();
        YardimYaz(baglam);

        try
        {
            return await DinlemeDongusuAsync(baglam, transkript, ct);
        }
        finally
        {
            ses.Duraklat();                                // menüdeyken mikrofon dinlemesin
        }
    }

    private async Task<MedikalRapor?> DinlemeDongusuAsync(MuayeneBaglami baglam, TranskriptTamponu transkript, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var ifade = await ses.DinleAsync(ct);
            if (ifade is null) return null;               // girdi akışı kapandı
            if (string.IsNullOrWhiteSpace(ifade)) continue;

            var komut = komutCozucu.Coz(ifade);
            if (komut is not DikteMetni) ses.Duraklat();   // onay/LLM sırasında söylenenler dikteye karışmasın

            switch (komut)
            {
                case DikteMetni dikte:
                    transkript.Ekle(dikte.Metin);
                    break;

                case SonCumleyiSilKomutu:
                    var silinenCumle = transkript.SonCumleyiSil();
                    if (silinenCumle is null) Konsol.Uyari("Transkript boş, silinecek cümle yok.");
                    else Konsol.Yaz(ConsoleColor.DarkYellow, $"⌫ Silindi: \"{silinenCumle}\"");
                    break;

                case SonKelimeyiSilKomutu:
                    var silinenKelime = transkript.SonKelimeyiSil();
                    if (silinenKelime is null) Konsol.Uyari("Transkript boş, silinecek kelime yok.");
                    else Konsol.Yaz(ConsoleColor.DarkYellow, $"⌫ Silindi: \"{silinenKelime}\"");
                    break;

                case TranskriptiGosterKomutu:
                    TranskriptiYaz(transkript);
                    break;

                case TaniListeleKomutu tani:
                    await TanilariListeleAsync(baglam, tani.Adet ?? _varsayilanTaniAdedi, ct);
                    break;

                case HizmetEkleKomutu hizmet:
                    foreach (var hizmetIfadesi in hizmet.HizmetIfadeleri)
                        await HizmetEkleAsync(baglam, hizmetIfadesi, ct);
                    break;

                case MuayeneyiTamamlaKomutu:
                    if (transkript.BosMu)
                    {
                        Konsol.Uyari("Transkript boş; önce muayene bulgularını dikte edin.");
                        break;
                    }
                    var rapor = await RaporOlusturAsync(baglam, transkript, ct);
                    if (rapor is not null) return rapor;
                    Konsol.Bilgi("Muayene modu devam ediyor; tekrar \"muayeneyi tamamla\" diyebilirsiniz.");
                    break;

                case MuayenedenCikKomutu:
                    if (transkript.BosMu || await soru.OnaylaAsync("Transkript kaydedilmeden muayeneden çıkılsın mı?", ct))
                        return null;
                    break;
            }
        }

        return null;
    }

    private async Task TanilariListeleAsync(MuayeneBaglami baglam, int adet, CancellationToken ct)
    {
        Konsol.Ajan($"Veritabanı ajanı: {baglam.HastaAdSoyad} için son {adet} tanı sorgulanıyor...");
        var tanilar = await taniService.SonTanilariGetirAsync(baglam.HastaId, adet, ct);

        if (tanilar.Count == 0)
        {
            Konsol.Bilgi("   Hastaya ait kayıtlı tanı bulunamadı.");
            return;
        }

        foreach (var t in tanilar)
            Konsol.Yaz(ConsoleColor.Magenta,
                $"   {t.Tarih:dd.MM.yyyy}  {t.IcdKodu,-8} {t.TaniAdi}  [{t.TaniTuru ?? "-"}]  (Başvuru {t.BasvuruId})");
    }

    private async Task HizmetEkleAsync(MuayeneBaglami baglam, string hizmetIfadesi, CancellationToken ct)
    {
        Konsol.Ajan($"Hizmet ajanı: \"{hizmetIfadesi}\" aranıyor...");
        var adaylar = await hizmetService.AdaylariBulAsync(hizmetIfadesi, ct);

        if (adaylar.Count == 0)
        {
            Konsol.Uyari($"\"{hizmetIfadesi}\" ile eşleşen aktif hizmet/tetkik bulunamadı.");
            return;
        }

        var secilen = adaylar.Count == 1 ? adaylar[0] : await AdaySecAsync(adaylar, ct);
        if (secilen is null)
        {
            Konsol.Bilgi("   Hizmet ekleme iptal edildi.");
            return;
        }

        var kayitId = await hizmetService.EkleAsync(baglam, secilen, ct);
        Konsol.Basari($"{secilen} başvuru {baglam.BasvuruId}'e eklendi. Hizmet ID: {kayitId}");
    }

    private async Task<HizmetAdayi?> AdaySecAsync(IReadOnlyList<HizmetAdayi> adaylar, CancellationToken ct)
    {
        for (var i = 0; i < adaylar.Count; i++)
            Konsol.Yaz(ConsoleColor.Magenta, $"   [{i + 1}] {adaylar[i]}");

        var no = await soru.SecAsync("   Hangisi eklensin?", adaylar.Count, ct);
        return no is null ? null : adaylar[no.Value - 1];
    }

    /// <summary>
    /// Transkripti LLM'e gönderir, sonucu gösterir ve hekim onayıyla MiaMed muayene kaydına yazar.
    /// null dönerse muayene modu devam eder (LLM hatası, yetersiz veri, onay verilmedi veya kayıt hatası).
    /// </summary>
    private async Task<MedikalRapor?> RaporOlusturAsync(MuayeneBaglami baglam, TranskriptTamponu transkript, CancellationToken ct)
    {
        Konsol.Ajan("Hasta geçmişi toplanıyor...");
        var gecmis = await GecmisiGetirAsync(baglam, ct);

        Konsol.Ajan($"Muayene tamamlanıyor; {transkript.Cumleler.Count} cümle {llmAgent.ModelAdi} modeline gönderiliyor...");

        MedikalRapor rapor;
        var sure = Stopwatch.StartNew();
        try
        {
            string hamJson;
            (rapor, hamJson) = await llmAgent.MedikalRaporOlusturAsync(transkript.BirlesikMetin(), gecmis, ct);
            debug.Yaz($"Ham model yanıtı ({sure.Elapsed.TotalSeconds:0.0} sn)", hamJson);
        }
        catch (LlmAgentException ex)
        {
            Konsol.Hata(ex.Message);
            return null;
        }

        Konsol.Baslik("MEDİKAL JSON");
        Console.WriteLine(JsonSerializer.Serialize(rapor, LlmAgentService.JsonAyarlari));

        if (rapor.Durum != "basarili")
        {
            Konsol.Uyari($"Model transkripti klinik değerlendirme için yetersiz buldu (durum: {rapor.Durum}); kayıt yapılmadı. Dikteye devam edin.");
            return null;
        }

        if (baglam.ProtokolId is null)
        {
            Konsol.Uyari("Başvuruda protokol olmadığı için muayene kaydı yazılamaz.");
            return rapor;
        }

        var tanilar = await TanilariCozAsync(rapor, gecmis, ct);
        var alanlar = MuayeneKayitService.AnamnezAlanlari(rapor.Anamnez);
        var (vitaller, reddedilenVitaller) = MuayeneKayitService.VitalDegerleri(rapor.VitalBulgular);

        foreach (var red in reddedilenVitaller)
            Konsol.Uyari($"Vital değer makul aralık dışında, yazılmayacak: {red}");

        if (alanlar.Count == 0 && vitaller.Count == 0 && tanilar.Count == 0)
        {
            Konsol.Uyari("Kaydedilecek anamnez, vital bulgu veya doğrulanmış tanı yok.");
            return null;
        }

        Konsol.Baslik($"MUAYENE KAYDI ÖNİZLEME — Muayene/Protokol ID {baglam.ProtokolId}");
        foreach (var (_, baslik, metin) in alanlar)
            Konsol.Yaz(ConsoleColor.White, $"  {baslik,-15}: {metin}");
        if (vitaller.Count > 0)
            Konsol.Yaz(ConsoleColor.White, $"  {"Vital bulgular",-15}: " + string.Join(", ", vitaller.Select(v =>
                $"{v.Baslik} {v.Ham} {v.Birim}" + (v.Yuvarlandi ? $" (→ {v.Deger} olarak yazılır)" : ""))));
        foreach (var t in tanilar)
            Konsol.Yaz(ConsoleColor.White, $"  {"Tanı",-15}: {t.Tani.IcdKodu} {t.Tani.Adi} [{(t.Kesin ? "Kesin Tanı" : "Ön Tanı")}]");
        Konsol.Bilgi("  (Aynı başlıkta/vital türünde mevcut değer varsa üzerine yazılır.)");

        if (!await soru.OnaylaAsync("Bu bilgiler MiaMed muayene kaydına yazılsın mı?", ct))
        {
            Konsol.Bilgi("Kayıt yapılmadı.");
            return null;
        }

        try
        {
            var sonuc = await kayitService.KaydetAsync(baglam, rapor, tanilar, ct);
            Konsol.Basari($"Muayene {sonuc.MuayeneId}: {sonuc.YazilanAlan} anamnez alanı, {sonuc.YazilanVital} vital bulgu yazıldı, " +
                          $"{sonuc.EklenenTani} tanı eklendi" +
                          (sonuc.MevcutTani > 0 ? $" ({sonuc.MevcutTani} tanı protokolde zaten vardı)." : "."));
            return rapor;
        }
        catch (DbUpdateException ex)
        {
            Konsol.Hata($"Muayene kaydı yazılamadı: {ex.InnerException?.Message ?? ex.Message}");
            return null;
        }
    }

    private async Task<HastaGecmisi> GecmisiGetirAsync(MuayeneBaglami baglam, CancellationToken ct)
    {
        var sure = Stopwatch.StartNew();
        var gecmis = await gecmisService.GetirAsync(baglam, ct);

        Konsol.Bilgi($"   Bağlam: {gecmis.Vizitler.Count} vizit, {gecmis.AdayTanilar.Count} aday tanı, " +
                     $"{gecmis.Ilaclar.Count} ilaç, {gecmis.AnormalLablar.Count} referans dışı lab" +
                     (gecmis.HaricTutulanTanilar.Count > 0 ? $"; {gecmis.HaricTutulanTanilar.Count} tanı hariç tutuldu" : ""));

        if (debug.Aktif)
        {
            debug.Yaz($"Hariç tutulan tanılar (MedScribe:TaniAnalizi:HaricIcdAraliklari)",
                gecmis.HaricTutulanTanilar.Count == 0
                    ? "(yok)"
                    : string.Join(Environment.NewLine, gecmis.HaricTutulanTanilar.Select(t =>
                        $"  {t.IcdKodu,-7} {t.TaniAdi} — {t.Adet} kez, son {t.SonTarih:dd.MM.yyyy}")));
            var metin = LlmAgentService.GecmisMetni(gecmis);
            debug.Yaz($"Modele giden hasta geçmişi ({metin.Length} karakter, {sure.Elapsed.TotalSeconds:0.0} sn)", metin);
        }

        return gecmis;
    }

    /// <summary>
    /// Model önerilerini MiaMed Tani sözlüğüne bağlar ve hekime tek tek onaylatır:
    ///   tani_id dolu  → yalnızca aday listesindeyse kabul edilir (uydurma ID/kod yapısal olarak elenir);
    ///   tani_id boş   → yalnızca hekimin dikte ettiği tanıda, sözlükte ada göre aranır ve hekim eşleşmeyi seçer.
    /// </summary>
    private async Task<IReadOnlyList<KaydedilecekTani>> TanilariCozAsync(MedikalRapor rapor, HastaGecmisi gecmis, CancellationToken ct)
    {
        var oneriler = rapor.OnerilenTanilar.Where(t => t.TaniId is not null || !string.IsNullOrWhiteSpace(t.TaniAdi)).ToList();
        if (oneriler.Count == 0) return [];

        Konsol.Baslik("TANI ÖNERİLERİ (hekim onayı)");
        var adaylar = gecmis.AdayTanilar.ToDictionary(t => t.TaniId);
        var secilenler = new List<KaydedilecekTani>();

        foreach (var oneri in oneriler)
        {
            var kesin = oneri.TaniTuru == "Kesin Tanı";
            var tur = kesin ? "Kesin Tanı" : "Ön Tanı";
            TaniSozlukKaydi? kayit;

            if (oneri.TaniId is { } taniId)
            {
                if (!adaylar.TryGetValue(taniId, out var aday))
                {
                    Konsol.Uyari($"Model aday listesinde olmayan bir tanı döndürdü ({taniId}, \"{oneri.TaniAdi}\") — atlandı.");
                    continue;
                }
                if (!string.Equals(oneri.IcdKodu.Trim(), aday.IcdKodu, StringComparison.OrdinalIgnoreCase))
                    debug.Yaz("Kod uyuşmazlığı", $"Model tani_id {taniId} için icd_kodu \"{oneri.IcdKodu}\" yazdı; listedeki kod {aday.IcdKodu} kullanıldı.");

                kayit = new TaniSozlukKaydi(aday.TaniId, aday.IcdKodu, aday.TaniAdi);
                Konsol.Yaz(ConsoleColor.White, $"  {kayit.IcdKodu} {kayit.Adi} [{tur}, güven {oneri.Guven:P0}]");
                Konsol.Bilgi($"    Kaynak : {(oneri.Kaynak == "dikte" ? "hekim dikte etti" : "geçmiş")}; " +
                             $"hastaya {aday.Adet} kez konmuş ({aday.AyniUzmanlikAdet} bu uzmanlıkta), son {aday.SonTarih:dd.MM.yyyy}");
                Konsol.Bilgi($"    Gerekçe: {oneri.Gerekce}");
                if (!await soru.OnaylaAsync("    Eklensin mi?", ct)) continue;
            }
            else
            {
                if (oneri.Kaynak != "dikte")
                {
                    debug.Yaz("Elenen öneri", $"\"{oneri.TaniAdi}\" hekim dikte etmedi ve aday listesinde yok (kaynak: {oneri.Kaynak}).");
                    continue;
                }

                var eslesmeler = await taniService.AdaGoreAraAsync(oneri.TaniAdi, 5, adaylar.Keys, ct);
                Konsol.Yaz(ConsoleColor.White, $"  \"{oneri.TaniAdi}\" [{tur}] — hekim dikte etti; MiaMed sözlük eşleşmeleri:");
                Konsol.Bilgi($"    Gerekçe: {oneri.Gerekce}");
                if (eslesmeler.Count == 0)
                {
                    Konsol.Uyari("    Sözlükte eşleşme bulunamadı — atlandı; tanıyı MiaMed'den ekleyin.");
                    continue;
                }

                for (var i = 0; i < eslesmeler.Count; i++)
                    Konsol.Yaz(ConsoleColor.Magenta, $"    [{i + 1}] {eslesmeler[i].IcdKodu,-7} {eslesmeler[i].Adi}" +
                                                     (adaylar.ContainsKey(eslesmeler[i].Id) ? "  ← hastaya daha önce konmuş" : ""));
                if (await soru.SecAsync("    Hangisi eklensin?", eslesmeler.Count, ct) is not { } no) continue;
                kayit = eslesmeler[no - 1];
            }

            if (secilenler.Any(s => s.Tani.Id == kayit.Id)) continue;
            secilenler.Add(new KaydedilecekTani(kayit, kesin));
        }

        return secilenler;
    }

    private static void TranskriptiYaz(TranskriptTamponu transkript)
    {
        Konsol.Baslik("TRANSKRİPT");
        if (transkript.BosMu) Konsol.Bilgi("(boş)");
        for (var i = 0; i < transkript.Cumleler.Count; i++)
            Konsol.Bilgi($"{i + 1,3}. {transkript.Cumleler[i]}");
    }

    private void YardimYaz(MuayeneBaglami baglam)
    {
        Konsol.Baslik($"SESLİ MUAYENE MODU — {baglam.HastaAdSoyad}, Başvuru {baglam.BasvuruId}");
        Konsol.Bilgi(ses is WhisperSpeechInput
            ? "Konuşun (cümle sonunda kısa bir duraklama ifadeyi bitirir) ya da yazıp Enter'a basın. Komutlar:"
            : "Mikrofon bağlı değil: söyleyeceğiniz her cümleyi yazıp Enter'a basın. Komutlar:");
        Konsol.Bilgi("  • \"son cümleyi sil\" / \"son kısmı sil\" / \"son kelimeyi sil\"");
        Konsol.Bilgi("  • \"hastanın aldığı son 10 tanıyı listele\"");
        Konsol.Bilgi("  • \"hemogram tetkiki ekle\", \"biyokimya hizmeti ekle\"");
        Konsol.Bilgi("  • \"transkripti göster\", \"muayeneyi tamamla\", \"muayeneden çık\"");
        Konsol.Bilgi("  • Onay sorularına \"evet\" / \"hayır\", seçimlere \"iki\" / \"ikinci\" diye sesli cevap verebilirsiniz");
        Konsol.Bilgi("  • Alanı zorlamak için başlıkla başlayın: \"fizik muayene: ...\", \"soygeçmiş: ...\", \"özgeçmiş: ...\", \"tedavi planı: ...\"");
    }
}
