using System.Globalization;

namespace NotchUser;

/// <summary>Texts in English, Portuguese and Spanish.</summary>
public static class Strings
{
    public static readonly string[] Languages = { "system", "en", "pt", "es" };

    private static readonly Dictionary<string, Dictionary<string, string>> All = new()
    {
        // Notch
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
        ["text"] = new() { ["en"] = "Text", ["pt"] = "Texto", ["es"] = "Texto" },
        // Menu and settings
        ["language"] = new() { ["en"] = "Language", ["pt"] = "Idioma", ["es"] = "Idioma" },
        ["langSystem"] = new() { ["en"] = "System", ["pt"] = "Sistema", ["es"] = "Sistema" },
        ["peekOnTrack"] = new() { ["en"] = "Show when the song changes", ["pt"] = "Mostrar ao trocar de música", ["es"] = "Mostrar al cambiar de canción" },
        ["launchAtLogin"] = new() { ["en"] = "Start with Windows", ["pt"] = "Abrir ao iniciar o Windows", ["es"] = "Abrir al iniciar Windows" },
        ["quit"] = new() { ["en"] = "Quit NotchUser", ["pt"] = "Sair do NotchUser", ["es"] = "Salir de NotchUser" },
        ["settings"] = new() { ["en"] = "Settings…", ["pt"] = "Ajustes…", ["es"] = "Ajustes…" },
        ["settingsTitle"] = new() { ["en"] = "NotchUser Settings", ["pt"] = "Ajustes do NotchUser", ["es"] = "Ajustes de NotchUser" },
        ["behavior"] = new() { ["en"] = "Behavior", ["pt"] = "Comportamento", ["es"] = "Comportamiento" },
        ["addons"] = new() { ["en"] = "Add-ons", ["pt"] = "Complementos", ["es"] = "Complementos" },
        ["addonsSub"] = new() { ["en"] = "Optional extras. Turn them on now or anytime in Settings.", ["pt"] = "Extras opcionais. Ligue agora ou quando quiser nos Ajustes.", ["es"] = "Extras opcionales. Actívalos ahora o cuando quieras en Ajustes." },
        ["addonOCR"] = new() { ["en"] = "Copy text from the screen (Ctrl+Shift+2)", ["pt"] = "Copiar texto da tela (Ctrl+Shift+2)", ["es"] = "Copiar texto de la pantalla (Ctrl+Shift+2)" },
        ["copyTextFromScreen"] = new() { ["en"] = "Copy text from screen", ["pt"] = "Copiar texto da tela", ["es"] = "Copiar texto de la pantalla" },
        ["done"] = new() { ["en"] = "Done", ["pt"] = "Pronto", ["es"] = "Listo" },
        // Text from screen
        ["selectText"] = new() { ["en"] = "Drag over the text to copy · Esc cancels", ["pt"] = "Arraste sobre o texto pra copiar · Esc cancela", ["es"] = "Arrastra sobre el texto para copiar · Esc cancela" },
        ["textCopied"] = new() { ["en"] = "Text copied", ["pt"] = "Texto copiado", ["es"] = "Texto copiado" },
        ["noTextFound"] = new() { ["en"] = "No text found", ["pt"] = "Nenhum texto encontrado", ["es"] = "No se encontró texto" },
        ["noTextHint"] = new() { ["en"] = "Try a bigger or sharper area.", ["pt"] = "Tente uma área maior ou mais nítida.", ["es"] = "Prueba con un área más grande o nítida." },
        ["shortcutTaken"] = new() { ["en"] = "Ctrl+Shift+2 is used by another app", ["pt"] = "Ctrl+Shift+2 já é usado por outro app", ["es"] = "Ctrl+Shift+2 ya lo usa otra app" },
        // Volume, notices, lyrics, appearance
        ["audioOutput"] = new() { ["en"] = "Sound output", ["pt"] = "Saída de som", ["es"] = "Salida de sonido" },
        ["charging"] = new() { ["en"] = "Charging", ["pt"] = "Carregando", ["es"] = "Cargando" },
        ["onBattery"] = new() { ["en"] = "On battery", ["pt"] = "Na bateria", ["es"] = "Con batería" },
        ["batteryLow"] = new() { ["en"] = "Battery low", ["pt"] = "Bateria fraca", ["es"] = "Batería baja" },
        ["addonLyrics"] = new() { ["en"] = "Synced lyrics in the notch", ["pt"] = "Letras sincronizadas no notch", ["es"] = "Letras sincronizadas en el notch" },
        ["lyricsHint"] = new() { ["en"] = "Lyrics come from LRCLIB: the song's name and artist are sent there.", ["pt"] = "As letras vêm do LRCLIB: o nome da música e o artista são enviados pra lá.", ["es"] = "Las letras vienen de LRCLIB: se envían allí el nombre de la canción y el artista." },
        ["volumeHud"] = new() { ["en"] = "Show the volume in the notch", ["pt"] = "Mostrar o volume no notch", ["es"] = "Mostrar el volumen en el notch" },
        ["notices"] = new() { ["en"] = "Battery and headphone notices", ["pt"] = "Avisos de bateria e fone", ["es"] = "Avisos de batería y auriculares" },
        ["appearance"] = new() { ["en"] = "Appearance", ["pt"] = "Aparência", ["es"] = "Apariencia" },
        ["size"] = new() { ["en"] = "Size", ["pt"] = "Tamanho", ["es"] = "Tamaño" },
        ["sizeAuto"] = new() { ["en"] = "Auto", ["pt"] = "Auto", ["es"] = "Auto" },
        ["compactPill"] = new() { ["en"] = "Compact pill (cover and bars only)", ["pt"] = "Pílula compacta (só capa e barrinhas)", ["es"] = "Píldora compacta (solo portada y barras)" },
        // Walkthrough
        ["welcomeTitle"] = new() { ["en"] = "Welcome to NotchUser", ["pt"] = "Bem-vindo ao NotchUser", ["es"] = "Bienvenido a NotchUser" },
        ["welcomeSub"] = new() { ["en"] = "Your music and a file shelf, in a pill at the top of the screen.", ["pt"] = "Sua música e uma prateleira de arquivos, numa pílula no topo da tela.", ["es"] = "Tu música y un estante de archivos, en una píldora arriba de la pantalla." },
        ["featuresTitle"] = new() { ["en"] = "What it does", ["pt"] = "O que ele faz", ["es"] = "Qué hace" },
        ["featMusic"] = new() { ["en"] = "Music at the top", ["pt"] = "Música no topo", ["es"] = "Música arriba" },
        ["featMusicDesc"] = new() { ["en"] = "Hover the pill: cover, progress and controls for Spotify, browsers and more.", ["pt"] = "Passe o mouse na pílula: capa, progresso e controles do Spotify, navegadores e outros.", ["es"] = "Pasa el ratón por la píldora: portada, progreso y controles de Spotify, navegadores y más." },
        ["featShelf"] = new() { ["en"] = "Shelf", ["pt"] = "Prateleira", ["es"] = "Estante" },
        ["featShelfDesc"] = new() { ["en"] = "Drag files, links or text to the pill and drop them anywhere later.", ["pt"] = "Arraste arquivos, links ou texto até a pílula e solte onde quiser depois.", ["es"] = "Arrastra archivos, enlaces o texto a la píldora y suéltalos donde quieras después." },
        ["readyTitle"] = new() { ["en"] = "You're all set!", ["pt"] = "Tudo pronto!", ["es"] = "¡Todo listo!" },
        ["tipHover"] = new() { ["en"] = "Hover the pill at the top to open it", ["pt"] = "Passe o mouse na pílula no topo pra abrir", ["es"] = "Pasa el ratón por la píldora de arriba para abrirla" },
        ["tipMenu"] = new() { ["en"] = "Right-click the pill for options and Settings", ["pt"] = "Botão direito na pílula: opções e Ajustes", ["es"] = "Clic derecho en la píldora: opciones y Ajustes" },
        ["tipOCR"] = new() { ["en"] = "Ctrl+Shift+2 copies text from anywhere on the screen", ["pt"] = "Ctrl+Shift+2 copia texto de qualquer lugar da tela", ["es"] = "Ctrl+Shift+2 copia texto de cualquier lugar de la pantalla" },
        ["next"] = new() { ["en"] = "Continue", ["pt"] = "Continuar", ["es"] = "Continuar" },
        ["back"] = new() { ["en"] = "Back", ["pt"] = "Voltar", ["es"] = "Atrás" },
        ["getStarted"] = new() { ["en"] = "Get started", ["pt"] = "Começar", ["es"] = "Empezar" },
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
