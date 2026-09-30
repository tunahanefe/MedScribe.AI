using MedScribe.AI.Agent;
using MedScribe.AI.Speech;

namespace MedScribe.AI.App;

/// <summary>
/// Muayene sırasında hekime onay/seçim sorar; cevap aktif girdiden (mikrofon ya da klavye) alınır.
/// Anlaşılmayan cevapta soru tekrarlanır; <see cref="MaxDeneme"/> sonunda güvenli taraf (hayır / iptal) seçilir.
/// Boş Enter her zaman hayır / iptal demektir.
/// </summary>
public sealed class HekimSorusu(ISpeechInput ses)
{
    private const int MaxDeneme = 3;

    public async Task<bool> OnaylaAsync(string soru, CancellationToken ct)
    {
        for (var deneme = 1; deneme <= MaxDeneme; deneme++)
        {
            var cevap = await CevapAlAsync($"{soru} (evet / hayır)", ct);
            if (string.IsNullOrEmpty(cevap)) return false;

            if (CevapCozucu.EvetHayir(cevap) is { } karar) return karar;
            Konsol.Uyari($"Anlaşılmadı: \"{cevap}\" — \"evet\" ya da \"hayır\" deyin.");
        }

        Konsol.Uyari("Cevap anlaşılamadı; \"hayır\" kabul edildi.");
        return false;
    }

    /// <summary>1..adet arası seçim; null = iptal/atla.</summary>
    public async Task<int?> SecAsync(string soru, int adet, CancellationToken ct)
    {
        for (var deneme = 1; deneme <= MaxDeneme; deneme++)
        {
            var cevap = await CevapAlAsync($"{soru} (1-{adet} söyleyin/yazın; \"geç\" ya da boş = atla)", ct);
            if (string.IsNullOrEmpty(cevap)) return null;

            if (CevapCozucu.Sayi(cevap) is { } no && no >= 1 && no <= adet) return no;
            if (CevapCozucu.EvetHayir(cevap) == false) return null;
            Konsol.Uyari($"Anlaşılmadı: \"{cevap}\" — 1 ile {adet} arasında bir numara söyleyin.");
        }

        Konsol.Uyari("Cevap anlaşılamadı; atlandı.");
        return null;
    }

    private async Task<string?> CevapAlAsync(string soru, CancellationToken ct)
    {
        Konsol.Yaz(ConsoleColor.White, soru);
        try
        {
            return (await ses.DinleAsync(ct))?.Trim();
        }
        finally
        {
            ses.Duraklat();                    // cevaptan sonra (kayıt/LLM sırasında) söylenenler dikteye karışmasın
        }
    }
}
