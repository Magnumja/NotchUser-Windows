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
        ["removeSelected"] = new() { ["en"] = "Remove the {0} selected", ["pt"] = "Tirar os {0} selecionados", ["es"] = "Quitar los {0} seleccionados" },
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
        ["addonsSub"] = new() { ["en"] = "Optional extras. Turn them on now or anytime in Settings or the notch menu.", ["pt"] = "Extras opcionais. Ligue agora ou quando quiser, nos Ajustes ou no menu do notch.", ["es"] = "Extras opcionales. Actívalos ahora o cuando quieras en Ajustes o en el menú del notch." },
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
        ["alreadyOnShelf"] = new() { ["en"] = "{0} already on the shelf", ["pt"] = "{0} já na prateleira", ["es"] = "{0} ya en el estante" },
        ["openApp"] = new() { ["en"] = "Open {0}", ["pt"] = "Abrir {0}", ["es"] = "Abrir {0}" },
        ["showApp"] = new() { ["en"] = "Show {0}", ["pt"] = "Mostrar {0}", ["es"] = "Mostrar {0}" },
        ["openN"] = new() { ["en"] = "Open {0} items", ["pt"] = "Abrir {0} itens", ["es"] = "Abrir {0} elementos" },
        ["copyN"] = new() { ["en"] = "Copy {0} items", ["pt"] = "Copiar {0} itens", ["es"] = "Copiar {0} elementos" },
        ["removeN"] = new() { ["en"] = "Remove {0} from shelf", ["pt"] = "Tirar {0} da prateleira", ["es"] = "Quitar {0} del estante" },
        ["selectAll"] = new() { ["en"] = "Select all", ["pt"] = "Selecionar tudo", ["es"] = "Seleccionar todo" },
        ["deselect"] = new() { ["en"] = "Deselect", ["pt"] = "Desmarcar", ["es"] = "Deseleccionar" },
        ["general"] = new() { ["en"] = "General", ["pt"] = "Geral", ["es"] = "General" },
        ["style.basic"] = new() { ["en"] = "Basic", ["pt"] = "Básico", ["es"] = "Básico" },
        ["style.elevated"] = new() { ["en"] = "Elevated", ["pt"] = "Elevado", ["es"] = "Elevado" },
        ["style.glass"] = new() { ["en"] = "Glass", ["pt"] = "Vidro", ["es"] = "Cristal" },
        ["styleHint"] = new() { ["en"] = "Elevated: depth, a glow under the notch and a cover that tilts toward the mouse. Glass: a see-through panel with waves that follow the music.", ["pt"] = "Elevado: profundidade, um brilho sob o notch e a capa que inclina na direção do mouse. Vidro: painel translúcido com ondas que seguem a música.", ["es"] = "Elevado: profundidad, un brillo bajo el notch y una portada que se inclina hacia el ratón. Cristal: panel translúcido con ondas que siguen la música." },
        ["tip"] = new() { ["en"] = "Hover the notch to open it. Right-click it for options and Settings.", ["pt"] = "Passe o mouse no notch pra abrir. Clique com o botão direito pra ver opções e Ajustes.", ["es"] = "Pasa el ratón por el notch para abrirlo. Haz clic derecho para ver opciones y Ajustes." },
        ["close"] = new() { ["en"] = "Close", ["pt"] = "Fechar", ["es"] = "Cerrar" },
        // Walkthrough
        ["welcomeTitle"] = new() { ["en"] = "Welcome to NotchUser", ["pt"] = "Bem-vindo ao NotchUser", ["es"] = "Bienvenido a NotchUser" },
        ["welcomeSub"] = new() { ["en"] = "Your music, a file shelf and text from the screen, right in the notch.", ["pt"] = "Sua música, uma prateleira de arquivos e texto da tela, direto no notch.", ["es"] = "Tu música, un estante de archivos y texto de la pantalla, directo en el notch." },
        ["featuresTitle"] = new() { ["en"] = "What it does", ["pt"] = "O que ele faz", ["es"] = "Qué hace" },
        ["featMusic"] = new() { ["en"] = "Music at the top", ["pt"] = "Música no topo", ["es"] = "Música arriba" },
        ["featMusicDesc"] = new() { ["en"] = "Hover the notch: cover, progress and controls for Spotify, the browser and more.", ["pt"] = "Passe o mouse no notch: capa, progresso e controles do Spotify, do navegador e outros.", ["es"] = "Pasa el ratón por el notch: portada, progreso y controles de Spotify, el navegador y más." },
        ["featShelf"] = new() { ["en"] = "Shelf", ["pt"] = "Prateleira", ["es"] = "Estante" },
        ["featShelfDesc"] = new() { ["en"] = "Drag files, links or text to the notch and drop them anywhere later.", ["pt"] = "Arraste arquivos, links ou texto até o notch e solte onde quiser depois.", ["es"] = "Arrastra archivos, enlaces o texto al notch y suéltalos donde quieras después." },
        ["readyTitle"] = new() { ["en"] = "You're all set!", ["pt"] = "Tudo pronto!", ["es"] = "¡Todo listo!" },
        ["tipHover"] = new() { ["en"] = "Hover the notch at the top to open it", ["pt"] = "Passe o mouse no notch, no topo da tela, pra abrir", ["es"] = "Pasa el ratón por el notch, arriba, para abrirlo" },
        ["tipMenu"] = new() { ["en"] = "Right-click the notch for options and Settings", ["pt"] = "Botão direito no notch: opções e Ajustes", ["es"] = "Clic derecho en el notch: opciones y Ajustes" },
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
