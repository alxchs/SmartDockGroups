# SmartDockGroups — Smart Dock Groups

> **Regra de manutenção — leia antes de mexer no código:** este arquivo e
> [`PROMPT.md`](PROMPT.md) descrevem o estado atual do produto. Toda mudança de
> comportamento, visual ou arquitetura precisa atualizar os dois no mesmo commit
> que muda o código. Um recurso que existe no app e não está aqui, ou uma
> decisão registrada aqui que o código já não segue, é a documentação falhando
> em fazer o que ela existe para fazer.

## O que é

Um organizador de área de trabalho para Windows, no espírito das telas do
Android: grupos de atalhos, arquivos e pastas, exibidos como pastas de app
fecháveis (mosaico 3×3 + selo de contagem) que abrem numa folha centralizada,
ou como painéis livres com ícones soltos — a critério de cada grupo.

Nome exibido: **Smart Dock Groups** — em janelas, bandeja, menu da área de
trabalho, instalador e na descrição do processo (`AssemblyTitle`/`Product`
do `.csproj` do App). Nome interno: `SmartDockGroups` — projetos,
namespaces, executável (`SmartDockGroups.App.exe`), pasta de dados
(`%AppData%\SmartDockGroups`), chaves de registro, mutex e pipe.

## Estrutura

```
SmartDockGroups.slnx
VERSION                          ← versão única do produto, ver "Versionamento"
src/
  Directory.Build.props          ← lê VERSION e aplica a src/SmartDockGroups.*
  SmartDockGroups.Core/           modelos e persistência, sem UI
  SmartDockGroups.App/            WPF: toda a interface e os serviços do Windows
tools/
  IconForge/                      gera o .ico oficial do app
tests/
  SmartDockGroups.Tests/          51 asserções xUnit (geometria de monitor, posicionamento multimonitor do diálogo de renomear, contrato do config, corte de transparência de ícones, renderização, versionamento v2 do cache, resolução unívoca de atalhos .url renomeados e descarte sob ambiguidade)
  GroupProbe/                     sonda que sobe o app e caracteriza os grupos
  baseline/                       referência da master b46789b, para comparar refatorações
docs/
  OVERVIEW.md                     este arquivo
  PROMPT.md                       o prompt único equivalente a tudo isto (histórico)
  PROMPT_GERACAO_UNICA.md         o mesmo produto como especificação do zero, em um passo
  VIABILIDADE_ATALHO_TASKBAR_GRUPO.md   análise (não implementada): atalho de grupo na barra de tarefas
```

`SmartDockGroups.Core` não depende de WPF nem de Win32: só modelos
(`Models/`) e o `ConfigurationStore` que serializa tudo em JSON. Tudo que
sabe desenhar uma janela ou chamar uma API do Windows vive em `SmartDockGroups.App`.

## Compilar

```
mkfile d      # ou mkfile r — de dentro da raiz do repo, acha o SmartDockGroups.slnx sozinho
```

O SDK do .NET já entende `.slnx` nativamente (`dotnet build`/`publish`), e o
`mkfile` reconhece a extensão — não precisa mais apontar para o `.csproj` só
para compilar. **Exceção:** `mkfile package` (gerar o instalador) ainda
precisa do caminho explícito do `.csproj` do App:

```
mkfile package src/SmartDockGroups.App/SmartDockGroups.App.csproj
```

porque é ao lado desse arquivo que o `mkfile` procura
`tools\build_installer.ps1` — ver "Instalador e distribuição" abaixo.

O `SmartDockGroups.slnx` também ganha, pelo clique direito do Explorer, os
mesmos itens "Compilar Debug/Release/Publicar" que `.sln`/`.csproj` já
tinham — registrado por `install-mkfile.ps1` (fora deste repositório, é
tooling pessoal do Alexandre). No Windows 11 esses itens ficam sob "Mostrar
mais opções", igual a qualquer outra extensão que o `mkfile` cobre.

## Instalador e distribuição

```
mkfile p src/SmartDockGroups.App/SmartDockGroups.App.csproj
```

`mkfile package` num projeto .NET olha primeiro se existe
`tools\build_installer.ps1` ao lado do `.csproj` — a mesma convenção que o
lado Flutter do `mkfile` já usa para `tools\build_apk.py`. Quando existe, ele
manda no que "empacotar" quer dizer; aqui isso é:

1. `dotnet publish` **self-contained, single-file, win-x64** — de propósito,
   não framework-dependent: o instalador precisa rodar numa máquina que pode
   não ter o runtime do .NET 10 instalado, já que o objetivo é justamente
   baixar e rodar fora desta rede.
2. Compilar `tools\SmartDockGroups.iss` com o **Inno Setup** (`ISCC.exe`,
   instalado via `winget install --id JRSoftware.InnoSetup`), passando a
   versão lida do `VERSION` como `/DMyAppVersion=...`.

O instalador resultante (`dist\SmartDockGroupsSetup-<versão>.exe`, pasta fora do
controle de versão) instala por usuário em
`%LocalAppData%\Programs\SmartDockGroups` — sem pedir elevação — com atalho no
menu iniciar, ícone de área de trabalho opcional e entrada em
Adicionar/Remover Programas. O desinstalador não toca em
`%AppData%\SmartDockGroups` (configuração e grupos do usuário) de propósito.

Este repositório precisou de um `NuGet.Config` próprio (na raiz) que
adiciona ao `nuget.org` o mapeamento dos pacotes de runtime que o publish
self-contained baixa (`Microsoft.NETCore.App.Runtime.*`,
`Microsoft.WindowsDesktop.App.Runtime.*` etc.) — a máquina de
desenvolvimento tem um `NuGet.Config` global com `packageSourceMapping`
restrito a uma lista fixa de outros projetos, que não inclui esses pacotes.
O arquivo local só acrescenta ao mapeamento, nunca mexe no global.

O instalador é publicado como **release do GitHub** (`alxchs/SmartDockGroups`,
repositório público), anexado à tag da versão — é o link estável que
funciona de fora desta rede, sem custo.

## Áreas do código

### Desktop (`src/SmartDockGroups.App/Desktop/`)

- **`DesktopGroupWindow`** — a janela de um grupo. Guarda os dois modos de
  exibição (`DesktopGroupDisplayMode.Panel` / `.AppFolder`) na mesma janela,
  alternando por visibilidade em vez de reconstruir; isso é o que permite
  voltar de um modo para o outro nas mesmas dimensões e posição anteriores
  (`MenuCategory.PanelX/PanelY` guarda onde o painel estava). O menu de
  contexto é o **mesmo** em qualquer lugar que se clique com o botão direito
  — cabeçalho, canvas vazio ou o ladrilho fechado do App Folder
  (`BuildHeaderContextMenu`, único método) — para o App Folder não ficar
  sem os comandos que só existiam no canvas (organizar, novo atalho,
  tamanho do ícone). É reconstruído a cada clique direito
  (`PreviewMouseRightButtonDown`) para o "visto" de qual organização está
  ativa nunca ficar desatualizado.
  - Arrastar um ícone (arquivo ou subpasta) para **fora** do próprio grupo,
    soltando sobre outro grupo aberto, move o objeto de verdade: sai da
    lista de origem e entra na lista de destino (`MoveEntryToOtherGroup` /
    `AcceptMovedEntry`), sempre em `Categories`/`Items` do grupo — nunca na
    configuração de nível superior, o que faria uma subpasta virar sem
    querer um grupo independente da área de trabalho. Um registro estático
    (`_allGroupWindows`) é o que permite a um grupo achar qual outro está
    embaixo do ponto onde o mouse soltou.
    - **Correção**: o ícone arrastado sumia assim que cruzava a borda da
      própria janela do grupo de origem, e soltava numa posição diferente
      de onde o usuário largou o mouse. As duas causas eram distintas: (1)
      o arrasto movia o tile só dentro do `Canvas` da janela de origem
      (`Canvas.SetLeft/Top`) — nada renderiza fora dos limites de uma janela
      WPF, então o ícone literalmente desaparecia ao ultrapassar a borda,
      mesmo captura de mouse continuando ativa; (2) o ponto de soltura usado
      para a posição final era o cursor cru (`PointToScreen(e.GetPosition(this))`),
      descartando o deslocamento entre onde o usuário pegou o ícone e o
      canto superior-esquerdo dele — deslocamento que o arrasto *dentro* do
      mesmo grupo preservava corretamente (`tileStart + delta`), mas o
      arrasto *entre* grupos não. Corrigido com uma janela-fantasma real
      (`DragGhostWindow`, um `VisualBrush` do próprio tile) que acompanha o
      cursor em coordenadas de tela — atravessando livremente os limites de
      qualquer janela, como o Explorer mostra um ícone "flutuando" durante
      um arrasto — e cuja posição (não o cursor) alimenta tanto a detecção
      de qual grupo está embaixo quanto a posição final no grupo de destino.
      `PointToScreen` só é chamado sobre a janela de origem (que nunca se
      move durante o arrasto), nunca lido de volta a partir da própria
      janela-fantasma, para não reintroduzir o mesmo tipo de bug de DPI já
      documentado abaixo para o App Folder. **Não verificado
      interativamente**: uma tentativa de reproduzir o arrasto via input
      sintético (`SendInput`/`mouse_event`) não conseguiu acertar os tiles de
      forma confiável — a janela usa `AllowsTransparency=true` (teste de
      clique pixel-a-pixel: um clique num pixel transparente atravessa para
      a janela debaixo) e os elementos internos não expõem uma árvore de
      UI Automation rica o bastante para mirar com segurança. A correção
      foi validada por leitura de código (a mesma inconsistência de
      coordenadas comparada ponto a ponto com o caminho que já funcionava
      dentro do mesmo grupo) e por compilação, não por reprodução visual.
  - A legenda do grupo tem uma dica (tooltip) resumindo suas configurações
    — estilo, organização, tamanho do ícone, opacidade, selo
    (`UpdateHeaderTooltip`), recalculada sempre que algo relevante muda.
  - Arrastar o ladrilho fechado do App Folder usa `DragMove()` — a mesma
    primitiva do WPF que já move a janela pelo cabeçalho — em vez de
    acumular a posição à mão a partir de deltas de `PointToScreen`
    (`OnFolderTileMouseDown`). A versão à mão foi a causa real de um bug em
    que o ladrilho "sumia": num sistema com DPI por monitor, chamar
    `PointToScreen` uma segunda vez no mesmo evento — logo depois de já ter
    mudado `Left`/`Top` a partir da primeira leitura — podia ler de volta
    um valor escalado por um fator de DPI diferente do da primeira chamada,
    jogando a janela para uma coordenada corrompida (observado uma vez
    bem exatamente em `Int16.MinValue`) sem monitor nenhum por perto.
    Reproduzido de propósito via UI Automation antes de corrigir — ver
    `PROMPT.md` para o passo a passo. O cabeçalho nunca teve esse problema
    porque já usava `DragMove()` desde o início.
  - Redimensiona por qualquer ponto da borda, não só por um canto: oito tiras
    invisíveis (`BuildResizeHandles`) cobrem os quatro lados e os quatro
    cantos do painel, cada uma com seu próprio cursor e sua própria borda
    oposta como âncora fixa (`OnResizeMouseMove`), do mesmo jeito que uma
    janela comum do Windows.
  - Dentro do painel, Ctrl+A seleciona tudo, Ctrl+C/Ctrl+X colocam os
    arquivos por trás dos ícones selecionados na área de transferência real
    do Windows (formato `CF_HDROP`, com o `Preferred DropEffect` que o
    Explorer também usa para diferenciar copiar de recortar), e Ctrl+V aceita
    de volta qualquer arquivo copiado em qualquer lugar do Windows, fixando-o
    como um novo ícone — o mesmo caminho que o arrastar-e-soltar já usava.
    Subpastas do grupo ficam de fora: são um agrupamento do próprio app, não
    uma pasta real em disco, então não há o que copiar. Um Ctrl+X **não**
    remove o ícone na hora — ele fica meio-opaco (como o Explorer faz) e só
    sai do grupo quando algo de fato aceita o "colar": um Ctrl+V em outro
    grupo deste mesmo app finaliza a remoção na hora; para um paste externo de
    verdade (Explorer), não existe um sinal do Windows para "terminou" — o
    grupo faz *polling* a cada 2s (`CheckPendingCutsAgainstDisk`) e só
    remove quando o arquivo realmente some do caminho original, o que só
    acontece se algo o moveu de fato.
  - Ícones no modo painel ficam em posição livre (`IconArrangement.None`, o
    padrão) ou em **organização automática, sempre por nome do atalho**
    (`IconArrangement.ByName`). Como um grupo só guarda atalhos, **não existe
    mais o menu "Ordenar por"** nem ordenação por tipo: o menu tem um único
    item marcável, "Organizar ícones automaticamente" (`ToggleArrangeIconsAutomatically`
    → `EnableAutoArrange`). `Grid` e `ByType` continuam no enum só para ler
    configs antigas; `FinishStructuralChange` os regrava como `ByName` na
    primeira mudança. A escolha é salva em
    `MenuCategory.IconArrangement` e reaplicada sozinha (`FinishStructuralChange`,
    ordem em `NameOrder`)
    toda vez que algo muda o conteúdo ou o tamanho do grupo — soltar um
    arquivo, colar, apagar, renomear, **redimensionar o painel** (`OnResizeMouseUp`)
    e **mudar o zoom dos ícones** (`SetIconScale`) — não só no momento em que
    o menu de organizar foi clicado.
  - `ArrangeInGrid` calcula quantas colunas cabem usando `TileSize * DesktopIconScale`,
    mas posiciona cada ícone em unidades de `TileSize` puro (sem multiplicar
    pelo zoom): as posições vivem no espaço de coordenadas de **antes** do
    `RenderTransform` do canvas, que já aplica esse mesmo zoom de novo na
    hora de desenhar. Multiplicar as duas vezes faz a grade "vazar" para
    fora do painel a qualquer zoom acima de 1×; só a conta de colunas
    precisa saber do zoom, para caber menos ícones por linha quando eles
    estão maiores.
  - O "Remover" do menu de um ícone ou pasta age sobre a seleção inteira
    quando o item clicado faz parte de uma seleção múltipla
    (`RemoveEntryRespectingSelection`), do mesmo jeito que o Explorer — antes
    disso, clicar em "remover" com vários itens marcados só removia o item
    sob o mouse. O menu de uma pasta é reconstruído a cada clique direito
    (`PreviewMouseRightButtonDown`) porque se "pode remover" depende da
    seleção no momento do clique, não de quando o ladrilho foi desenhado.
  - A borda do painel não vem do tema: é derivada da cor de fundo efetiva,
    10% mais clara (ou 10% mais escura, quando o fundo já é branco ou quase
    branco) — `ComputeGroupBorderColor`, recalculada toda vez que o fundo
    muda.
  - Passar o mouse sobre um ícone dá um leve aumento de escala e um tingimento
    do fundo, animados (`AttachHoverEffect`), do jeito que um cartão numa
    página web reage ao hover — sem competir com o destaque mais forte da
    seleção.
  - **Ctrl+F**, só no modo painel, abre uma barra de busca sob o título
    (`BuildSearchBar`) com contador de resultados ao vivo, destaque do trecho
    buscado dentro do próprio nome do ícone, e uma lista suspensa
    (`Popup` + `ListBox`) navegável com seta para cima/baixo; Enter num
    resultado tem o mesmo efeito que dar duplo clique nele e fecha a busca.
    Esc também fecha. Os dois únicos momentos em que o texto buscado é
    lembrado para a próxima vez são exatamente esses — Enter num resultado ou
    Esc — nunca um simples perder o foco.
- **Atalho de grupo na barra de tarefas** (v1.1.1.0). O item **"Criar atalho na barra de
  tarefas"** do menu do grupo (`IDesktopGroupCommands.CreateTaskbarShortcut`) grava
  `%AppData%\SmartDockGroups\GroupShortcuts\<nome> (<6 primeiros do id>).lnk`
  (`TaskbarShortcutService`, via `WScript.Shell`), com destino no próprio
  `SmartDockGroups.App.exe`, argumento `--desktop-action=focus-group:<id>` e o ícone do app, e abre o
  Explorer com o arquivo selecionado para o usuário arrastá-lo à barra. Clicar no atalho reusa
  o caminho que já existia: o processo novo não obtém o mutex, manda a ação pelo pipe
  `SmartDockGroups.DesktopAction` e sai; a instância aberta trata em `App.FocusGroup`
  (fechado → abre e grava; recolhido → expande; depois `DesktopGroupWindow.BringForwardAndPulse`:
  `RestoreIfMinimized` + `Activate` + pulso de opacidade). Se o app estava fechado, a mesma
  ação roda como `startupAction`. Id desconhecido mostra um balão "Grupo não encontrado".
  Isso exigiu uma identidade estável: **`MenuCategory.Id`** (GUID sem hífens, nulo em configs
  antigas; `DesktopOrganizerService.EnsureIds` preenche na carga e grava; `Clone()` o copia e o
  "Duplicar grupo" gera um novo). Medido nesta máquina (Windows 11, build 26340), com a
  aplicação aberta: um grupo enterrado na posição 36 da ordem-z foi para a 3 em ~0,6 s ao
  abrir o atalho; com todos os grupos fechados, abrir o atalho reabriu **só** o grupo dele.
  **Fixar sozinho não está implementado**: soltar um `.lnk` na pasta
  `...\Quick Launch\User Pinned\TaskBar` não fixa nada (nem reiniciando o Explorer), porque os
  botões vêm do valor binário `Favorites` da chave `Taskband`. Ver
  [`VIABILIDADE_ATALHO_TASKBAR_GRUPO.md`](VIABILIDADE_ATALHO_TASKBAR_GRUPO.md).
- **`AppFolderTile`** — o ladrilho fechado: mosaico 3×3 dos primeiros ícones,
  selo de contagem, nome do grupo. Puramente desenho, sem estado. Ao ser
  colocado como o ícone do grupo na área de trabalho (`DesktopGroupWindow`,
  modo App Folder), é desenhado **40% maior** — `Build` recebe um parâmetro
  `scale` e todas as medidas (plate, mosaico, selo, legenda) nascem já no
  tamanho final. A primeira versão fazia isso com um `LayoutTransform` no
  `ContentControl` que hospeda o ladrilho: visualmente idêntico, mas deixava
  o ladrilho **sem responder a clique** — a área de acerto do hit-test não
  acompanhava com segurança o tamanho pós-transform num `Window` com
  `SizeToContent`. Escalar as medidas na origem elimina a camada de
  transform inteira, então o que é desenhado é exatamente o que recebe o
  clique.
- **`GroupOverlayWindow`** — a folha que abre ao tocar o ladrilho. Tamanho:
  `min(2× a largura/altura do painel, 60%/50% da área útil do monitor)`,
  centralizada, nunca em tela cheia. Navega para dentro de subpastas sem abrir
  janelas novas; Esc sobe um nível e só fecha quando já está na raiz.
  - Um clique num ícone **seleciona**, não abre mais na hora — abrir é
    duplo clique ou Enter (`SelectEntry` / `_openActionsByEntry`). Antes,
    um único clique já disparava a ação, o que tirava a chance de só marcar
    um ícone para fazer outra coisa com ele. O ladrilho de "voltar" continua
    sem estado de seleção — clicar nele sempre sobe um nível na hora.
  - Tem a mesma seleção múltipla (Ctrl+clique, Ctrl+A), Delete (com a mesma
    regra de só remover pasta vazia) e F2 do painel, além de um menu de
    contexto por ícone (`BuildTileContextMenu`) com os comandos de sempre
    (executar como admin, abrir local, copiar caminho, propriedades,
    renomear, remover) — deliberadamente um `ContextMenu` simples, sem o
    tema completo do painel, já que esta janela já tem sua própria
    linguagem visual (a folha translúcida).
  - Digitar sem nenhum atalho faz o mesmo "pular para o item" do Explorer:
    acumula os caracteres digitados dentro de 1s um do outro e seleciona o
    primeiro ícone da pasta atual cujo nome comece com o texto acumulado
    (`OnPreviewTextInput`). O painel (`DesktopGroupWindow`) tem a mesma
    lógica, independente do Ctrl+F que já existia — o Ctrl+F abre uma caixa
    de busca com contador e lista; digitar sem Ctrl+F só pula a seleção,
    sem abrir nada na tela, do jeito que o Explorer sempre fez.
- **`GroupEntries`** — enumera os itens de um grupo (subpastas + itens fixados)
  na mesma ordem para o ladrilho, a folha e o painel concordarem sobre "o que
  tem dentro".
- **`MonitorPlacement`** — geometria pura (sem WPF) para resgatar grupos
  presos num monitor que sumiu, e para o Win+Shift+seta. Testada com cenários
  simulados de dois monitores (ver `PROMPT.md` → seção de testes).
- **`DisplayInventory`** — lê `Screen.AllScreens` e converte para os pixels
  independentes de dispositivo que uma `Window` usa.
- **`IDesktopGroupCommands`** — os comandos que um grupo pode pedir mas não
  pode fazer sozinho (duplicar-se, copiar sua aparência para os outros,
  virar o padrão, aplicar o papel de parede, **abrir as Configurações
  globais**). Implementado em `App.xaml.cs`, porque só quem possui a
  configuração inteira pode reescrever os outros grupos (ou, no caso das
  Configurações, é quem já guarda a janela). É por aqui que qualquer grupo
  — não só a bandeja — chega até a tela de Configurações.
- ~~**`DesktopContextMenuBuilder`**~~ — removido em 2026-09-24 (código morto). Foi escrito para
  desenhar um menu de clique direito temático na área de trabalho vazia, mas
  depois que a seção "Grupos não vivem dentro da área de trabalho" (abaixo)
  decidiu não reparentar os grupos no `Progman`/`SHELLDLL_DefView`, o app não
  tem como interceptar o clique direito na área de trabalho real — não sobrou
  chamador para esta classe. A integração real com o clique direito do
  Windows é outra, ver `DesktopContextMenuRegistration` logo abaixo.

### Serviços (`src/SmartDockGroups.App/Services/`)

- **`DesktopContextMenuRegistration`** — registra um submenu de verdade sob
  `HKCU\...\DesktopBackground\Shell` (o truque clássico de verbos por
  registro — sem extensão COM, sem hook) com cinco comandos (novo grupo,
  colapsar/expandir todos, todos em App Folder, todos em painel,
  Configurações). Cada verbo só relança o próprio `.exe` com um argumento
  `--desktop-action=...`; quem decide se isso inicia o app do zero ou
  entrega o comando ao processo já aberto é o `SingleInstanceCoordinator`.
  Roda em todo startup (idempotente). Os rótulos seguem o **idioma do
  Windows** (`LocalizationService.GetForLanguage` + `DetectLanguage`, nunca
  `Get`/`CurrentLanguage`) — de propósito, diferente de todo o resto do
  app: este menu é lido pelo Explorer, possivelmente com o app fechado, e
  os outros itens que já estão ao lado dele (Atualizar, Novo, Configurações
  de exibição) também seguem o idioma do Windows, não uma preferência de
  um app instalado. Dentro do app, tudo continua no idioma configurado nas
  Configurações, como sempre.
- **`SingleInstanceCoordinator`** — um `Mutex` nomeado decide quem é a
  primeira instância; qualquer instância seguinte manda a ação recebida por
  um named pipe (`SmartDockGroups.DesktopAction`) e sai imediatamente, sem abrir
  um segundo ícone de bandeja ou duplicar os grupos.
- **`IconCacheService`** — ícones vêm da lista de imagens do shell
  (`SHGetImageList`, tamanho jumbo/256px), com fallback em
  `Icon.ExtractAssociatedIcon`, `IShellItemImageFactory` e resolução de URIs de
  protocolo (`msteams:`, `ms-settings:`, `http:`, etc.) e `.url` via registro
  (`AssocQueryString`) e referências indiretas UWP (`SHLoadIndirectString`).
  O cache em disco é **PNG**, não `.ico`: salvar como `.ico` e reler achatava
  o canal alfa, deixando os ícones com halo preto ou branco. Bitmaps que contêm
  uma resolução pequena ancorada no canto de um canvas transparente (comportamento
  comum do ImageList jumbo para atalhos de protocolo) passam por corte automático
  de margens transparentes (`TrimTransparentMargins`), evitando que o mosaico 3×3
  do ladrilho de pasta de app reduza um ícone útil a poucos pixels. Além disso,
  `AppFolderTile` alinha os ícones centralizadamente nas células do mosaico.
- **`ShellCommands`** — os comandos de arquivo do menu de um item (executar
  como administrador, abrir local do arquivo, copiar como caminho,
  propriedades). Só aparecem quando o alvo é um arquivo/pasta real.
- **`ShellContextMenu`** — hospeda o `IContextMenu` de verdade do Explorer
  ("Menu padrão"), com extensões de terceiros e submenus. É desenhado pelo
  Windows, não pelo tema do app — é inerente a ser o menu real.
- **`WallpaperService`** — lê `SPI_GETDESKWALLPAPER`, com fallback no
  `TranscodedWallpaper` para quando o Windows está em modo slideshow.
- **`GlobalHotkeyService`** — registra **dois** atalhos globais
  independentes: abrir o menu da bandeja, e restaurar grupos escondidos.
  Veja "Atalho de restaurar grupos" abaixo para o porquê do segundo existir.

### Configurações e diálogos (`src/SmartDockGroups.App/Settings/`)

- **`TextPromptWindow`** e **`PromptPositioning`** — diálogo para entrada de texto
  (renomear grupo, renomear atalho/subpasta, novo grupo, nova pasta). O diálogo usa
  `WindowStartupLocation="Manual"` e é centralizado pelo `PromptPositioning.PositionWindowAtCursor`
  no monitor onde ocorreu o clique do usuário (`Screen.FromPoint(Control.MousePosition)`).
  Calcula a posição centralizada na área de trabalho útil desse monitor (`WorkingArea`),
  converte pixels físicos para DIPs do WPF com base no DPI do sistema e do monitor
  (`GetDpiForMonitor`/`GetDpiForSystem`), restringe as coordenadas para impedir que a
  janela saia dos limites da tela, e ajusta a posição do HWND diretamente via Win32
  `SetWindowPos` (`SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE`) além de atualizar
  `Left` e `Top`. Evita que o diálogo caia no monitor primário em sistemas multi-monitor.

### Tema (`src/SmartDockGroups.App/Theming/`)

Paleta e métricas de controle portadas do projeto irmão **IGCParam**
(`Dark.xaml`/`Light.xaml`): comandos de 28px, campos de 27px, cantos de
4–6px, anel de foco de 2px. `Icons.xaml` + `AppIcons.cs` são um conjunto de
~28 ícones de linha (grade 24×24, traço 1.7, pontas arredondadas) que
substituiu os glifos do Segoe MDL2 usados antes — os dois apps agora falam a
mesma linguagem visual.

Convenções de menu seguidas: toda linha reserva a coluna de ícone mesmo sem
ícone (para os textos ficarem alinhados); um item ligado usa `IconCheck` na
coluna, nunca um caractere colado no texto; reticências (`...`) só em comandos
que pedem mais informação antes de agir (Renomear..., Cor de fundo...); nunca
em ações diretas ou que já mostram uma confirmação própria.

### O bug do clique-direito que travava o Windows

Uma versão anterior tinha um hook `WH_MOUSE_LL` que fazia
`SendMessage` bloqueante para o Explorer dentro do callback do hook — callback
de hook de baixo nível tem um prazo rígido do Windows (~300ms), e uma chamada
síncrona entre processos ali trava o mouse do sistema inteiro até o Explorer
responder, ou faz o Windows remover o hook silenciosamente quando estoura o
prazo. **Foi removido**, e nenhum hook voltou desde então — inclusive o
menu de contexto real que o app hoje coloca na área de trabalho
(`DesktopContextMenuRegistration`) é puro registro do Windows: o Explorer lê
as chaves e decide sozinho quando mostrar o item, sem o app interceptar nada
do clique. As ações que esse menu antigo oferecia (recolher tudo, reunir
tudo) também estão na bandeja.

## Decisões de arquitetura

### Grupos não vivem "dentro" da área de trabalho — de propósito

Foi investigado ancorar as janelas de grupo como filhas da janela real da
área de trabalho (`SHELLDLL_DefView`, sob o `Progman`), o que as tornaria
imunes a "Mostrar área de trabalho" e a ficar atrás de outra janela. Uma sonda
em C# (não commitada, ver seção de testes) provou que:

- Uma janela Win32 comum reparentada assim **pinta e recebe clique**.
- Uma janela **WPF com `AllowsTransparency=true` reparentada perde toda a
  transparência** — vira opaca, mesmo mantendo as flags internas de janela em
  camadas. Confirmado comparando lado a lado com a mesma janela sem
  reparentar (que mistura de verdade com o que está atrás).

Como o produto usa transparência em vários lugares (sliders de opacidade por
grupo, papel de parede como fundo visto através da área, a folha translúcida
do modo pasta de app), a decisão foi **não ancorar**: os grupos continuam
como janelas flutuantes normais, e o problema do "Mostrar área de trabalho"
foi resolvido de outro jeito (próxima seção).

### Atalho de restaurar grupos (padrão: Win+Ctrl+Alt+D)

"Mostrar área de trabalho" (e Win+M) **não minimiza de verdade** uma janela
sem botão na barra de tarefas — o que os grupos são, de propósito
(`ShowInTaskbar=false`). Medido diretamente (`IsIconic`, ordem Z): o Windows
só traz a janela do Progman para o topo da ordem Z e deixa os grupos
**visíveis, no lugar certo, mas enterrados atrás dela** — `WindowState`
nunca muda para `Minimized`. A correção não checa `WindowState`; ela alterna
`Topmost` (true e depois false), o truque padrão para forçar uma janela de
volta ao topo da sua faixa de ordem Z sem roubar o foco. Testado
ponta a ponta: Win+D esconde, o atalho traz de volta na posição exata.

### Menu do shell convive com o menu temático, não o substitui

O menu de contexto de um ícone é construído pelo app (mesmo tema, mesmos
comandos do dia a dia), com uma entrada final **"Menu padrão"** que abre o
`IContextMenu` real do Explorer via `ShellContextMenu`. As duas coisas
coexistem porque o menu real traz extensões de terceiros (7-Zip,
antivírus, Git) que um menu temático nunca replicaria, mas ele é
desenhado pelo Windows e não pode seguir o tema do app.

## Multi-monitor

Regras (`MonitorPlacement.cs`, sem estado, testável sem dois monitores
físicos):

- Um monitor some → todo grupo nele é levado para o monitor restante mantendo
  o **lugar relativo** (canto superior direito → canto superior direito).
  Essa posição de resgate **não é salva**: o grupo guarda a posição original
  (`DesktopX`/`DesktopY`) como "casa" e volta sozinho quando o monitor volta.
  Um arrasto manual durante o resgate vira a nova casa normalmente.
- **Win+Shift+←/→** move o grupo em foco para o monitor vizinho, dando a
  volta nas pontas — a mesma convenção do Windows.
- **Posicionamento de diálogos no monitor do clique**: diálogos interativos (`TextPromptWindow`)
  abrem centralizados no monitor do cursor do usuário no momento da invocação,
  respeitando a área útil e o DPI daquele monitor específico (`PromptPositioning`),
  comprovado com medições e capturas em sistema multi-monitor real.

## Ícone e identidade visual

`tools/IconForge` desenha o `.ico` oficial em código (não é um arquivo de
imagem editado à mão): quatro ladrilhos 2×2 representando janelas/grupos, um
deles com uma estrelinha de destaque. Cada resolução de 16 a 256px é
desenhada separadamente — nunca reduzida de uma maior — porque um ícone
reduzido vira uma mancha cinza bem no tamanho em que a bandeja o mostra.

```
dotnet run --project tools/IconForge -- src/SmartDockGroups.App/Assets/SmartDockGroups.ico
dotnet run --project tools/IconForge -- saida.ico pasta-de-previa cyan   # variante alternativa
```

Variantes disponíveis: `tiles` (azul, a oficial), `cyan` (preto/ciano, no
espírito do launcher Android que inspirou o projeto) e `violet`.

## Versionamento

Um único arquivo `VERSION` na raiz (`a.b.c.d`), igual à convenção já usada
nos projetos Delphi do Alexandre. `src/Directory.Build.props` lê esse arquivo
e aplica `Version`/`AssemblyVersion`/`FileVersion` a todo projeto dentro de
`src/` — `SmartDockGroups.Core` e `SmartDockGroups.App` seguem a mesma versão
automaticamente. As ferramentas em `tools/` ficam de fora de propósito (não
fazem parte do produto entregue).

Para lançar uma versão nova: editar o `VERSION`, recompilar, commitar.

## O que ainda está pendente ou não verificado

- **Multi-monitor real**: a lógica de resgate de grupos (`MonitorPlacement`)
  foi validada por simulação e testes unitários. O posicionamento de diálogos
  no monitor sob o cursor (`PromptPositioning` / `TextPromptWindow`) e o fluxo de
  "Novo grupo" foram comprovados em sistema multi-monitor real nesta máquina com
  o app em execução real (`SmartDockGroups.App.exe` Release em modo original sem manifesto), medição dos retângulos físicos
  feita pela ferramenta de teste `GroupProbe` (sob manifesto nativo `PerMonitorV2`), validação de coordenadas do cursor real
  (`GetCursorPos`) e capturas recortadas das janelas (`B-fluxo-real-monitor1.png` e `B-fluxo-real-monitor2.png`).
- **Renderização real do mosaico e painel (Defeito A / OS 04)**: comprovada no executável publicado self-contained (`SmartDockGroups.App.exe` em `win-x64/publish/`) cobrindo cenários "Upgrade" (com IconCache existente) e "Instalação limpa" (com IconCache vazio), exibindo o ícone nítido do Teams tanto para URI direta (`msteams://...`) quanto para atalhos de Área de Trabalho renomeados (`Alexandre.url` resolvido para `Alexandre Chagas Sousa.url` quando há exatamente um candidato inequívoco; múltiplos candidatos são tratados como não resolvidos para evitar acionar ou exibir alvo incorreto), comprovado em `upgrade-painel.png`, `upgrade-mosaico.png`, `limpa-painel.png`, `limpa-mosaico.png`, `A-app-real-painel.png` e `A-app-real-mosaico.png`. O cache agora usa schema versionado `v2:` para URIs e URLs, evitando entradas estagnadas permanentes com ticks=0.
- **Importar configurações só entra em vigor ao clicar em Salvar** na tela de
  Configurações — para um restore de backup isso é fácil de esquecer.

## Testes e Sonda de Caracterização

O projeto conta com uma suíte automatizada de testes xUnit em `tests/SmartDockGroups.Tests`
(51 asserções cobrindo geometria de monitor `MonitorPlacement`, posicionamento de janelas por monitor
`PromptPositioning` com DPI heterogêneo e contenção na área de trabalho, integridade e contrato
de serialização de `config.json`, extração e corte de transparência de ícones no
`IconCacheService`, schema versionado `v2`, resolução unívoca de atalhos `.url` de área de trabalho e rejeição sob ambiguidade e renderização do ladrilho `AppFolderTile`).

Além dos testes unitários, há a sonda de caracterização em C# (`tests/GroupProbe`) que sobe
a aplicação de verdade com um fixture de teste, valida backup por hash, mede a geometria
das janelas, gera hash SHA-256 das capturas e inspeciona a árvore de menus via UI Automation,
permitindo comparar com a linha de base via `compare_reports.py`. A aplicação mantém seu modo de DPI original (System DPI Aware sem manifesto), enquanto a sonda de teste `GroupProbe` conta com manifesto nativo `app.manifest` (`PerMonitorV2`) para medição física real e bridge automático para a desktop interativa
`WinSta0\Default`. A comparação contra a baseline `report-master.json` apresentou 316 de 316 campos idênticos.

> **Nota sobre PowerShell para diagnóstico Win32:** `FindWindow("Progman",
> $null)` em PowerShell retorna identificador vazio mesmo quando a janela
> existe — a marshalling do PowerShell não converte `$null` para `NULL` num
> parâmetro `string` de P/Invoke. Isso já produziu duas conclusões erradas
> nesta investigação (achar que o `Progman` não existia; achar que o
> `SetParent` entre processos não funcionava mais). Uma sonda escrita em C#
> puro deu a resposta certa nas duas vezes. Para qualquer diagnóstico Win32
> daqui para frente, escrever a sonda em C#, não em PowerShell.
