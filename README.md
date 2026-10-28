<p align="center">
  <img src="https://raw.githubusercontent.com/Magnumja/NotchUser/main/Assets/logo.png" width="320" alt="NotchUser">
</p>

<p align="center">
  NotchUser para Windows: sua música e uma prateleira de arquivos numa pílula no topo da tela.<br>
  Versão de testes.
</p>

---

> Versão para Mac: [Magnumja/NotchUser](https://github.com/Magnumja/NotchUser) · Linux (GNOME): [Magnumja/NotchUser-Linux](https://github.com/Magnumja/NotchUser-Linux)

## O que ele faz

- **Pílula preta no topo da tela**, no meio, como um notch: capa, nome da música e barrinhas animadas na cor do álbum. Sem nada tocando, fica só um traço discreto.
- **Passe o mouse para abrir**: capa grande, artista, barra de progresso (clique para pular), voltar/play/avançar e os apps que estão tocando, para trocar entre eles.
- **Funciona com tudo que aparece nos controles de mídia do Windows**: Spotify, Chrome, Edge, Firefox, Media Player…
- **Prateleira**: arraste arquivos, links ou texto até a pílula para guardar; depois arraste para onde quiser. Clique para selecionar vários; dois cliques abrem; botão direito tem Abrir, Mostrar no Explorador, Copiar e Tirar.
- **"Pulinho"** quando a música troca, e o painel abre rapidinho para avisar.
- **Complemento: copiar texto da tela.** Aperte **Ctrl+Shift+2**, arraste sobre qualquer pedaço da tela (vídeo, imagem, PDF travado, slide numa chamada) e o texto vai direto para a área de transferência. Usa o reconhecimento de texto do próprio Windows, sem internet; a imagem não é salva.
  > Vem desligado: ligue na apresentação ou nos **Ajustes**.
- **Apresentação** na primeira vez que abre: o que o app faz, idioma e complementos.
- **Ajustes** (botão direito na pílula → **Ajustes…**): idioma (Sistema, Português, English, Español), complementos, mostrar ao trocar de música e abrir ao iniciar o Windows.
- **Animações como no Mac**: o painel abre com a mesma mola do Mac (cresce com um leve passo além e assenta), o conteúdo entra crescendo do topo, botões afundam ao clicar, itens novos pulam na prateleira e o título desliza ao trocar de música. Tudo a 60 fps.
- **Leve**: a cor do álbum, os players e a prateleira só são refeitos quando algo muda, e a capa é guardada pequena na memória.

Requisitos: Windows 10 (2004) ou Windows 11.

## Rodar

O jeito mais fácil: baixe o `NotchUser.exe` na página de [Releases](../../releases) e abra. Não precisa instalar nada.

### A partir do código

1. Instale o **.NET 8 SDK**: https://dotnet.microsoft.com/download/dotnet/8.0
2. No terminal (PowerShell), dentro desta pasta:

```powershell
dotnet run
```

Para gerar um `NotchUser.exe` único, que roda sem instalar o .NET:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

O executável fica em `dist\NotchUser.exe`. Na primeira vez o Windows pode mostrar "O Windows protegeu o computador": clique em **Mais informações → Executar assim mesmo** (o app não é assinado por um certificado pago).

## Arquivos

- `MainWindow.xaml` / `.cs`: a pílula e o painel.
- `MediaService.cs`: lê o que está tocando pelos controles de mídia do próprio Windows.
- `Shelf.cs`: a prateleira (salva em `%AppData%\NotchUser\shelf.json`).
- `Settings.cs`, `Strings.cs`: ajustes e textos em português, inglês e espanhol.
- `OnboardingWindow.cs`, `SettingsWindow.cs`, `Ui.cs`: a apresentação, os Ajustes e as peças visuais deles (estilos em `App.xaml`).
- `ScreenText.cs`: copiar texto da tela (captura, seleção e reconhecimento).

## Licença

[MIT](LICENSE).
