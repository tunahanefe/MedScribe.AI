using MedScribe.AI.Configuration;
using MedScribe.AI.Domain;
using MedScribe.AI.Services;
using Microsoft.Extensions.Options;

namespace MedScribe.AI.App;

/// <summary>Ana klinik akış: hasta/başvuru bulma → protokol seçimi → sesli muayene.</summary>
public sealed class MedScribeApp(
    PatientService patientService,
    MuayeneOturumu muayeneOturumu,
    IOptions<MuayeneOptions> muayeneOptions,
    TimeProvider zaman)
{
    private readonly int _sonBasvuruAdedi = muayeneOptions.Value.SonBasvuruAdedi;

    public async Task CalistirAsync(CancellationToken ct)
    {
        Konsol.Baslik("MedScribeAI — Sesli Medikal Asistan");

        while (!ct.IsCancellationRequested)
        {
            Console.WriteLine();
            Konsol.Bilgi("[1] T.C. Kimlik No / Hasta No ile hasta sorgula");
            Konsol.Bilgi("[2] Başvuru No ile doğrudan muayeneye git");
            Konsol.Bilgi("[Q] Çıkış");

            var secim = Konsol.Sor("Seçim");
            if (secim is null || secim.Equals("q", StringComparison.OrdinalIgnoreCase)) return;

            var basvuruId = secim switch
            {
                "1" => await HastaUzerindenBasvuruSecAsync(ct),
                "2" => SayiSor("Başvuru No"),
                _ => null,
            };
            if (basvuruId is null) continue;

            var baglam = await MuayeneBaglamiOlusturAsync(basvuruId.Value, ct);
            if (baglam is null) continue;

            var rapor = await muayeneOturumu.BaslatAsync(baglam, ct);
            if (rapor is not null) Konsol.Basari($"Başvuru {baglam.BasvuruId} muayenesi tamamlandı.");
        }
    }

    private async Task<int?> HastaUzerindenBasvuruSecAsync(CancellationToken ct)
    {
        var giris = Konsol.Sor("T.C. Kimlik No veya Hasta No");
        if (string.IsNullOrEmpty(giris)) return null;

        var hasta = await patientService.HastaBulAsync(giris, ct);
        if (hasta is null)
        {
            Konsol.Hata("Hasta bulunamadı.");
            return null;
        }

        HastaYaz(hasta);

        var basvurular = await patientService.SonBasvurulariGetirAsync(hasta.Id, _sonBasvuruAdedi, ct);
        if (basvurular.Count == 0)
        {
            Konsol.Uyari("Hastaya ait başvuru bulunamadı.");
            return null;
        }

        Konsol.Baslik($"SON {basvurular.Count} BAŞVURU");
        foreach (var b in basvurular)
        {
            var durum = b.Kapali ? "Kapalı" : "Açık  ";
            var birimler = string.Join(", ", b.Birimler.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
            Konsol.Yaz(b.Kapali ? ConsoleColor.DarkGray : ConsoleColor.White,
                $"  {b.Id,10}  {b.KayitTarihi:dd.MM.yyyy HH:mm}  {durum}  {birimler}");
        }

        var basvuruId = SayiSor("Muayene edilecek Başvuru ID");
        if (basvuruId is null) return null;

        if (basvurular.All(b => b.Id != basvuruId))
        {
            Konsol.Hata("Seçilen ID listede yok.");
            return null;
        }

        return basvuruId;
    }

    private async Task<MuayeneBaglami?> MuayeneBaglamiOlusturAsync(int basvuruId, CancellationToken ct)
    {
        var detay = await patientService.BasvuruDetayGetirAsync(basvuruId, ct);
        if (detay is null)
        {
            Konsol.Hata($"{basvuruId} numaralı başvuru bulunamadı.");
            return null;
        }

        var adSoyad = $"{detay.HastaAdi} {detay.HastaSoyadi}".Trim();
        var bugun = zaman.GetLocalNow().DateTime;
        var yas = YasHesaplayici.Hesapla(detay.DogumTarihi, bugun);
        Konsol.Basari($"Başvuru {detay.BasvuruId} — {adSoyad}, {YasMetni(yas)}, {detay.KayitTarihi:dd.MM.yyyy}" +
                      (detay.Kapali ? " (KAPALI başvuru)" : ""));

        if (detay.KayitTarihi.Date != bugun.Date)
        {
            var yeniBasvuruId = await BugunkuBasvuruyuSorAsync(detay, bugun, ct);
            if (yeniBasvuruId is null) return null;
            if (yeniBasvuruId != detay.BasvuruId) return await MuayeneBaglamiOlusturAsync(yeniBasvuruId.Value, ct);
        }

        var protokol = ProtokolSec(detay.Protokoller);
        if (protokol is null && detay.Protokoller.Count > 0) return null;
        if (protokol is null) Konsol.Uyari("Başvuruda protokol yok; hizmetler yalnızca başvuruya bağlanacak.");

        return new MuayeneBaglami(detay.HastaId, adSoyad, yas, detay.BasvuruId, protokol?.Id, protokol?.DoktorId, protokol?.BirimAdi,
            protokol?.UzmanlikDalId);
    }

    /// <summary>
    /// Varsayılan: en yeni poliklinik protokolü (Birim.BirimTip_ID = 1; ProtokolTur "Açılış" veya "Konsultasyon"). Lab/radyoloji/diyaliz protokolleri
    /// poliklinikten türeyen hizmet protokolleridir ve muayene kaydı için doğru yer değildir; poliklinik yoksa ilk protokol.
    /// </summary>
    /// <summary>
    /// Seçilen başvuru bugüne ait değilse uyarır: hastanın bugün açılmış başvurusu varsa ona geçmeyi önerir,
    /// yoksa eski başvuruya yazmak için onay ister. Dönüş: devam edilecek başvuru ID'si, null = iptal.
    /// </summary>
    private async Task<int?> BugunkuBasvuruyuSorAsync(BasvuruDetay detay, DateTime bugun, CancellationToken ct)
    {
        Konsol.Uyari($"Bu başvuru bugüne ait değil ({detay.KayitTarihi:dd.MM.yyyy HH:mm}). Muayene kaydı bu eski başvuruya yazılır.");

        var bugunkuler = await patientService.GunlukBasvurulariGetirAsync(detay.HastaId, bugun, ct);
        if (bugunkuler.Count > 0)
        {
            Konsol.Bilgi("   Hastanın bugün açılmış başvuruları:");
            foreach (var b in bugunkuler)
                Konsol.Yaz(ConsoleColor.White,
                    $"     {b.Id,10}  {b.KayitTarihi:HH:mm}  {string.Join(", ", b.Birimler.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())}");

            if (Konsol.Onayla($"   Bugünkü başvuruya ({bugunkuler[0].Id}) geçilsin mi?"))
                return bugunkuler[0].Id;
        }

        return Konsol.Onayla($"   Eski tarihli başvuru {detay.BasvuruId} ile devam edilsin mi?") ? detay.BasvuruId : null;
    }

    private static ProtokolOzet? ProtokolSec(IReadOnlyList<ProtokolOzet> protokoller)
    {
        if (protokoller.Count <= 1) return protokoller.FirstOrDefault();

        var varsayilan = Math.Max(0, protokoller.ToList().FindIndex(p => p.Poliklinik));

        Konsol.Baslik("PROTOKOLLER");
        for (var i = 0; i < protokoller.Count; i++)
        {
            var p = protokoller[i];
            Konsol.Yaz(p.Poliklinik ? ConsoleColor.White : ConsoleColor.DarkGray,
                $"  [{i + 1}] {p.AcilisTarihi:dd.MM.yyyy HH:mm}  Prot.No {p.DefterProtokolNo,-8} {p.BirimAdi}" +
                (p.Konsultasyon ? "  (konsültasyon)" : "") +
                (i == varsayilan ? "  ← varsayılan" : ""));
        }

        var secim = Konsol.Sor($"Muayene protokolü (1-{protokoller.Count}, boş = {varsayilan + 1})");
        if (string.IsNullOrEmpty(secim)) return protokoller[varsayilan];
        return int.TryParse(secim, out var no) && no >= 1 && no <= protokoller.Count ? protokoller[no - 1] : null;
    }

    private void HastaYaz(Hasta hasta)
    {
        Konsol.Baslik("HASTA");
        Konsol.Yaz(ConsoleColor.White, $"  Hasta No : {hasta.Id}");
        Konsol.Yaz(ConsoleColor.White, $"  Adı      : {hasta.Adi}");
        Konsol.Yaz(ConsoleColor.White, $"  Soyadı   : {hasta.Soyadi}");
        Konsol.Yaz(ConsoleColor.White, $"  Yaşı     : {YasMetni(hasta.Yas(zaman.GetLocalNow().DateTime))}");
    }

    private static string YasMetni(int? yas) => yas is null ? "yaş bilinmiyor" : $"{yas} yaş";

    private static int? SayiSor(string soru)
    {
        var giris = Konsol.Sor(soru);
        if (int.TryParse(giris, out var sayi)) return sayi;
        if (!string.IsNullOrEmpty(giris)) Konsol.Hata("Geçerli bir sayı girin.");
        return null;
    }
}
