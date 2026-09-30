namespace MedScribe.AI.Services;

// Alan adları JSON'a snake_case olarak serileştirilir (LlmAgentService.JsonAyarlari).

public sealed class MedikalRapor
{
    public string Durum { get; set; } = "basarili";
    public VitalBulgular VitalBulgular { get; set; } = new();
    public EngelDurumu EngelDurumu { get; set; } = new();
    public Anamnez Anamnez { get; set; } = new();
    public List<OnerilenTani> OnerilenTanilar { get; set; } = [];
}

public sealed class VitalBulgular
{
    public double? BoyCm { get; set; }
    public double? KiloKg { get; set; }
    public double? Vki { get; set; }
    public double? Ates { get; set; }
    public double? Nabiz { get; set; }
    public double? SolunumSayisi { get; set; }
    public double? KanBasinciSistolik { get; set; }
    public double? KanBasinciDiastolik { get; set; }

    /// <summary>Boy ve kilo varsa VKİ'yi modelin hesabına bırakmadan deterministik olarak yeniden hesaplar.</summary>
    public void VkiHesapla()
    {
        if (BoyCm is > 0 and var boy && KiloKg is > 0 and var kilo)
        {
            var metre = boy / 100d;
            Vki = Math.Round(kilo / (metre * metre), 1);
        }
    }
}

public sealed class EngelDurumu
{
    public bool Zihinsel { get; set; }
    public bool Bedensel { get; set; }
}

public sealed class Anamnez
{
    public string Sikayet { get; set; } = "";
    public string Hikayesi { get; set; } = "";
    public string Ozgecmis { get; set; } = "";
    public string Soygecmis { get; set; } = "";
    public string FizikiMuayene { get; set; } = "";
    public string Bulgular { get; set; } = "";
    public string TedaviPlani { get; set; } = "";
}

public sealed class OnerilenTani
{
    /// <summary>Aday listesinden seçildiyse MiaMed Tani.ID; listede olmayan (hekimin dikte ettiği yeni) tanıda null.</summary>
    public int? TaniId { get; set; }
    public string IcdKodu { get; set; } = "";
    public string TaniAdi { get; set; } = "";
    public string TaniTuru { get; set; } = "";
    public double Guven { get; set; }

    /// <summary>"dikte" (hekim transkriptte söyledi) veya "gecmis" (aday listesinden, transkriptle ilişkili).</summary>
    public string Kaynak { get; set; } = "";
    public string Gerekce { get; set; } = "";
}
