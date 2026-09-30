namespace MedScribe.AI.Services;

public sealed record BasvuruOzet(int Id, DateTime KayitTarihi, bool Kapali, IReadOnlyList<string?> Birimler);

public sealed record ProtokolOzet(
    int Id, int DefterProtokolNo, DateTime AcilisTarihi, string? BirimAdi, int? DoktorId, int? UzmanlikDalId, bool Poliklinik, bool Konsultasyon);

public sealed record BasvuruDetay(
    int BasvuruId,
    DateTime KayitTarihi,
    bool Kapali,
    int HastaId,
    string? HastaAdi,
    string? HastaSoyadi,
    DateTime? DogumTarihi,
    IReadOnlyList<ProtokolOzet> Protokoller);

/// <summary>Sesli muayene oturumunun bağlı olduğu kayıt.</summary>
public sealed record MuayeneBaglami(
    int HastaId,
    string HastaAdSoyad,
    int? HastaYas,
    int BasvuruId,
    int? ProtokolId,
    int? DoktorId,
    string? BirimAdi,
    int? UzmanlikDalId);

public sealed record TaniKaydi(DateTime? Tarih, string? IcdKodu, string? TaniAdi, string? TaniTuru, int? BasvuruId);

public sealed record TaniSozlukKaydi(int Id, string IcdKodu, string? Adi);

// ── Hasta geçmişi (LLM bağlamı) ──────────────────────────────────────────────

public sealed record GecmisVizit(DateTime Tarih, string? Birim, string? Sikayet, string? Hikayesi, string? TedaviPlani);

public sealed record GecmisTani(int TaniId, string IcdKodu, string? TaniAdi, int Adet, int AyniUzmanlikAdet, DateTime? SonTarih);

public sealed record GecmisIlac(string? IlacAdi, string? EtkenMadde, int Adet, DateTime? SonTarih);

public sealed record AnormalLab(DateTime Tarih, string? Test, string Sonuc, string? OlcuBirimi, double AltLimit, double UstLimit, bool Yuksek);

public sealed record HastaGecmisi(
    IReadOnlyList<GecmisVizit> Vizitler,
    IReadOnlyList<GecmisTani> AdayTanilar,
    IReadOnlyList<GecmisTani> HaricTutulanTanilar,
    IReadOnlyList<GecmisIlac> Ilaclar,
    IReadOnlyList<AnormalLab> AnormalLablar)
{
    public static readonly HastaGecmisi Bos = new([], [], [], [], []);
}

/// <summary>Hekim onayından geçip HastaTani'ye yazılacak tanı.</summary>
public sealed record KaydedilecekTani(TaniSozlukKaydi Tani, bool Kesin);

public sealed record HizmetAdayi(int Id, string Adi, string? Kodu)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Kodu) ? Adi : $"{Adi} ({Kodu})";
}
