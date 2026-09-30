namespace MedScribe.AI.App;

/// <summary>Konsol çıktısı için küçük yardımcılar; iş mantığı içermez.</summary>
public static class Konsol
{
    public static void Baslik(string metin)
    {
        Console.WriteLine();
        Yaz(ConsoleColor.Cyan, $"══ {metin} {new string('═', Math.Max(0, 60 - metin.Length))}");
    }

    public static void Bilgi(string metin) => Yaz(ConsoleColor.Gray, metin);
    public static void Basari(string metin) => Yaz(ConsoleColor.Green, $"✔ {metin}");
    public static void Uyari(string metin) => Yaz(ConsoleColor.Yellow, $"⚠ {metin}");
    public static void Hata(string metin) => Yaz(ConsoleColor.Red, $"✖ {metin}");
    public static void Ajan(string metin) => Yaz(ConsoleColor.Magenta, $"🤖 {metin}");

    public static string? Sor(string soru)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{soru}: ");
        Console.ResetColor();
        return Console.ReadLine()?.Trim();
    }

    public static bool Onayla(string soru)
    {
        var cevap = Sor($"{soru} (E/H)");
        return cevap is not null && (cevap.Equals("e", StringComparison.OrdinalIgnoreCase)
                                     || cevap.Equals("evet", StringComparison.OrdinalIgnoreCase));
    }

    public static void Yaz(ConsoleColor renk, string metin)
    {
        Console.ForegroundColor = renk;
        Console.WriteLine(metin);
        Console.ResetColor();
    }
}
