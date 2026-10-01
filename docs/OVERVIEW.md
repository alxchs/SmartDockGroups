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
  SmartDockGroups.Tests/          73 asserções xUnit (geometria de monitor, posicionamento do diálogo perto do cursor e no monitor do clique, contrato do config, corte de transparência de ícones, renderização, versionamento v3 do cache, resolução unívoca de atalhos .url renomeados, nomes únicos no grupo, compartilhamento de aparência por aspecto, padrões de novos grupos, recuperação de atalho perdido)
  GroupProbe/                     sonda que sobe o app e caracteriza os grupos
  baseline/                       referência da sonda, regenerada em 2026-09-30 (1.1.2.0), para comparar refatorações
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
  (`MenuCategory.PanelX/PanelY` guarda onde o painel estava). O menu do
  grupo é **um só** (`PopulateGroupMenu`) e aparece igual em qualquer lugar
  — cabeçalho, botão "…", canvas vazio, ladrilho fechado do App Folder,
  fundo da folha aberta do App Folder e o submenu "Grupo "<nome>"" no menu
  de cada ícone. Desde 2026-09-30 ele é **agrupado por contexto**, em vez de
  uma lista corrida de ~20 itens (ordem desde a 1.1.4.0):
  1. **Novo ▸** — Atalho…, Grupo…, Atalho para um grupo ▸ (lista os outros
     grupos); Importar atalhos…; Colar;
  2. **Exibição ▸** — estilo painel/pasta de apps, organizar automaticamente,
     tamanho do ícone ▸, espaçamento entre ícones ▸;
  3. **Aparência ▸** — cor de fundo…, imagem de fundo…, papel de parede como
     fundo, remover imagem, opacidade ▸, **Compartilhar com os outros grupos ▸**;
  4. **Ordem na área de trabalho ▸** — frente/fundo e envio para monitores; Atalho na
     barra de tarefas;
  5. **uma seção só para o grupo**: Renomear…, Expandir/Colapsar (ou Abrir pasta),
     Acoplar/Desacoplar, Duplicar, Fechar grupo, Remover grupo…;
  6. **Configurações… sempre por último.**

  O menu de um ícone (`BuildEntryMenu`) segue a mesma lógica — abrir
  (executar como admin, abrir local), área de transferência (recortar,
  copiar, copiar como caminho), alterar (renomear F2, localizar atalho
  perdido, remover Del), Windows (propriedades, menu padrão) — e termina com
  o submenu do grupo e **Novo grupo…**, para criar um grupo sem precisar
  achar um espaço vazio para clicar. É o mesmo construtor para o painel e
  para a folha do App Folder. Os menus são reconstruídos a cada clique
  direito (`PreviewMouseRightButtonDown`), para "vistos" e itens habilitados
  refletirem o estado do momento.
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
      documentado abaixo para o App Folder. **Verificado com o mouse real em
      2026-09-30** (`GroupProbe --verify-lote --drag`): a tentativa antiga falhava
      por mirar o ícone sem saber onde ele estava; agora o alvo é achado pela
      legenda via UI Automation. Apertar sobre "Google Chrome" num grupo
      organizado por nome, mover em 24 passos e soltar num ponto vazio de um grupo
      de posição livre: origem 14→13 itens, destino 3→4, e a legenda do ícone parou a
      17 px do ponto da soltura.
  - **Menus montados só no clique direito (1.1.5.0).** Cada ícone, o cabeçalho, o canvas e
    o ladrilho montavam um menu completo ao abrir — com o submenu "Grupo ▸" de ~50 itens
    em cada um dos 35 ícones — e o refaziam no clique. Era a maior parte do tempo de
    abertura dos grupos. Medido com a configuração real (3 grupos, 35 atalhos): primeira
    abertura, cache de ícones vazio, 4,6 s → 2,8 s até os grupos aparecerem; aberturas
    seguintes 3,0 s → 2,0 s. O publish passou a usar ReadyToRun (menos compilação a cada
    partida; +5,6 MB, ~0,1–0,6 s, mais na primeira abertura). Sobra ~1 s de partida do WPF.
  - **Texto dos menus legível em qualquer tema do app.** O app tem um estilo global de
    `TextBlock` (`AppTextBrush`, claro/escuro conforme o Windows) que vencia a cor que o grupo
    dá ao menu: com o Windows em modo claro, o texto saía quase preto (`#1A1A1A`) sobre o
    menu escuro do grupo, e só os ícones — que usam a cor do grupo — apareciam. Reproduzido com
    `SDG_TEST_APPTHEME=Light` e corrigido em `MenuTemplates.xaml` (o texto do cabeçalho segue o
    `Foreground` do próprio item). Se o grupo mandar uma cor de texto escura sobre fundo escuro,
    o menu agora obedece (é a cor que o grupo pediu).
  - **Acoplado, arrastar qualquer título leva a pilha junto, ao vivo** (`DockFollow`, a
    cada `LocationChanged`): medido arrastando o 3º de 6 grupos, os seis se moveram juntos
    enquanto o botão estava apertado e terminaram com a mesma diferença.
  - **Limite de 30 entradas por grupo** (`GroupLimits`, no Core; atalhos + subpastas).
    Colar, soltar, importar, criar e mover de outro grupo passam pelo mesmo teste: o que
    couber entra, o resto fica onde estava (um arraste recusado não perde nada) e um aviso
    diz o limite, quantos não entraram e que o ideal é criar um novo grupo. Grupos que já
    passavam de 30 numa configuração antiga ficam como estão, mas não aceitam mais nada.
  - **Atalho para um grupo** (`LaunchItemType.GroupLink`; `Target` = `Id` do grupo): vem de
    Novo ▸ Atalho para um grupo ▸, nunca digitado no editor. Um grupo não aceita atalho
    para si mesmo nem dois para o mesmo grupo (`GroupLimits.CanHoldLinkTo`, valendo
    também para arrastar de outro grupo). Clicar abre o grupo se estiver fechado, traz
    para frente, e seleciona o **primeiro ícone na ordem visual** (a barra de tarefas usa o
    mesmo caminho); com os grupos acoplados, abre o dele na pilha. O nome acompanha o do
    grupo (renomear atualiza os atalhos) e apagar o grupo apaga os atalhos para ele.
    Provado no app real: o atalho aparece, a segunda entrada fica desabilitada, e com o
    grupo fechado o duplo clique o reabre com o título "Dev Apps [DBeaver Community]".
  - **Navegação por teclado pela posição na tela** (`TileNavigation`, no Core): as linhas
    vêm da posição dos ícones desenhados, não da ordem de criação (que fazia a seleção
    "pular"). Setas esquerda/direita andam na ordem de leitura; cima/baixo mantêm a coluna
    mais próxima; Page Up/Down saltam as linhas visíveis; Home/End vão ao primeiro/último.
    **Não há começo nem fim**: passar do último vai ao primeiro e vice-versa (linhas e
    colunas). Shift estende a seleção pela mesma ordem. Só no modo painel. Medido por teclas
    reais num grupo de 15 ícones em 5 linhas: 12 passos, todos como calculados a partir
    das posições salvas.
  - O ícone selecionado (e o realce do mouse e da busca) é uma **placa de cantos
    arredondados** (`WrapTile`, raio 10); o título do grupo mostra a seleção entre colchetes
    — `Grupo [Item]`, `Grupo [3]` para vários, só `Grupo` sem seleção. O `Title` da janela
    continua sendo o nome puro. O menu de um ícone agora é refeito a cada clique direito e
    seleciona o ícone se ele estava fora da seleção; antes agia sobre a seleção da hora
    em que o ícone foi desenhado.
  - **Um grupo nunca fica fora do monitor** (`KeepOnScreen`): ao soltar o arrasto do título
    ou do ladrilho, ao expandir e ao redimensionar, os quatro lados voltam para dentro da
    área útil. Se a soltura deixa o grupo entre dois monitores, ele vai para o monitor onde
    estava o **mouse** na soltura. Esse monitor é perguntado em contexto "por monitor"
    (`SetThreadDpiAwarenessContext`) porque, com monitores de escalas diferentes, este
    processo (ciente do DPI do sistema) via o cursor em x=3915 do segundo monitor como
    pertencente ao primeiro — medido e corrigido em 2026-09-30. Provado com arrastes reais:
    metade fora pela esquerda, topo, rodapé e direita, e sobre a divisa com o mouse de
    cada lado (6 de 6).
  - A legenda do grupo **não** mostra dica (tooltip): `UpdateHeaderTooltip`
    ainda monta as linhas (estilo, organização, tamanho, opacidade), mas
    termina com `ToolTip = null` — conferido no código em 2026-09-30. A
    janela do grupo leva o nome do grupo como `Title` (nunca desenhado, pois
    não há barra de título do sistema), o que a identifica para leitores de
    tela, UI Automation e a sonda.
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
  - Colar, soltar, importar e "Novo atalho" entram por um único caminho,
    `AddIncomingPaths`: o `.lnk`/`.url` é **adotado** (copiado para
    `%AppData%\SmartDockGroups\Shortcuts`, ver `ShortcutStore`), o mesmo
    atalho (mesmo nome e mesmo destino) não é acrescentado de novo, um nome já
    usado no grupo ganha " (2)", " (3)"… (`GroupNames.MakeUnique`), e sem ponto
    de soltura cada item vai para a **primeira célula livre** da grade
    (`PlaceInFreeCells`) em vez de todos empilhados no canto.
  - **Correção (2026-09-30): atalho colado só aparecia depois de "forçar"
    ordenar, redimensionar ou salvar as Configurações.** Causa:
    `FinishStructuralChange` só redesenhava os ícones quando o grupo estava em
    organização automática; em posição livre (o padrão de todo grupo novo) ele
    não repintava nem salvava. Colar, soltar, criar, renomear e remover
    mudavam a lista sem o painel mostrar. Agora os dois modos repintam e
    salvam no mesmo lugar, e a folha aberta do App Folder também é
    atualizada (`GroupOverlayWindow.Refresh`). Provado no app real (grupo em
    posição livre, 3 atalhos colados aparecem na hora):
    `docs/execucoes/lote-colar-antes.png` / `lote-colar-depois.png`.
  - **Nomes únicos dentro do grupo**, sem diferenciar maiúsculas nem espaços
    nas pontas (`GroupNames`, no Core): renomear para um nome já usado é
    recusado com o motivo sob a caixa e o diálogo continua aberto
    (`TextPromptWindow` com validação; `lote-renomear-duplicado.png`); "Novo
    atalho…" recusa da mesma forma; colar, soltar, importar e mover de outro
    grupo numeram o repetido. Configurações antigas e JSON importado são
    normalizados na carga (`GroupNames.EnsureUnique`: o primeiro mantém o
    nome, os seguintes ganham o número).
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
    zoom do canvas (hoje um `LayoutTransform`), que já aplica esse mesmo zoom
    de novo na hora de desenhar. Multiplicar as duas vezes faz a grade "vazar" para
    fora do painel a qualquer zoom acima de 1×; só a conta de colunas
    precisa saber do zoom, para caber menos ícones por linha quando eles
    estão maiores.
  - **Rolagem no modo painel**: o canvas fica dentro de um `ScrollViewer` e o
    zoom passou de `RenderTransform` para `LayoutTransform`, para a rolagem
    enxergar o tamanho real. `UpdateCanvasExtent` dimensiona o canvas até o
    ícone mais distante e nunca menor que a área visível (o vazio continua
    recebendo clique, seleção por retângulo e menu); desconta a faixa da outra
    barra antes de decidir a largura, pois preencher a largura inteira antes de
    a barra vertical aparecer deixava alguns pixels sobrando e uma barra
    horizontal inútil (medido e corrigido em 2026-09-30). A roda do mouse rola;
    Ctrl+roda continua sendo zoom. Setas, seleção e resultado do Ctrl+F rolam
    até o ícone (`ScrollEntryIntoView`). O quadrado onde as duas barras se
    encontram é transparente (a cor de sistema que o template usa é
    sobrescrita só para essa janela).
  - Os comandos de um ícone (remover, recortar, copiar) agem sobre a seleção
    inteira quando o ícone clicado faz parte de uma seleção múltipla, como no
    Explorer (`BuildEntryMenu` → `RemoveEntries` / `CopyEntriesToClipboard`).
    Uma pasta **com conteúdo** também pode ser removida: a pergunta diz quantos
    atalhos serão perdidos e "Não" é a resposta padrão.
  - **Remover grupo** vale para qualquer grupo, não só o vazio (a regra
    anterior foi retirada a pedido): com atalhos dentro, o aviso diz quantos
    serão perdidos e o botão padrão é **Não** — Enter mantém o grupo
    (`OnDeleteGroupClick`; medido por UI Automation: botão focado "No",
    grupo intacto após Enter; `lote-remover-grupo-aviso.png`).
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
    **Durante a busca os próprios ícones respondem** (`ReapplySearchEmphasis`):
    cada ícone que bate ganha fundo com a cor de destaque, o resultado que o
    Enter abriria ganha o destaque cheio e é rolado para a vista, e todos os
    outros ficam esmaecidos (opacidade 0,22). Ao fechar a busca, seleção e
    "recortado" voltam ao normal.
- **Atalho de grupo na barra de tarefas** (v1.1.1.0; local e ID revistos em 2026-09-30). O item
  **"Criar atalho na barra de tarefas"** do menu do grupo (`IDesktopGroupCommands.CreateTaskbarShortcut`)
  grava `<Menu Iniciar do usuário>\Programs\Smart Dock Groups\<nome do grupo>.lnk`
  (`TaskbarShortcutService`; a pasta vem do known folder `Programs`, nunca de um caminho fixo; é a
  mesma pasta onde o instalador põe o atalho do app), com destino no próprio
  `SmartDockGroups.App.exe`, argumento `--desktop-action=focus-group:<id>`, o ícone do app e um
  **AppUserModelID próprio por grupo** (`SmartDockGroups.Group.<id>`, gravado via `IPropertyStore`).
  Abre o Explorer com o arquivo selecionado e um balão explica: clique direito → Fixar na barra de
  tarefas (no Windows 11, em "Mostrar mais opções" ou com Shift+clique direito), ou digitar o nome
  do grupo no Iniciar e fixar o resultado. O atalho é achado pelo argumento (o `Id`), não pelo nome:
  renomear o grupo renomeia o arquivo, e remover o grupo o apaga. O nome leva o `Id` curto só
  quando outro grupo já usa o mesmo nome.
  **Por que o app não fixa sozinho** (medido em 2026-09-30, Windows 11 26H2, build 26340):
  (1) `User Pinned\TaskBar` não é a barra: havia um `Google Chrome.lnk` ali que não estava fixado;
  a barra é o valor binário `Taskband\Favorites`. (2) A interface de fixar do shell
  (`IPinnedList3::Modify`, validada antes por leitura da lista real de fixados) devolveu `S_OK` e não
  mudou nada: fora do Explorer o pedido é ignorado. (3) O verbo "Pin to taskbar" nem é listado para
  programas (nem para o Bloco de Notas); só "Unpin" aparece. Os caminhos restantes seriam se passar
  pelo Explorer ou escrever o blob não documentado, e foram recusados.
  **O que foi provado no Explorer de verdade**: Shift+clique direito no atalho de grupo mostrou
  "Pin to taskbar"; ao clicar, o **próprio Windows** copiou o `.lnk` para `User Pinned\TaskBar` e a
  lista de fixados passou de 24 para 25; com esse fixado, um segundo atalho de grupo **continuou**
  oferecendo "Pin to taskbar" (sem o AppUserModelID próprio, um atalho para o mesmo `.exe` de um
  programa fixado aparece como "Unpin from taskbar" — controle medido com o VS Code); desafixar
  devolveu a lista e o `Favorites` exatamente ao estado inicial (24 itens, 19.703 bytes). Clicar no atalho reusa
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
- **Acoplar todos os grupos** (`DesktopOrganizerService.Dock`/`Undock`,
  `LauncherConfiguration.Dock`, cálculo em `DockLayout` no Core). Empilha todos os
  grupos abertos numa coluna, fechados, a partir de onde está o grupo clicado e com
  a largura dele (ou do grupo mais acima/à esquerda, quando vem da bandeja ou da área
  de trabalho). Antes guarda, por grupo, posição, tamanho, fechado/aberto, estilo
  (painel ou pasta de apps) e a posição do painel (`GroupPlacement`); pasta de apps
  vira barra de título enquanto está acoplada. A seta de expandir de um grupo
  acoplado abre **só** ele (o que estava aberto fecha) e os de baixo deslizam para
  baixo (animação do `Top`); fechar puxa de volta; "colapsar todos" fecha o aberto.
  O grupo aberto recebe a altura que tinha antes de acoplar, cortada ao que cabe até o
  rodapé da área útil; se nem as barras cabem, a pilha inteira sobe (nunca acima do
  topo da área útil — a barra de tarefas pode estar em cima). Arrastar o título de
  qualquer um move a pilha; os comandos "todos para este monitor" / "para o monitor
  N" / "reunir" movem a pilha; as bordas de redimensionar ficam desligadas. **Exibição ▸ Estilo
  continua valendo acoplado** (`DockedStyle`/`DockToggleStyle`): o grupo segue sendo uma barra na
  pilha, e a opção troca o estilo guardado, aplicado ao desacoplar — o rótulo mostra o estilo guardado.
  (Antes ficava desabilitada, e "sumia" para quem acoplou os grupos; 1.1.5.1.) Grupos abertos ou criados durante o acoplamento entram no fim (com a
  aparência deles guardada antes); fechar um tira-o da pilha sem deixar buraco. O
  estado fica no `config.json`: o app reaberto volta acoplado. **Desacoplar** devolve
  cada grupo ao que foi guardado. Provado no app real (`GroupProbe --verify-lote
  --dock`): 5 grupos empilhados e encostados na mesma coluna; expandir o 2º empurrou
  os de baixo; expandir o 3º fechou o 2º; arrastar moveu todos (+300, +150 px);
  reabrir manteve; desacoplar devolveu cada janela ao mesmo retângulo e cada grupo à
  mesma configuração (inclusive "Teams Chat" de volta a pasta de apps).
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
  - Tem a mesma seleção múltipla (Ctrl+clique, Ctrl+A), Ctrl+C/Ctrl+X,
    Delete e F2 do painel. **Desde 2026-09-30 o menu de um ícone é o mesmo
    menu temático do painel** (`DesktopGroupWindow.BuildEntryMenu`, com o
    painel como dono): antes era um `ContextMenu` simples, sem tema, com
    menos comandos e — no tema escuro — texto ilegível. O clique direito no
    **fundo** da folha abre o menu do grupo (antes não fazia nada). Renomear e
    remover pela folha agora **salvam** (antes mudavam só a memória).
    A folha fecha ao perder o foco, mas não quando quem tomou o foco é um
    diálogo que ela mesma abriu (renomear, "remover?", seletor de arquivo):
    `OnDeactivated` decide depois que a nova janela da frente existe
    (`ForegroundIsOwnDialog`). Provas: `lote-pasta-menu-atalho.png`,
    `lote-pasta-menu-fundo.png`.
  - Digitar sem nenhum atalho faz o mesmo "pular para o item" do Explorer:
    acumula os caracteres digitados dentro de 1s um do outro e seleciona o
    primeiro ícone da pasta atual cujo nome comece com o texto acumulado
    (`OnPreviewTextInput`). O painel (`DesktopGroupWindow`) tem a mesma
    lógica, independente do Ctrl+F que já existia — o Ctrl+F abre uma caixa
    de busca com contador e lista; digitar sem Ctrl+F só pula a seleção,
    sem abrir nada na tela, do jeito que o Explorer sempre fez.
- **Idiomas completos (1.1.5.1).** Seis idiomas (de, es, it, ja, pl, ru) estavam sem 10 chaves — "Fechar
  grupo", "Colar", "Recortar", "Copiar", a busca e os rótulos "todos em pasta/painel" do menu da área de
  trabalho — e mostravam inglês (o plano B de `LocalizationService.Get`). Achado ao auditar o prompt de
  geração; corrigido e travado por teste. O app em alemão foi aberto e o menu do grupo lido por UI
  Automation ("Einfügen", "Gruppe schließen"…).
- **`docs/PROMPT_GERACAO_UNICA.md`** — a especificação do zero, para entregar a outra IA e obter este
  app numa única interação. Os apêndices (paleta, ícones, textos en/pt, constantes) são **gerados** por
  `tools/atualizar_apendices_prompt.py`; rode-o ao mudar paleta, ícones, textos ou as constantes citadas.
- **`GroupEntries`** — enumera os itens de um grupo (subpastas + itens fixados)
  na mesma ordem para o ladrilho, a folha e o painel concordarem sobre "o que
  tem dentro".
- **`MonitorPlacement`** — geometria pura (sem WPF) para resgatar grupos
  presos num monitor que sumiu, e para o Win+Shift+seta. Testada com cenários
  simulados de dois monitores (ver `PROMPT.md` → seção de testes).
- **`DisplayInventory`** — lê `Screen.AllScreens` e converte para os pixels
  independentes de dispositivo que uma `Window` usa.
- **`IDesktopGroupCommands`** — os comandos que um grupo pode pedir mas não
  pode fazer sozinho (**criar um grupo novo** perto do mouse, duplicar-se,
  **compartilhar aspectos da aparência** com os outros ou como padrão,
  aplicar o papel de parede, **abrir as Configurações globais**).
  `ShareVisual(origem, aspectos, comoPadrão)` usa `VisualAspects` (Core):
  cor de fundo (com a cor de texto derivada), imagem de fundo, opacidade,
  espaçamento entre ícones, tamanho do ícone, ou tudo. Só o aspecto
  escolhido se move — compartilhar a imagem não impõe a cor e vice-versa
  (`MenuCategory.CopyVisualFrom(origem, aspectos, temaPadrão)`). "Como padrão
  para novos grupos" grava a cor no tema padrão e os demais aspectos em
  `LauncherConfiguration.GroupDefaults`; antes de trocar a cor padrão, cada
  grupo que ainda usava o tema padrão recebe uma cópia dele, para só os
  grupos criados depois mudarem. Medido no app real: "Só a opacidade" levou
  0,55/0,8 a todos os grupos da área de trabalho sem mexer em nenhuma cor. Implementado em `App.xaml.cs`, porque só quem possui a
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
  registro — sem extensão COM, sem hook) com onze comandos
  (`DesktopContextMenuRegistration.Verbs`): novo grupo, abrir todos, fechar todos,
  **trazer todos para frente**, **mandar todos para o fundo**, colapsar/expandir
  todos, **acoplar todos**, **desacoplar**, todos em App Folder, todos em painel,
  Configurações. Cada verbo só relança o próprio `.exe` com um argumento
  `--desktop-action=...`, então **nenhum depende do app já estar na memória**: se
  ele não está rodando, sobe e executa o comando; se está, o
  `SingleInstanceCoordinator` entrega o comando ao processo aberto. Um comando que
  sobe o app é executado só depois que os grupos foram mostrados e dispostos
  (`Dispatcher` em `ApplicationIdle`) — antes, "trazer para frente" ou "focar grupo"
  agiam sobre janelas ainda não desenhadas. Provado em 2026-09-30 com o app fechado
  para acoplar, trazer para frente, mandar para o fundo, abrir todos e colapsar todos,
  e com o app aberto para desacoplar (a segunda instância repassou e saiu). Roda em
  todo startup e reescreve a lista inteira (verbos renumerados de uma versão antiga
  não ficam para trás). Os mesmos comandos de "todos os grupos" estão no menu da
  bandeja. Os rótulos seguem o **idioma do
  Windows** (`LocalizationService.GetForLanguage` + `DetectLanguage`, nunca
  `Get`/`CurrentLanguage`) — de propósito, diferente de todo o resto do
  app: este menu é lido pelo Explorer, possivelmente com o app fechado, e
  os outros itens que já estão ao lado dele (Atualizar, Novo, Configurações
  de exibição) também seguem o idioma do Windows, não uma preferência de
  um app instalado. Dentro do app, tudo continua no idioma configurado nas
  Configurações, como sempre.
- **`ShortcutStore`** — mantém os atalhos dos grupos vivos sozinhos.
  **Causa dos ícones que sumiam (2026-09-30):** os seis atalhos citados
  (DBeaver, Parametrizacao, SSMS 22, Edge, NVIDIA App, WinDirStat)
  apontavam para `.lnk` em `...\Quick Launch\User Pinned\TaskBar\`, e o
  Windows apaga esse arquivo quando o app é desafixado da barra. O grupo só
  guardava o caminho. Agora todo `.lnk`/`.url` é **adotado** ao entrar no
  grupo (copiado para `%AppData%\SmartDockGroups\Shortcuts`, como o Explorer
  faz ao colar um atalho em outra pasta) e, na inicialização,
  `AdoptAndRecoverAll` adota os que ainda existem e **recupera** os perdidos
  procurando no Menu Iniciar (usuário e todos), áreas de trabalho e Quick
  Launch: mesmo nome de arquivo, depois nome do item, depois prefixo
  ("DBeaver" para "DBeaver Community"); vários candidatos só são aceitos se
  todos abrem o mesmo programa (`ReadLinkTarget`, via `IShellLink`), nunca por
  palpite. Antes de reescrever, o `config.json` ganha uma cópia datada ao
  lado (`config.json.before-repair-*.bak`). Medido numa cópia da
  configuração real: 5 dos 6 recuperados; "Parametrizacao de acesso ao IGC"
  não existe em lugar nenhum procurado e fica marcado como perdido. Item
  cujo arquivo sumiu mostra um triângulo de aviso no lugar do ícone (nunca
  um espaço vazio), dica com o caminho e o comando "Localizar atalho
  perdido…". Os arquivos adotados nunca são apagados pelo app.
- **`ApplicationPaths`** — `SMARTDOCKGROUPS_DATA_DIR` aponta a pasta de dados
  para outro lugar (e os atalhos de grupo vão para `<dados>\StartMenu`, não para o Iniciar real); essa instância também usa mutex/pipe próprios e não
  registra o menu da área de trabalho, então roda ao lado da instância real
  sem tocar na configuração, no registro nem na trava de instância única. É
  como a sonda testa o app (ver "Testes").
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
  **Atalho que só diz "use o ícone do programa"** (local do ícone vazio no
  `.lnk`) é desenhado a partir do próprio programa, com a seta de atalho do
  usuário por cima (`ShortcutStore.IconSourceForTargetIcon`,
  `ExtractWithShortcutArrow`): em processo com DPI (o app a 150%), o shell
  devolvia um documento em branco para "SQL Server Management Studio 22.lnk"
  em todas as APIs testadas (`ExtractAssociatedIcon`, `SHGetFileInfo`, lista
  jumbo, `IShellItemImageFactory`), enquanto o `SSMS.exe` saía certo em
  todas. Por isso o cache em disco passou ao schema **`v3:`** (os PNGs
  gravados antes são extraídos de novo).
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
  `WindowStartupLocation="Manual"` e, desde 2026-09-30, abre **perto do mouse**
  (`CalculateNearCursorPosition`: centrado na horizontal sob o ponteiro, logo abaixo
  dele, empurrado para dentro da área útil perto de uma borda) no monitor do clique
  (`Screen.FromPoint(Control.MousePosition)`) — antes ficava no centro do monitor, longe
  da mão numa tela muito grande. Medido: 18 px entre o ponteiro e o diálogo; o grupo
  criado por "Novo grupo…" também nasce ali (antes nascia em 40,40 do monitor principal).
  A área útil (`WorkingArea`) já exclui a barra de tarefas **em qualquer lado** (a do
  Alexandre fica em cima). Aceita um validador opcional que recusa o valor com o motivo
  sob a caixa, sem fechar o diálogo (usado para nomes repetidos).
  Converte a posição para a área de trabalho útil desse monitor (`WorkingArea`),
  converte pixels físicos para DIPs do WPF com base no DPI do sistema e do monitor
  (`GetDpiForMonitor`/`GetDpiForSystem`), restringe as coordenadas para impedir que a
  janela saia dos limites da tela, e ajusta a posição do HWND diretamente via Win32
  `SetWindowPos` (`SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE`) além de atualizar
  `Left` e `Top`. Evita que o diálogo caia no monitor primário em sistemas multi-monitor.

- **`SettingsWindow`** — além de aparência e comportamento, tem "Importar e
  exportar": **Importar atalhos…** (seletor de arquivos com seleção múltipla e
  `DereferenceLinks = false`, para trazer o próprio `.lnk` com argumentos e
  ícone; Ctrl+A pega a pasta inteira) seguido de **`ImportTargetWindow`**, que
  pergunta o grupo de destino ou o nome de um grupo novo; e Importar/Exportar
  configuração (JSON). "Salvar" agora aplica **só** o que está na janela: os
  grupos só são substituídos quando um JSON foi importado. Antes, Salvar
  sempre trocava todos os grupos pela cópia tirada quando a janela abriu, o
  que desfazia qualquer mudança feita nos grupos com ela aberta. O mesmo
  "Importar atalhos…" existe no menu de cada grupo, direto para ele.
  Provas: `lote-configuracoes.png`, `lote-importar-escolher-grupo.png`,
  `lote-importar-resultado.png`.

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
- **Importar configuração (JSON) só entra em vigor ao clicar em Salvar** na tela de
  Configurações — para um restore de backup isso é fácil de esquecer. (Importar
  atalhos age na hora.)
- **Arrastar um ícone para outro grupo em posição livre** usa o ponto da soltura como
  posição (antes levava as coordenadas do grupo de origem). Comprovado com o mouse real
  (ver "Arrastar um ícone ... para fora do próprio grupo" acima).
- **Menu abrindo para fora da tela ou no outro monitor perto das bordas**: relatado
  pelo Alexandre em 2026-09-30, **não reproduzido**. `GroupProbe --verify-lote
  --menu-edges` pôs um grupo em cada canto da área útil dos dois monitores (150% e
  100%) e mediu o menu do grupo, um submenu, um sub-submenu, o menu de um ícone com o
  submenu "Grupo ▸" e o menu do botão "…", além do ladrilho de pasta de apps no canto
  inferior direito: os 19 casos abriram inteiros dentro do monitor clicado e para o
  lado certo (à esquerda na borda direita, para cima no rodapé). À espera de um caso
  concreto (captura) para reproduzir. Observado no mesmo teste: ao pôr uma janela
  atravessando a divisa entre os monitores de escalas diferentes, o Windows a
  reposicionou sozinho (o app é "System DPI aware").
- **"Parametrizacao de acesso ao IGC"** não foi recuperado: não existe `.lnk` com
  esse nome em nenhum lugar procurado. Ele aparece com o triângulo de aviso até o
  Alexandre apontá-lo com "Localizar atalho perdido…".

## Testes e Sonda de Caracterização

O projeto conta com uma suíte automatizada de testes xUnit em `tests/SmartDockGroups.Tests`
(73 asserções cobrindo geometria de monitor `MonitorPlacement`, posicionamento de janelas por monitor
`PromptPositioning` com DPI heterogêneo, contenção na área de trabalho e abertura perto do cursor, integridade e contrato
de serialização de `config.json`, extração e corte de transparência de ícones no
`IconCacheService`, schema versionado `v3`, resolução unívoca de atalhos `.url` de área de trabalho e rejeição sob ambiguidade, renderização do ladrilho `AppFolderTile`,
e as regras do lote de 2026-09-30 em `GroupRulesTests`: nomes únicos, compartilhamento por aspecto, `GroupDefaults`
e recuperação de atalho perdido sem palpite; `DockTests`: pilha acoplada, restauração, persistência e verbos do menu
da área de trabalho; `NavigationAndLimitTests`: navegação por posição com voltas, limite de 30 e atalhos de grupo; `LocalizationCompletenessTests`: os oito idiomas com as mesmas chaves e marcadores, e toda chave usada pelo código existe) — 101 no total.

Além dos testes unitários, há a sonda de caracterização em C# (`tests/GroupProbe`) que sobe
a aplicação de verdade com um fixture de teste, valida backup por hash, mede a geometria
das janelas, gera hash SHA-256 das capturas e inspeciona a árvore de menus via UI Automation,
permitindo comparar com a linha de base via `compare_reports.py`. A aplicação mantém seu modo de DPI original (System DPI Aware sem manifesto), enquanto a sonda de teste `GroupProbe` conta com manifesto nativo `app.manifest` (`PerMonitorV2`) para medição física real e bridge automático para a desktop interativa
`WinSta0\Default`. **Desde 2026-09-30 a sonda roda o app numa pasta de dados isolada**
(`SMARTDOCKGROUPS_DATA_DIR`), ao lado da instância do usuário, sem trocar o `config.json` real;
`--legacy-real-config` mantém a troca com backup por hash para builds anteriores, que não conhecem a
variável. O relatório tem hoje 382 campos (menus reorganizados; item "Acoplar" e estado `Dock` desde a 1.1.3.0). A baseline `tests/baseline/report-master.json`
foi regenerada neste lote: o oráculo deu `IDENTICO` em duas rodadas do mesmo build, e a comparação
master (17dcd55) × branch divergiu só nos menus reorganizados, no `title` das janelas e na captura do
grupo "Alpha Panel" — onde "Mike Paint" (`mspaint.exe`, ausente nesta máquina) passou de espaço vazio
para o triângulo de aviso.

Há também o modo `--verify-lote` (`tests/GroupProbe/LoteVerifier.cs`), que roda o app sobre uma **cópia**
da configuração real (mesmos tipos de item) e percorre: reparo dos atalhos na inicialização, colar em grupo
livre (e colar de novo sem duplicar), rolagem, Ctrl+F, menus do grupo e do ícone, nome repetido, remover
grupo com atalhos, menus da folha do App Folder, novo grupo perto do mouse, compartilhar só a opacidade e
importar atalhos pelas Configurações. `--dock` roda só o acoplamento (acoplar, expandir, arrastar, reabrir,
desacoplar e os comandos com o app fechado); `--menu-edges` só a posição dos menus nas bordas; `--batch` a ordem do menu,
manter na tela (arrastes reais), navegação por teclas, limite de 30 e atalho de grupo.
`--clean-cache` troca o cenário "Upgrade" (cache de ícones copiado)
por "Instalação limpa". Passou inteiro no executável **publicado** self-contained 1.1.2.0 nos dois cenários.

> **Nota sobre PowerShell para diagnóstico Win32:** `FindWindow("Progman",
> $null)` em PowerShell retorna identificador vazio mesmo quando a janela
> existe — a marshalling do PowerShell não converte `$null` para `NULL` num
> parâmetro `string` de P/Invoke. Isso já produziu duas conclusões erradas
> nesta investigação (achar que o `Progman` não existia; achar que o
> `SetParent` entre processos não funcionava mais). Uma sonda escrita em C#
> puro deu a resposta certa nas duas vezes. Para qualquer diagnóstico Win32
> daqui para frente, escrever a sonda em C#, não em PowerShell.
