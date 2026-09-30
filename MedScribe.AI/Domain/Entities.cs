namespace MedScribe.AI.Domain;

// Mantıksal model ↔ MiaMed fiziksel tablo eşlemesi (kolon eşlemeleri MedScribeDbContext içinde):
//   Hasta          → dbo.Hasta
//   Basvuru        → dbo.HastaKart   (Başvuru No = HastaKart.ID)
//   Protokol       → dbo.Protokol    (başvuru altındaki poliklinik/birim muayene protokolü)
//   Birim          → dbo.Birim
//   Tani           → dbo.Tani        (ICD-10 sözlüğü, ICD kodu = SkrsKodu)
//   TaniTur        → dbo.TaniTur     (Ön Tanı, Kesin Tanı ...)
//   HastaTani      → dbo.HastaTani   (hastaya konmuş geçmiş tanılar)
//   Hizmet         → dbo.Tetkik      (hizmet/tetkik tanımları)
//   BasvuruHizmet  → dbo.Hizmet      (başvuruya eklenen hizmet/tetkik emirleri)
//   Muayene        → dbo.Muayene     (Muayene.ID = Protokol.ID, 1:1)
//   MuayeneSonuc   → dbo.Muayene_MuayeneSonuc (anamnez metinleri; PK Muayene_ID + MuayeneSonucTur_ID)
//   MuayeneGenelBilgi → dbo.Muayene_MuayeneGenelBilgiler (vital bulgular; PK Muayene_ID + MuayeneGenelBilgiler_ID)
// Tablolarda burada olmayan kolonlar bilinçli olarak eşlenmemiştir.

public class Hasta
{
    public int Id { get; set; }
    public decimal? TcKimlikNo { get; set; }
    public string? Adi { get; set; }
    public string? Soyadi { get; set; }
    public DateTime? DogumTarihi { get; set; }

    public ICollection<Basvuru> Basvurular { get; set; } = new List<Basvuru>();

    public string AdSoyad => $"{Adi} {Soyadi}".Trim();

    public int? Yas(DateTime bugun) => YasHesaplayici.Hesapla(DogumTarihi, bugun);
}

public class Basvuru
{
    public int Id { get; set; }
    public int HastaId { get; set; }
    public DateTime KayitTarihi { get; set; }
    public bool? Kapali { get; set; }
    public DateTime? TaburcuTarihi { get; set; }

    public Hasta? Hasta { get; set; }
    public ICollection<Protokol> Protokoller { get; set; } = new List<Protokol>();
    public ICollection<HastaTani> Tanilar { get; set; } = new List<HastaTani>();
    public ICollection<BasvuruHizmet> Hizmetler { get; set; } = new List<BasvuruHizmet>();
}

public class Protokol
{
    public int Id { get; set; }
    public int BasvuruId { get; set; }
    public int? BirimId { get; set; }
    public int? DoktorId { get; set; }
    public int DefterProtokolNo { get; set; }
    public DateTime AcilisTarihi { get; set; }

    /// <summary>
    /// dbo.ProtokolTur: 1 Açılış, 3 Konsultasyon, 5 Lab., 8 Radyoloji, 9 Diyaliz ...
    /// Poliklinik tespiti için kullanılmaz: poliklinik protokolleri 1 veya 3'tür, 6 "Poliklinik" pratikte kullanılmıyor.
    /// </summary>
    public int? ProtokolTurId { get; set; }

    public const int KonsultasyonProtokolTurId = 3;

    public Basvuru? Basvuru { get; set; }
    public Birim? Birim { get; set; }
    public ICollection<HastaTani> Tanilar { get; set; } = new List<HastaTani>();
    public ICollection<BasvuruHizmet> Hizmetler { get; set; } = new List<BasvuruHizmet>();
}

public class Birim
{
    public int Id { get; set; }
    public string? Adi { get; set; }

    /// <summary>Aynı uzmanlığın poliklinik varyantları (POL-1, POL-2, RETİNA POL. ...) aynı UzmanlikDal'ı paylaşır.</summary>
    public int? UzmanlikDalId { get; set; }

    /// <summary>dbo.BirimTip: 1 Poliklinik, 2 Radyoloji, 3 Laboratuar, 16 Diyaliz ...</summary>
    public int? BirimTipId { get; set; }

    public const int PoliklinikBirimTipId = 1;

    public ICollection<Protokol> Protokoller { get; set; } = new List<Protokol>();
}

public class Tani
{
    public int Id { get; set; }
    public string? IcdKodu { get; set; }
    public string? Adi { get; set; }
    public bool? Aktif { get; set; }

    public ICollection<HastaTani> HastaTanilari { get; set; } = new List<HastaTani>();
}

public class TaniTur
{
    public int Id { get; set; }
    public string? Adi { get; set; }
}

public class HastaTani
{
    public int Id { get; set; }
    public int? BasvuruId { get; set; }
    public int? ProtokolId { get; set; }
    public int TaniId { get; set; }
    public int? TaniTurId { get; set; }
    public int? TaniTipId { get; set; }
    public DateTime? EklenmeTarihi { get; set; }

    public Basvuru? Basvuru { get; set; }
    public Protokol? Protokol { get; set; }
    public Tani? Tani { get; set; }
    public TaniTur? TaniTur { get; set; }
}

public class Hizmet
{
    public int Id { get; set; }
    public string? Adi { get; set; }
    public string? Kodu { get; set; }
    public bool? Aktif { get; set; }

    public ICollection<BasvuruHizmet> BasvuruHizmetleri { get; set; } = new List<BasvuruHizmet>();
}

public class BasvuruHizmet
{
    public int Id { get; set; }
    public int? BasvuruId { get; set; }
    public int? ProtokolId { get; set; }
    public int? HizmetId { get; set; }
    public int? HizmetDurumTurId { get; set; }
    public int? IsteyenDoktorId { get; set; }
    public int? KaydedenKullaniciId { get; set; }
    public decimal? Miktar { get; set; }
    public DateTime? EklemeTarihi { get; set; }
    public DateTime? KayitTarihi { get; set; }
    public string? Aciklama { get; set; }

    public Basvuru? Basvuru { get; set; }
    public Protokol? Protokol { get; set; }
    public Hizmet? Hizmet { get; set; }
}

public class Muayene
{
    public int Id { get; set; }                 // = Protokol.ID
    public int? DoktorId { get; set; }
    public bool? Tamamlandi { get; set; }
    public DateTime? KayitTarihi { get; set; }
    public DateTime? TamamlanmaTarihi { get; set; }
}

public class MuayeneSonuc
{
    public int MuayeneId { get; set; }
    public int MuayeneSonucTurId { get; set; }
    public string? Aciklama { get; set; }
}

/// <summary>dbo.MuayeneSonucTur ID değerleri. 6 Klinik Seyir ve 8 Taburcu Notu yatan hasta içindir, kullanılmıyor.</summary>
public static class MuayeneSonucTurleri
{
    public const int Hikayesi = 1;
    public const int Sikayeti = 2;
    public const int FizikiMuayene = 3;
    public const int TedaviPlani = 4;
    public const int Ozgecmis = 5;
    public const int Soygecmis = 7;
    public const int Bulgular = 9;
}

public class MuayeneGenelBilgi
{
    /// <summary>
    /// Deger kolonu şu an decimal(18,0): küsuratlı girişler SQL CAST gibi yuvarlanır (37,8 → 38).
    /// Kolon decimal(18,2)'ye güncellendiğinde yalnızca bu sabit 2 yapılır (EF eşlemesi ve yuvarlama buradan okur).
    /// </summary>
    public const int DegerOndalikBasamak = 0;

    public int MuayeneId { get; set; }
    public int MuayeneGenelBilgilerId { get; set; }
    public decimal Deger { get; set; }
}

/// <summary>
/// dbo.MuayeneGenelBilgiler ID değerleri.
/// DİKKAT: 6/8 adlarının tersine kullanılıyor: 6 = sistolik, 8 = diastolik (MiaMed ekibince teyit edildi, 30.09.2026).
/// Veride de böyle: ~1280 kayıtta "Kan Basıncı Küçük" (6) ortalaması 122, "Kan Basıncı Büyük" (8) ortalaması 74.
/// 7 VKİ son kayıtlarda hiç yok (MiaMed hesaplıyor olmalı); yazılmıyor.
/// </summary>
public static class MuayeneGenelBilgiTurleri
{
    public const int Boy = 1;
    public const int Kilo = 2;
    public const int Ates = 3;
    public const int Nabiz = 4;
    public const int Solunum = 5;
    public const int KanBasinciSistolik = 6;
    public const int KanBasinciDiastolik = 8;
}

public static class YasHesaplayici
{
    public static int? Hesapla(DateTime? dogumTarihi, DateTime bugun)
    {
        if (dogumTarihi is not { } dogum) return null;

        var yas = bugun.Year - dogum.Year;
        if (dogum.Date > bugun.Date.AddYears(-yas)) yas--;
        return yas;
    }
}
