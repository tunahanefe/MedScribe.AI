namespace MedScribe.AI.Speech;

/// <summary>Hekimin sesli girdisini metin cümleleri olarak sağlar. null = oturum kapandı (EOF).</summary>
public interface ISpeechInput
{
    Task<string?> DinleAsync(CancellationToken ct);

    /// <summary>
    /// Dinlemeyi bir sonraki DinleAsync çağrısına kadar durdurur ve bekleyen sesi atar.
    /// Onay soruları ve LLM çalışırken söylenenler dikteye karışmasın diye komutlardan önce çağrılır.
    /// </summary>
    void Duraklat() { }
}

/// <summary>Mikrofon simülasyonu: her satır, hekimin söylediği bir ifade olarak kabul edilir.</summary>
public sealed class ConsoleSpeechInput : ISpeechInput
{
    public Task<string?> DinleAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("⌨  ");
        Console.ResetColor();
        return Task.FromResult(Console.ReadLine());
    }
}
