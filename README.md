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

O visual e as animações são os mesmos da versão do Mac: mesmo formato de notch, mesmos tamanhos, mesmas molas.

- **Notch preto no topo da tela**, no meio, como o Mac mostra em telas sem notch: fechado, só a capa à esquerda e as barrinhas na cor do álbum à direita. Sem nada tocando, fica só a pílula.
- **Passe o mouse para abrir**: o notch cresce numa mola, com os cantos se arredondando, e o painel "materializa" (nos estilos Elevado e Vidro, sai de um desfoque; no Básico, cresce de 90%). Capa grande, título e artista (que deslizam quando não cabem), barra de progresso (engrossa ao passar o mouse; arraste para pular), voltar/play/avançar e os ícones dos apps que estão tocando, para trocar entre eles. Clicar na capa traz o app que está tocando para a frente.
- **Três estilos**, como no Mac (Ajustes → Aparência, ou botão direito no notch):
  - **Básico**: preto puro.
  - **Elevado**: sombras em camadas, controles em relevo, brilho na cor do álbum sob o notch, a capa que inclina na direção do mouse e o notch que sobe um pouco antes de abrir.
  - **Vidro**: painel translúcido com reflexo e ondas na cor do álbum que seguem a música.
- **Funciona com tudo que aparece nos controles de mídia do Windows**: Spotify, Chrome, Edge, Firefox, Media Player…
- **Prateleira**: arraste arquivos, links ou texto em direção ao topo; o notch abre antes de você chegar na borda, com uma área grande "Solte aqui pra guardar". Miniaturas reais dos arquivos (as mesmas do Explorador). Clique para selecionar vários (aparece o ✓), dois cliques abrem, passe o mouse para ver o × que tira um item; botão direito tem Abrir, Mostrar no Explorador, Copiar, Selecionar tudo e Tirar.
- **Volume no notch**: quando o volume muda (teclado, barra de tarefas ou outro app), o notch mostra o nível. No painel há um controle de volume (clique no alto-falante para silenciar), e **girar a roda do mouse sobre o notch** sobe e desce o volume.
- **Avisos no notch**: carregador conectado ou desconectado, bateria fraca (20% e 10%) e troca da saída de som (ex.: fone Bluetooth conectou).
- **Some em tela cheia**: jogos, vídeos e apresentações em tela cheia ficam sem o notch por cima.
- **"Pulinho"** quando a música troca. Abrir o painel a cada música vem desligado, como no Mac.
- **Complemento: copiar texto da tela.** Aperte **Ctrl+Shift+2**, arraste sobre qualquer pedaço da tela e o texto vai para a área de transferência. Usa o reconhecimento de texto do próprio Windows, sem internet; a imagem não é salva.
- **Complemento: letras sincronizadas.** A linha que está sendo cantada aparece no notch fechado e no painel, na cor do álbum. As letras vêm do [LRCLIB](https://lrclib.net) (gratuito e aberto).
  > Os complementos vêm desligados. As letras são o único recurso que usa a internet: com elas ligadas, o nome da música, o artista, o álbum e a duração são enviados ao LRCLIB.
- **Tamanho** (Ajustes → Aparência): Auto, 100%, 115%, 130% ou 150%.
- **Apresentação** na primeira vez que abre e **Ajustes** em paisagem, com barra lateral, como no Mac. Menu do botão direito escuro, com ícones e seções.
- **Ajustes e prateleira seguros**: os arquivos são gravados de forma atômica; se um deles estragar, ele é guardado como `.bak` e o app abre com o padrão.

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

- `MainWindow.xaml` / `.cs`: o notch e o painel.
- `Visuals.cs`: peças visuais portadas do Mac (formato do notch, barrinhas, ondas, texto que desliza, botões).
- `ShellImage.cs`: miniaturas de arquivos e ícones dos apps, pelo shell do Windows.
- `MediaService.cs`: lê o que está tocando pelos controles de mídia do próprio Windows.
- `Shelf.cs`: a prateleira (salva em `%AppData%\NotchUser\shelf.json`).
- `Settings.cs`, `Strings.cs`: ajustes e textos em português, inglês e espanhol.
- `OnboardingWindow.cs`, `SettingsWindow.cs`, `Ui.cs`: a apresentação, os Ajustes e as peças visuais deles (estilos em `App.xaml`).
- `ScreenText.cs`: copiar texto da tela (captura, seleção e reconhecimento).
- `Volume.cs`: volume e saída de som pelo Core Audio do Windows.
- `Lyrics.cs`: letras sincronizadas (LRCLIB) e leitura do formato LRC.
- `FullScreen.cs`, `Power.cs`: detectar tela cheia e ler a bateria.

## Licença

[MIT](LICENSE).
