using System.Globalization;

namespace NotchUser;

/// <summary>Texts in English, Portuguese and Spanish.</summary>
public static class Strings
{
    public static readonly string[] Languages = { "system", "en", "pt", "es" };

    private static readonly Dictionary<string, Dictionary<string, string>> All = new()
    {
        ["nothingPlaying"] = new() { ["en"] = "Nothing playing", ["pt"] = "Nada tocando", ["es"] = "No suena nada" },
        ["dragHint"] = new() { ["en"] = "Drag files, links or text here", ["pt"] = "Arraste arquivos, links ou texto até aqui", ["es"] = "Arrastra archivos, enlaces o texto aquí" },
        ["dropHere"] = new() { ["en"] = "Drop here to keep it", ["pt"] = "Solte aqui pra guardar", ["es"] = "Suelta aquí para guardarlo" },
        ["releaseNow"] = new() { ["en"] = "Drop it", ["pt"] = "Pode soltar", ["es"] = "Suéltalo" },
        ["clearShelf"] = new() { ["en"] = "Clear shelf", ["pt"] = "Limpar prateleira", ["es"] = "Vaciar estante" },
        ["removeSelected"] = new() { ["en"] = "Remove the selected", ["pt"] = "Tirar os selecionados", ["es"] = "Quitar los seleccionados" },
        ["open"] = new() { ["en"] = "Open", ["pt"] = "Abrir", ["es"] = "Abrir" },
        ["copyText"] = new() { ["en"] = "Copy text", ["pt"] = "Copiar texto", ["es"] = "Copiar texto" },
        ["showInExplorer"] = new() { ["en"] = "Show in File Explorer", ["pt"] = "Mostrar no Explorador", ["es"] = "Mostrar en el Explorador" },
        ["copy"] = new() { ["en"] = "Copy", ["pt"] = "Copiar", ["es"] = "Copiar" },
        ["removeFromShelf"] = new() { ["en"] = "Remove from shelf", ["pt"] = "Tirar da prateleira", ["es"] = "Quitar del estante" },
        ["language"] = new() { ["en"] = "Language", ["pt"] = "Idioma", ["es"] = "Idioma" },
        ["langSystem"] = new() { ["en"] = "System", ["pt"] = "Sistema", ["es"] = "Sistema" },
        ["peekOnTrack"] = new() { ["en"] = "Show when the song changes", ["pt"] = "Mostrar ao trocar de música", ["es"] = "Mostrar al cambiar de canción" },
        ["launchAtLogin"] = new() { ["en"] = "Start with Windows", ["pt"] = "Abrir ao iniciar o Windows", ["es"] = "Abrir al iniciar Windows" },
        ["quit"] = new() { ["en"] = "Quit NotchUser", ["pt"] = "Sair do NotchUser", ["es"] = "Salir de NotchUser" },
        ["text"] = new() { ["en"] = "Text", ["pt"] = "Texto", ["es"] = "Texto" },
    };

    /// <summary>The language actually used: the chosen one, or Windows' if we have it.</summary>
    public static string Resolved
    {
        get
        {
            var chosen = Settings.Current.Language;
            if (chosen != "system") return chosen;
            var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return code is "pt" or "es" or "en" ? code : "en";
        }
    }

    public static string L(string key) =>
        All.TryGetValue(key, out var t) ? (t.TryGetValue(Resolved, out var s) ? s : t["en"]) : key;

    public static string LanguageName(string code) => code switch
    {
        "en" => "English",
        "pt" => "Português",
        "es" => "Español",
        _ => L("langSystem"),
    };
}
