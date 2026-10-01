# Prompt de geração em um passo — Smart Dock Groups 1.1.5.2

> **Para que serve.** Entregue o texto da seção "O texto do prompt" (e os apêndices A a E, que fazem
> parte dele) a um agente de código, numa pasta vazia, numa única interação. O resultado deve ser
> o aplicativo **Smart Dock Groups 1.1.5.2**: mesmas telas, mesmos menus, mesmas regras, mesmos
> números, mesmos textos nos oito idiomas e o mesmo instalador.
>
> **Diferença para os outros documentos.** [`PROMPT.md`](PROMPT.md) é o *histórico* do pedido,
> cronológico, com as correções que foram acontecendo. [`OVERVIEW.md`](OVERVIEW.md) explica a
> arquitetura e o porquê das decisões. **Este arquivo é a especificação do zero**, já com as decisões
> finais, escrita para ser executada de uma vez.
>
> **Como foi escrito.** Todo valor, nome, regra e texto abaixo foi lido do código da 1.1.5.2, não de
> documentação anterior — e conferido por auditoria: cada ícone e cada chave de texto que o código usa
> está nos apêndices, e cada chave citada no texto existe nos arquivos de idioma. Os apêndices A, B, C e D são **gerados** dos
> arquivos reais por `tools/atualizar_apendices_prompt.py` — rode-o depois de mudar paleta, ícones
> ou textos. Regra de manutenção do projeto: mudou o comportamento, este arquivo muda no mesmo commit.
>
> **O que um prompt não consegue garantir.** Pixels idênticos e o tempo exato de animação dependem
> de decisões de implementação que o texto não cobre. O que está fixado aqui: dados, regras,
> estrutura, dimensões, cores, textos, comportamento e verificação. Quem receber este prompt deve
> tratar a seção 20 (como provar) como parte da entrega, não como sugestão.

---

## O texto do prompt

Crie, numa pasta vazia, um aplicativo para **Windows 10/11 x64** chamado **Smart Dock Groups**
(nome interno `SmartDockGroups`), em **C# / .NET 10 / WPF**, que organiza a área de trabalho em
**grupos de atalhos**, no espírito das pastas de app do Android: cada grupo é uma janela solta
sobre a área de trabalho, com ícones que se arrastam, e o app vive na bandeja do sistema.
Entregue o código, os testes, a documentação e um instalador que funcione de verdade.

### 0. Regras que valem para tudo

1. **Nunca use vermelho** — nem vermelho com amarelo, nem estrela amarela — em ícone, logo, seleção,
   selo ou qualquer arte gerada. Pergunte por outra paleta em vez de usar vermelho. (A paleta do
   apêndice A tem um par "Danger" por herança de outro produto; ele **não** pode aparecer na
   interface: mensagens de erro usam a cor de destaque.)
2. **Nenhum texto fixo na interface.** Tudo vem de arquivos de idioma (seção 18).
3. **A interface é prova, não opinião.** Uma função com tela só está pronta depois de abrir o app de
   verdade e usá-la (clique, tecla, captura de tela ou UI Automation). Teste de lógica não substitui.
4. **Sintoma que se repete** (ex.: coordenada corrompida em DPI misto) exige achar a causa comum antes
   de acrescentar mais um caso especial.
5. **Diagnóstico Win32 escreve-se em C#**, não em PowerShell (a marshalling do PowerShell converte
   `$null` em string vazia em P/Invoke).
6. **Medir antes de afirmar**: "ocupa X", "leva Y s", "não existe Z" exigem o comando que mediu.
7. **Não faça `git push`** sem autorização explícita. Commits locais são livres.
8. **Limite de 30 entradas por grupo** (seção 9).

### 1. Solução e arquivos

```
SmartDockGroups.slnx            (Core, App, Tests, GroupProbe)
VERSION                         "1.1.5.2" (sem quebra de linha) — única fonte da versão
NuGet.Config                    só ACRESCENTA mapeamentos ao nuget.org (seção 19)
AGENTS.md  CLAUDE.md            regras para agentes (seção 21)
docs/OVERVIEW.md  docs/PROMPT.md  docs/PROMPT_GERACAO_UNICA.md
src/Directory.Build.props       lê VERSION e aplica Version/AssemblyVersion/FileVersion/InformationalVersion
src/SmartDockGroups.Core        net10.0, SEM WPF e SEM Win32: modelos, regras e persistência
src/SmartDockGroups.App         net10.0-windows, WPF + WinForms (NotifyIcon, Screen), WinExe
tools/IconForge                 gera o .ico oficial (seção 17)
tests/SmartDockGroups.Tests     xUnit (seção 20)
tests/GroupProbe                sonda que sobe o app de verdade (seção 20)
tests/baseline  tests/compare_reports.py  tests/README.md
```

- App: `AssemblyTitle` e `Product` = `Smart Dock Groups`; `ApplicationIcon` = `Assets/SmartDockGroups.ico`;
  `Nullable` e `ImplicitUsings` ligados; recursos: `Localization/*.json` e o `.ico` como `Resource`.
  `InternalsVisibleTo("SmartDockGroups.Tests")`. `ShutdownMode=OnExplicitShutdown`.
- Core nunca referencia WPF nem Win32. Tudo que desenha ou chama o Windows fica no App.
- `.gitignore`: `bin/ obj/ *.user dist/ .vs/ .idea/ Thumbs.db tests/runs/`.

### 2. Dados (Core) — JSON em `%AppData%\SmartDockGroups\config.json`

Serialização `System.Text.Json`: indentado, **nulos omitidos**, enums como **texto**. Um arquivo
antigo com campo a menos ou a mais **abre sem erro**. `ConfigurationStore.Load()` devolve a
configuração padrão (e a grava) se o arquivo não existir ou estiver corrompido; `TryLoad` informa
sucesso; `Save` cria a pasta.

**Enums:** `LaunchItemType {Application, File, Folder, Url, Command, GroupLink}` ·
`ExecutionMode {Normal, Minimized, Administrator}` ·
`DesktopGroupDisplayMode {Panel, AppFolder}` ·
`IconArrangement {None, Grid, ByName, ByType}` (só `None` e `ByName` são usados; `Grid` e `ByType`
existem para ler configs antigas e valem como `ByName`) · `AppThemeMode {System, Light, Dark}` ·
`TrayClickMode {SingleClick, DoubleClick}` ·
`HotkeyModifiers [Flags] {None=0, Alt=1, Control=2, Shift=4, Windows=8}`.

**`LaunchItem`** (com `Clone()`): `Name`, `Type`, `Target`, `Arguments?`, `WorkingDirectory?`,
`ExecutionMode=Normal`, `IconOverridePath?`, `IsDesktopPinned=false`, `DesktopIconX?`, `DesktopIconY?`.
Para `Type=GroupLink`, `Target` é o `Id` do grupo apontado.

**`MenuCategory`** (grupo ou subpasta; implementa `IMenuContainer {Categories, Items}`; `Clone()`):
`Name`; `Id?` (GUID "N", sem hífens; nulo em config antiga — preenchido na carga e gravado; o clone
copia; "Duplicar" gera outro); `Categories`, `Items`; `ThemeOverride?` (`MenuTheme`);
`IsDesktopGroup`; `DesktopX=40`, `DesktopY=40`, `DesktopWidth=220`, `DesktopHeight=180`;
`DesktopIconScale=1.0`; `DesktopBackgroundImagePath?`; `IconX?`, `IconY?` (posição de subpasta);
`IsCollapsed`; `IsClosed`; `AreaOpacity=1.0`; `TitleOpacity=1.0`; `DisplayMode=Panel`;
`PanelX?`, `PanelY?` (onde o painel estava antes de virar ladrilho); `IconHGap=10`, `IconVGap=10`
(px antes do zoom, faixa 10–30); `IconArrangement=None`.
Compatibilidade: as chaves JSON antigas `AreaTransparent` e `TitleTransparent`, se `true`, viram
opacidade 0; nunca são regravadas (o getter devolve nulo).
`CopyVisualFrom(origem, aspectos, temaPadrão)` copia só os aspectos escolhidos (seção 12).

**`MenuTheme`** (com `Clone()`), padrões: `BackgroundColor="#1E1E1E"`, `Opacity=0.97`,
`BorderColor="#3C3C3C"`, `CornerRadius=6`, `ShowShadow=true`, `ShadowBlurRadius=12`, `ShadowDepth=2`,
`ShadowDirection=315`, `ShadowOpacity=0.35`, `ItemSpacing=2`, `ItemPadding=8`, `IconSize=18`,
`TextColor="#FFFFFF"`, `HighlightColor="#3D7EB8FF"` (ARGB, alfa 0x3D), `TitleFontFamily="Segoe UI"`,
`TitleFontSize=13`, `TitleBold=true`, `ItemFontFamily="Segoe UI"`, `ItemFontSize=13`,
`AnimationDurationMs=120`.

**`LauncherBehavior`** (com `Clone()`): `ClickMode=SingleClick`; atalho global do menu:
`GlobalHotkeyEnabled=false`, `GlobalHotkeyModifiers=Control|Shift`, `GlobalHotkeyKey="Space"`;
atalho de restaurar grupos: `RestoreGroupsHotkeyEnabled=true`,
`RestoreGroupsHotkeyModifiers=Windows|Control|Alt`, `RestoreGroupsHotkeyKey="D"`;
`AppTheme=System`; `Language=null` (= idioma do Windows).

**`LauncherConfiguration`** (implementa `IMenuContainer`): `Categories`, `Items`, `Theme` (`MenuTheme`),
`Behavior`, `GroupDefaults`, `Dock`. `Clone()`, `ReplaceContentsWith(outra)` e `CreateDefault()`:
um item "Bloco de Notas" (`notepad.exe`, Application) e uma categoria "Ferramentas do Windows"
(`HighlightColor="#3DFFA34D"`) com "Calculadora" (`calc.exe`) e "Paint" (`mspaint.exe`) — nenhuma é
grupo de área de trabalho, então um primeiro uso começa **sem janelas**; o usuário cria o primeiro
grupo pela bandeja.

**`GroupDefaults`** (o que um grupo *novo* herda além do tema padrão; nulo = sem preferência):
`BackgroundImagePath?`, `AreaOpacity?`, `TitleOpacity?`, `IconHGap?`, `IconVGap?`, `IconScale?`.
`TakeFrom(grupo, aspectos)` e `ApplyTo(grupo)`.

**`VisualAspects [Flags]`**: `BackgroundColor=1`, `BackgroundImage=2`, `Opacity=4`, `IconSpacing=8`,
`IconSize=16`, `All=63`.

**`DockState`** (seção 13): `IsDocked`, `Left`, `Top`, `Width`, `Order` (ids, de cima para baixo),
`ExpandedId?`, `Saved` (lista de `GroupPlacement`: `Id`, `X`, `Y`, `Width`, `Height`, `IsCollapsed`,
`DisplayMode`, `PanelX?`, `PanelY?`; `From(grupo)` e `ApplyTo(grupo)`).

**`GroupNames`** (estático): comparação **sem diferenciar maiúsculas nem espaços nas pontas**, atalhos
e subpastas num só espaço de nomes. `IsTaken(grupo, nome, exceto?)`; `MakeUnique(grupo, nome, exceto?)`
→ o próprio nome ou "Nome (2)", "Nome (3)"…; `FindSameShortcut(grupo, item)` (mesmo nome **e** mesmo
destino, destino sem diferenciar maiúsculas); `EnsureUnique(contêiner)` percorre tudo: o primeiro
mantém o nome e os seguintes ganham o número; devolve se mudou algo.

**`GroupLimits`** (estático): `MaxEntries=30`; `Count` = atalhos + subpastas; `Room` nunca negativo;
`CanHoldLinkTo(contêiner, idAlvo)` = não é o próprio grupo **e** ainda não tem `GroupLink` para esse
alvo; `CreateGroupLink(contêiner, alvo)` (nome = nome do grupo alvo tornado único, `IsDesktopPinned`);
`Admit(contêiner, entradas)` → (aceitos, acimaDoLimite, atalhosDeGrupoRecusados), preservando a ordem:
recusa primeiro o `GroupLink` proibido (inclusive um segundo para o mesmo alvo **dentro do mesmo
lote**), depois o que passa de `Room`.

**`TileNavigation`** (estático): `Rows(posições, tolerância)` agrupa por Y (ícones cujo Y difere
menos que a tolerância ficam na mesma linha; linhas por X crescente); `ReadingOrder`;
`Move(posições, atual, tecla, linhasPorPágina, tolerância)` com `NavigationKey {Left, Right, Up, Down,
PageUp, PageDown, Home, End}`: Esquerda/Direita andam na ordem de leitura **com volta** nas pontas;
Cima/Baixo vão à linha vizinha (com volta da primeira à última e vice-versa) escolhendo a coluna de X
mais próximo; PageUp/PageDown saltam `linhasPorPágina` (mínimo 1) e, estando na linha extrema, vão à
oposta; Home/End vão ao primeiro/último; sem seleção (`-1`), teclas "para trás" (Esquerda, Cima,
PageUp, End) escolhem o último e as demais o primeiro.

**`DockLayout.Arrange(membros, expandidoId, topo, topoÚtil, fundoÚtil)`** — função pura. Cada membro:
`(Id, AlturaBarra, AlturaPreferida)`. Todos ficam na altura da barra, exceto o expandido, que recebe
`max(AlturaBarra, min(max(AlturaPreferida, 120), espaçoRestante))`. Se nem as barras (mais 120 para o
expandido) cabem abaixo de `topo`, a pilha inteira sobe — nunca acima de `topoÚtil`. Devolve o topo
usado e um `DockSlot(Id, Top, Height, Expanded)` por membro. `MinExpandedHeight=120`.

### 3. Abertura, reparo e instância única (App)

**Pasta de dados.** `%AppData%\SmartDockGroups` (`config.json`, `IconCache\`, `Shortcuts\`).
A variável de ambiente **`SMARTDOCKGROUPS_DATA_DIR`** aponta tudo para outra pasta; uma instância
assim é **isolada**: mutex e pipe com sufixo derivado do caminho (12 primeiros hex do SHA-256 do
caminho em minúsculas), **não** registra o menu da área de trabalho, e guarda os atalhos de grupo em
`<dados>\StartMenu`. É assim que se testa o app ao lado do app real, sem tocar nos dados nem no registro.

**Instância única.** `Mutex` nomeado `SmartDockGroups.SingleInstance` decide a primeira instância;
as seguintes enviam a ação recebida (`--desktop-action=<ação>`) por **pipe nomeado**
`SmartDockGroups.DesktopAction` (meio segundo no máximo) e saem sem janela nem ícone de bandeja.

**Ordem da inicialização:** instância única → carregar config → `EnsureIds` → **reparo**
(`ShortcutStore.AdoptAndRecoverAll` + `GroupNames.EnsureUnique`; se mudou algo, **copia datada do
config** `config.json.before-repair-aaaaMMdd-HHmmss.bak` ao lado e grava) → cache de ícones → tema do
app e idioma → janela-âncora invisível (0×0 fora da tela) para atalhos globais → atalhos globais →
grupos → ícone da bandeja → registro do menu da área de trabalho (se não isolado) → escuta do pipe →
ação de linha de comando, se houver, executada em `ApplicationIdle` (depois dos grupos já
mostrados e dispostos). Sem janela principal. Fechar o app encerra todas as janelas de grupo.

**`ShortcutStore`.** Um `.lnk`/`.url` que entra num grupo é **adotado**: copiado para
`%AppData%\SmartDockGroups\Shortcuts` (nome, ou "nome (2)"; o mesmo arquivo adotado duas vezes
reaproveita a cópia). Motivo medido: o Windows apaga o `.lnk` de `...\Quick Launch\User Pinned\TaskBar`
quando o app é desafixado, e o grupo só guardava o caminho. Na inicialização, para cada item cujo
destino é `.lnk`/`.url`: se existe (ou se resolve pela regra de nome parecido), adota; se não,
**recupera** procurando recursivamente em Menu Iniciar (usuário e todos), área de trabalho (usuário e
pública) e `Quick Launch`, ignorando "Uninstall…", por esta ordem: (a) mesmo nome de arquivo; (b)
nome do item; (c) prefixo (nome com ≥4 letras que começa o outro seguido de espaço). Várias
correspondências só valem se todas abrem o **mesmo programa** (lido via `IShellLink`: caminho +
argumentos); senão, não recupera — nunca por palpite. Item que não recupera continua e aparece
com aviso (seção 6). Os arquivos adotados nunca são apagados pelo app.

### 4. Serviços do Windows (App)

- **`IconCacheService`** (seção 5), **`ShellCommands`**: `HasFileTarget`, `TryResolveTarget` (nunca
  para `Command`/`Url`), `RevealInExplorer` (`explorer /select,"…"`, ou abre a pasta),
  `RunAsAdministrator` (`runas`), `CopyPath`, `ShowProperties` (`ShellExecuteEx`, verbo
  `properties`). `ResolveTarget`: arquivo/pasta existente; caminho absoluto inexistente → procura na
  pasta `nome*extensão` e **só aceita se houver exatamente um candidato**; nome sem caminho → `PATH`.
- **`ShellContextMenu`**: hospeda o `IContextMenu` real do Explorer (extensões de terceiros, submenus),
  aberto pelo despachante em `ApplicationIdle` depois que o menu temático fechou; **Shift** mostra os
  verbos estendidos (`CMF_EXTENDEDVERBS`). É desenhado pelo Windows, não pelo tema — inerente.
- **`LaunchExecutor.Execute(item)`**: `Process.Start` com shell; `Command` roda `cmd.exe /c <alvo> [args]`;
  `WorkingDirectory`; `Minimized` → janela minimizada; `Administrator` → `runas`; falhas de
  `Win32Exception`/`FileNotFoundException` são normais e silenciosas. Um `GroupLink` não passa por
  aqui: o organizador o desvia para "focar o grupo" (seção 12).
- **`StartupRegistration`**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `SmartDockGroups`
  = caminho do exe; "ligado" só se o valor aponta para o exe em execução.
- **`WallpaperService`**: `SystemParametersInfo(SPI_GETDESKWALLPAPER)`; se vazio/inexistente, usa
  `%AppData%\Microsoft\Windows\Themes\TranscodedWallpaper`; senão, nulo.
- **`GlobalHotkeyService`**: `RegisterHotKey` na janela-âncora, ids `0xA1F3` (menu) e `0xA1F4`
  (restaurar grupos); `Apply(behavior)` desregistra e registra de novo. A tecla é o nome de
  `System.Windows.Input.Key` (Space, Tab, A–Z).
  *Por que o segundo existe:* "Mostrar área de trabalho"/Win+M escondem janelas sem botão na barra e o
  Windows não as devolve; o atalho de restaurar chama `RestoreIfMinimized` em todos os grupos.
- **`DesktopContextMenuRegistration`**: submenu real em
  `HKCU\Software\Classes\DesktopBackground\Shell\SmartDockGroups` (`MUIVerb`, `Icon`, `SubCommands`
  vazio) com **11 verbos** em `shell\<NN><Nome>\command` = `"<exe>" --desktop-action=<ação>`:
  `01NewGroup new-group`, `02OpenAllGroups open-all-groups`, `03CloseAllGroups close-all-groups`,
  `04BringAllToFront bring-all-to-front`, `05SendAllToBack send-all-to-back`,
  `06ToggleCollapseAll toggle-collapse-all`, `07DockAll dock-all`, `08UndockAll undock-all`,
  `09AllAppFolder all-appfolder`, `10AllPanel all-panel`, `11OpenSettings open-settings`, com os
  rótulos das chaves `tray.newDesktopGroup`, `desktop.openAllGroups`, `desktop.closeAllGroups`,
  `group.bringAllToFront`, `group.sendAllToBack`, `desktop.toggleCollapseAll`, `group.dockAll`,
  `group.undockAll`, `desktop.allAppFolder`, `desktop.allPanel`, `tray.settings`. Reescreve a subárvore
  `shell` **a cada inicialização** (idempotente; verbos antigos não ficam). Os rótulos seguem o
  **idioma do Windows** (`DetectLanguage` + `GetForLanguage`), nunca o idioma configurado no app, pois
  quem lê o menu é o Explorer, possivelmente com o app fechado. **Nenhum hook de mouse global**
  (`WH_MOUSE_LL` já travou o Windows). Ação extra `focus-group:<id>` (atalhos de grupo).
- **`TaskbarShortcutService`** (seção 15) e **`SingleInstanceCoordinator`**.

### 5. Ícones

`IconCacheService` produz a imagem de cada destino. **Cache em disco em PNG** (nunca `.ico`: achata o
canal alfa e deixa halo preto/branco) em `IconCache\<SHA-256>.png`; chave =
`"v3:" + destino-em-minúsculas + "|" + ticks-da-última-gravação-do-arquivo-resolvido (0 se não houver
arquivo)`. O prefixo de versão sobe sempre que o algoritmo de extração muda (v3 = atalhos que só usam o
ícone do programa passaram a ser desenhados a partir do programa).

Extração, na ordem: se o destino é um `.url`, lê `URL=` e extrai pelo protocolo; senão, lista de imagens
do shell **jumbo (256 px)**, depois **extra-grande**, depois `Icon.ExtractAssociatedIcon`. Destinos
que são URIs (`msteams:`, `ms-settings:`, `http:`, `https:`, `shell:AppsFolder\…`): `AssocQueryString`
→ ícone padrão do protocolo (`ASSOCSTR_DEFAULTICON`, inclusive referências indiretas `@{…}` via
`SHLoadIndirectString`), depois o executável associado, depois `IShellItemImageFactory` (256×256,
`ICONONLY`). **Corte de margens transparentes** (`TrimTransparentMargins`: alfa > 10; se já ocupa
≥90% do quadro e está centrado, mantém) — o jumbo devolve ícones pequenos ancorados no canto de um
canvas 256×256 transparente. Resolução de caminho com nome renomeado: só quando há **exatamente um**
candidato `nome*extensão` na pasta.

**Atalho que só diz "use o ícone do programa"** (`.lnk` com local de ícone vazio): o shell devolve uma
folha em branco para alguns deles (medido com o SSMS 22 a 150% de DPI, em todas as APIs). Então o ícone
é o do **programa de destino** com a **seta de atalho** do usuário por cima (chave
`HKLM\…\Explorer\Shell Icons` valor `29`; padrão `imageres.dll,-163`; `SHDefExtractIcon`) —
`IconSourceForTargetIcon` lê o `.lnk` via `IShellLink`.

`GetIcon` (painel, `System.Drawing.Icon`) e `GetImageSource` (WPF, congelada, compartilhável entre
janelas). Caches em memória por destino. `Dispose` libera os ícones.

### 6. A janela de um grupo (`DesktopGroupWindow`)

Janela WPF **sem borda**, `AllowsTransparency`, `ShowInTaskbar=false`, `ResizeMode=NoResize`, fora da
camada "sempre no topo", `Title` = nome do grupo (nunca desenhado). Posição/tamanho vêm do grupo; ao
mover/redimensionar, grava (movimentos externos, como Win+Shift+setas, são gravados com atraso de 400 ms
e **só** se não foram feitos pelo próprio app). Um registro estático de todas as janelas de grupo
permite achar qual está sob o ponto de uma soltura.

**Aparência (vem do tema do grupo ou do global):** borda de 1 px e cantos `CornerRadius`; a cor da borda
é derivada do fundo — 10% mais clara, ou 10% mais escura se o fundo tem luminância > 0,9 (luminância =
0,299R+0,587G+0,114B); fundo = `BackgroundColor` com alfa `Opacity × AreaOpacity` (ou
**imagem** `UniformToFill` com opacidade `AreaOpacity`, quando há caminho válido; ou transparente se
`AreaOpacity ≤ 0`); **sombra** `DropShadowEffect` (blur, profundidade, direção, opacidade do tema, cor
preta) se `ShowShadow`. Cabeçalho: faixa com `HighlightColor` na opacidade `TitleOpacity`, texto do
nome (`TitleFontFamily`/`TitleFontSize`/negrito do tema, `TextColor`, reticências), e à direita, nesta
ordem a partir da borda: **✕ fechar**, **⋯ menu**, **chevron** (colapsar/expandir) — botões quadrados
de `TitleFontSize+14`, cantos 4, destaque `TextColor` a 14% no mouse.

**Título com a seleção:** sem seleção, só o nome; com um ícone selecionado, `Nome [Ícone]`; com vários,
`Nome [N]`. Atualiza a cada mudança de seleção.

**Ícone de atalho (tile):** placa `Border` de **72 px de largura** (`TileSize 80 − 8`), **cantos de
raio 10** (é a placa que recebe o destaque de seleção/mouse/busca, por isso arredondada), conteúdo em
coluna: imagem do ícone de `IconSize × 1.6` px (centralizada) e, abaixo, o nome (`ItemFontFamily`/
`ItemFontSize`, centralizado, quebra de linha, sombra de texto: blur 4, profundidade 1, direção 315,
opacidade 0,85, preto). **Cor do texto do nome é derivada, não do tema**: branca se há imagem de fundo
ou se `Opacity × AreaOpacity < 0,5`; senão preto/branco por contraste com o fundo (luminância > 0,55 →
preto). Subpasta: glifo de pasta (Segoe MDL2 Assets). **GroupLink**: ícone `IconStylePanel` da seção
13. **Destino que não existe mais**: nunca espaço vazio — glifo de aviso (Segoe MDL2 `E7BA`, 75% de
opacidade) e dica com o caminho e a orientação para "Localizar atalho perdido…".
Efeitos: mouse por cima → escala 1,08 (120 ms, `QuadraticEase`) e fundo `HighlightColor` a 45%;
selecionado → `HighlightColor` a 100%.
Coordenadas dos ícones: no espaço **antes do zoom**; passo horizontal = `80 + IconHGap`, vertical =
`80 + IconVGap`; margem do canvas = `max(8, 5% da largura/altura do grupo)`. Item sem posição salva
usa grade de 3 colunas.

**Rolagem.** O canvas dos ícones fica num `ScrollViewer` (barras automáticas) e o zoom é
`LayoutTransform` (não `RenderTransform`) para a rolagem enxergar o tamanho real. O canvas é dimensionado
até o ícone mais distante e **nunca menor que a área visível** (o vazio continua recebendo clique,
retângulo de seleção e menu); usa o espaço que o próprio `ScrollViewer` informa (`ViewportWidth/Height`, menos 1 DIP de folga para
arredondamento; refeito em `ScrollChanged` quando uma barra aparece ou some) — prever pelas medidas do sistema
deixava uma fração de pixel passar do visível e aparecia uma barra horizontal inútil em alguns DPIs. Roda do mouse rola; **Ctrl+roda**
é zoom (`DesktopIconScale`, passos de 0,1, faixa 0,5–3,0); o quadradinho onde as duas barras se
encontram é transparente. Setas, seleção e o resultado da busca rolam até o ícone.

**Colapsar:** só a barra de título (altura do conteúdo). **Redimensionar** por qualquer borda ou canto:
8 tiras invisíveis (espessura 6, cantos 14), cada uma com seu cursor e a borda oposta fixa; mínimo
140 × 120; ao soltar, regrava e reorganiza. Tudo desligado quando o grupo está acoplado.

**Seleção (estilo Explorer):** clique, Ctrl+clique (alterna), Shift+clique (intervalo **pela ordem
visual**), **retângulo de seleção** arrastando no fundo (com Ctrl, inverte), Ctrl+A. Clicar num item já
selecionado sem arrastar reduz a seleção a ele ao soltar (comportamento do Explorer). Duplo clique abre.

**Teclado (só no modo painel):** setas, Page Up/Down, Home, End — `TileNavigation` pela **posição na
tela**, sem começo nem fim; Shift estende o intervalo; `Enter` abre os selecionados; `F2` renomeia (um
selecionado); `Delete` remove (com confirmação); `F5` reaplica a organização; `Esc` limpa a seleção;
**digitar** (sem Ctrl+F) salta para o primeiro ícone cujo nome *começa* com o texto, acumulando
caracteres digitados com ≤1 s entre eles; Ctrl+C/X/V; **Ctrl+F** busca; **Win+Shift+←/→** leva o grupo ao
monitor vizinho (lembra a posição em cada monitor). `PageUp/Down` saltam as linhas visíveis.

**Ctrl+F (busca):** faixa sob o título com ícone de lupa, caixa (placeholder "<group.searchPlaceholder> (Esc)"),
contador (`group.searchCount`) e ✕; lista suspensa (`Popup`+`ListBox`, até 240 px) navegável com ↑/↓,
`Enter` abre como o duplo clique e fecha. **Os ícones respondem**: os que batem ganham fundo de destaque
a 50%, o que o Enter abriria ganha destaque cheio e é rolado para a vista, os demais ficam com opacidade
0,22; o trecho buscado fica em negrito e com fundo dentro do nome. Fecha com Esc, ✕, clique no fundo ou
perda de foco; o texto é lembrado **só** depois de Enter ou Esc.

**Área de transferência real:** Ctrl+C/X põem os *arquivos* dos ícones selecionados (`CF_HDROP` +
`Preferred DropEffect`: 5 copiar, 2 recortar); subpastas ficam de fora. Ctrl+X **não remove na hora**: o
ícone fica a 50% de opacidade e só sai quando o colar acontece (em outro grupo do app: na hora; no
Explorer: *polling* a cada 2 s até o arquivo sumir da origem). Ctrl+V aceita arquivos copiados em
qualquer lugar do Windows. Arrastar arquivos do Explorer para o grupo faz o mesmo.

**Entrada de atalhos (`AddIncomingPaths`) — um só caminho** para soltar, colar, importar:
adotar o `.lnk`/`.url` (seção 3); descartar o que já está lá (mesmo nome e destino); respeitar o limite
(seção 9); nome repetido ganha " (2)"; sem ponto de soltura, cada item vai para a **primeira célula
livre** da grade em ordem de leitura (não empilhados); com ponto, no ponto (+16 px por item); no fim
repinta, grava e seleciona os novos. Tipo inferido: pasta → `Folder`; `.exe`/`.lnk` → `Application`;
senão `File`.

**Repintar e gravar** acontece sempre que o conteúdo muda (`FinishStructuralChange`), nos dois modos de
organização (um erro antigo só repintava no modo automático: o atalho colado em grupo de posição livre
só aparecia depois de ordenar/redimensionar). Atualiza também a folha aberta do App Folder.

**Organização automática:** item "Organizar ícones automaticamente" (marcável). **Não existe "Ordenar
por"**: ligada, a ordem é **sempre por nome** (`CurrentCultureIgnoreCase`), subpastas antes, em grade;
colunas = `max(1, (largura − 2×margem) / (passo horizontal × zoom))`. É **viva**: reaplicada ao soltar,
colar, apagar, renomear, redimensionar e mudar o zoom. Com ela ligada, mover um ícone à mão o devolve
ao lugar. Desligada (`None`), a posição é livre e persiste.

**Arrastar ícones (`DragGhostWindow`):** janela-fantasma sem borda, *click-through*, sempre no topo,
posicionada em **pixels físicos** por `SetWindowPos`, com uma imagem do tile (opacidade 0,92; se não der para capturar, um `VisualBrush`); com **vários**
itens, um cartão semitransparente (fundo `#55364153`, borda `#99647488`, raio 6, opacidade 0,70)
deslocado 10 px atrás, como pilha; leve batimento depois de 300 ms parado; o tile de origem fica a 35%. O cursor é
lido por `GetCursorPos` (a janela de origem nunca se move durante o arrasto; `PointToScreen` só é chamado
sobre ela, nunca sobre a fantasma — chamá-lo duas vezes por evento em DPI misto corrompeu coordenadas).
O grupo de destino vem de `WindowFromPoint` e, na falta, da interseção da fantasma. Soltar no **mesmo**
grupo reordena/reposiciona (ponto de soltura menos o deslocamento de onde se pegou o ícone). Soltar em
**outro** grupo move de verdade (sai da origem, entra no destino; em posição livre usa o ponto da soltura;
nomes e limite do destino valem — o que não cabe ou é recusado **fica na origem**). Vários selecionados
arrastam juntos.

### 7. App Folder

`DisplayMode=AppFolder` troca, **na mesma janela**, o painel por um **ladrilho** (alternar é trocar
visibilidade, sem reconstruir — assim volta ao mesmo tamanho/posição; `PanelX/PanelY` guardam onde o
painel estava). Ladrilho (`AppFolderTile`, desenhado 1,4× quando é o ícone do grupo): placa 92 px (raio
26%), preenchimento interno de 11%, fundo em gradiente vertical da cor do fundo (alfa
`Opacity × AreaOpacity`) à mesma cor × 0,75, sombra do tema, **mosaico 3×3** dos primeiros 9 ícones
(células com margem 1,5; glifo para o que não tem imagem), e abaixo o nome (fonte do item × escala,
`SemiBold`, até placa+24 de largura, sombra). **Sem selo de contagem** (foi removido). O ladrilho
arrasta com `DragMove()` (não com contas de `PointToScreen`); um toque (movimento < 5 px) abre a folha.

**Folha (`GroupOverlayWindow`):** translúcida, centralizada na área útil do monitor do grupo, tamanho
`min(2× largura/altura do painel, 60%/50% da área útil)` (mínimo 360×260), nunca tela cheia; fundo = cor
do tema com alfa 0xD2; título 26 pt negrito; grade de ladrilhos 118×128 (ícone de 56); anima entrada
(opacidade e escala 0,94→1) e saída. Navega por subpastas **sem abrir janelas** (ladrilho "Voltar"; Esc
sobe um nível e só fecha na raiz). Um clique **seleciona**; duplo clique/Enter abre. Seleção múltipla,
Ctrl+A, Ctrl+C/X, F2, Delete como no painel. Digitar salta como no painel. **Fecha ao perder o foco —
exceto** quando quem tomou o foco é um diálogo do próprio app (renomear, "remover?", seletor de arquivo):
a decisão é tomada depois que a nova janela da frente existe (`ForegroundIsOwnDialog`). **Usa os mesmos
menus temáticos do painel**: clique direito num ícone = menu do ícone; no fundo = menu do grupo; renomear
e remover **gravam**. Acoplado, o grupo vira sempre barra de título e a folha fecha.

### 8. Menus (temáticos, construídos só no clique direito)

Todo menu é montado **no momento do clique direito** (ou do botão ⋯), nunca na abertura: montar um menu
completo para cada ícone na inicialização foi a maior parte do tempo de abertura (medido: 4,6 s → 2,8 s
na primeira abertura com 35 atalhos).

**Aparência dos menus:** `ContextMenu` e `MenuItem` com estilos próprios (`OverridesDefaultStyle`):
painel arredondado (`CornerRadius` do tema, borda do tema, preenchimento 4), linha com coluna de ícone
sempre reservada (para os textos alinharem), cabeçalho, seta de submenu (`M0,0 L4,4 L0,8`), submenu em
`Popup` à direita, realce de linha com `HighlightColor` e cantos 4. O texto **segue o `Foreground` do
próprio item** (um estilo local de `TextBlock` dentro do `ContentPresenter`; sem isso o estilo global
de texto do app, que muda com o tema claro/escuro, vence a cor do grupo e o texto sai quase preto sobre
menu escuro — bug real). Ícones de linha do apêndice B, traço 1,7, na cor de texto a 85%; item marcado
usa `IconCheck` na coluna do ícone, nunca um caractere no texto; reticências (`…`) só em comandos que
pedem mais informação. Separador: 1 px na cor da borda, margem 6,4. Sliders dentro de menu (opacidade
0–100%; espaçamento 10–30 px) mantêm o menu aberto, aplicam ao vivo e só gravam ao soltar.

**Menu do grupo** (cabeçalho, ⋯, fundo do painel, ladrilho, fundo da folha e submenu "Grupo «nome» ▸" de
cada ícone), nesta ordem — seções separadas por linha:

1. **Novo ▸** — *Atalho…* · *Grupo…* · *Atalho para um grupo ▸* (lista os outros grupos; os já ligados
   ficam desabilitados; desabilitado se não há outro grupo). Depois *Importar atalhos…* e *Colar* (Ctrl+V;
   desabilitado sem arquivos na área de transferência nem recorte pendente).
2. **Exibição ▸** — *Estilo: painel / pasta de apps* (acoplado, troca o estilo que o grupo terá ao desacoplar; o rótulo mostra esse estilo) · *Organizar ícones
   automaticamente* (marcável) · *Tamanho do ícone ▸* (Pequeno 0,75 / Médio 1,0 / Grande 1,5, marcável) ·
   *Espaçamento entre ícones ▸* (sliders Horizontal e Vertical).
3. **Aparência ▸** — *Cor de fundo…* (`ColorDialog`; ajusta também a cor do texto por contraste) ·
   *Imagem de fundo…* · *Usar papel de parede como fundo* · *Remover imagem de fundo* (desabilitado sem
   imagem) · *Opacidade ▸* (Área, Barra de título) · *Compartilhar com os outros grupos ▸* (seção 12).
4. **Ordem na área de trabalho ▸** — trazer todos para frente · enviar os outros para trás · enviar todos
   para trás · *trazer todos os grupos para este monitor centralizados* · um item por monitor
   ("Enviar todos os grupos para o Monitor N (Principal, LxA)"). E *Criar atalho na barra de tarefas*.
5. **Uma seção só para o grupo:** *Renomear…* · *Colapsar/Expandir* (no App Folder: *Abrir pasta*) ·
   *Acoplar todos os grupos / Desacoplar grupos* · *Duplicar grupo* · *Fechar grupo* · *Remover grupo…*.
6. **Configurações… — sempre por último.**

**Menu de um ícone** (`BuildEntryMenu`, o mesmo no painel e na folha; age sobre a seleção inteira se o
ícone clicado faz parte dela; clicar com o direito num ícone fora da seleção o seleciona primeiro):
*Abrir* · *Executar como administrador* · *Abrir local do arquivo* | *Recortar Ctrl+X* · *Copiar Ctrl+C* ·
*Copiar como caminho* | *Renomear… F2* · *Localizar atalho perdido…* (só se o destino sumiu) · *Remover do
grupo Del* | *Propriedades* · *Menu padrão* (o do Explorer) | *Grupo «nome» ▸* (o menu do grupo acima) ·
*Novo grupo…*. Os itens de arquivo só aparecem se o destino é arquivo/pasta real.

**Menu da bandeja** (`NotifyIcon` com o ícone do app no tamanho de ícone pequeno do sistema): clique
simples ou duplo conforme `ClickMode` abre; clique direito sempre abre. Itens: *Novo grupo no desktop…* ·
*Grupos da área de trabalho ▸* (abrir todos, fechar todos, e um item **marcável por grupo** que abre/fecha) ·
*Expandir/colapsar todos* · *Reunir grupos no centro* · *Trazer todos para frente* · *Mandar todos para o
fundo* · *Acoplar / Desacoplar* (esses cinco desabilitados sem grupos abertos) | *Iniciar com o Windows*
(marcável) · *Configurações…* | *Sair*. Abre na posição do cursor (convertida para DIPs); anima opacidade em
`AnimationDurationMs`.

### 9. Regras de conteúdo

- **Nomes únicos por grupo** (seção 2): digitando, o diálogo **recusa com o motivo** sob a caixa e fica
  aberto; colar/soltar/mover/importar numeram; configurações antigas e JSON importado são normalizados.
- **No máximo 30 entradas por grupo** (atalhos + subpastas). Colar, soltar, importar, criar e mover de
  outro grupo passam pelo mesmo teste: o que cabe entra, o resto **fica onde estava** (um arraste
  recusado não perde nada) e um aviso diz o limite, quantos não entraram e que o ideal é criar um novo
  grupo. Um grupo que já passava de 30 numa config antiga fica como está, mas não aceita mais nada.
- **Remover grupo vale para qualquer grupo**, não só o vazio. Com atalhos dentro, o aviso diz **quantos**
  serão perdidos e o botão padrão é **Não**. O mesmo para subpasta com conteúdo.
- **Atalho para um grupo** (`GroupLink`): vem só de *Novo ▸ Atalho para um grupo ▸* (o editor de item não
  lista esse tipo). Nunca para o próprio grupo, nunca dois para o mesmo (vale também ao arrastar de outro
  grupo; recusa com aviso). Clicar: abre o grupo se estava fechado, traz para frente (acoplado: abre o
  dele na pilha), pulsa a opacidade duas vezes e **seleciona o primeiro ícone na ordem visual**. O nome
  acompanha o do grupo (renomear atualiza todos os atalhos para ele) e **apagar o grupo apaga os
  atalhos para ele**. O atalho da barra de tarefas usa o mesmo caminho.
- **Importar atalhos** (menu do grupo e Configurações): seletor de arquivos de **seleção múltipla**, com
  `DereferenceLinks=false` (traz o próprio `.lnk`, com argumentos e ícone), filtro "Atalhos e programas
  (*.lnk;*.url;*.exe)|…|Todos"; pelas Configurações pergunta o grupo de destino (ou o nome de um grupo
  novo, criado perto do mouse com os padrões de grupo novo) e avisa quantos entraram.

### 10. Vários monitores e DPI

O app é **ciente do DPI do sistema** (sem manifesto de DPI por monitor). Com monitores de escalas
diferentes o espaço de coordenadas das janelas **não é uniforme** (ex.: monitor 1 a 150% → DIP = px/1,5;
monitor 2 a 100% → DIP = px), e `Screen.AllScreens`/`GetCursorPos` do processo vivem em espaços
diferentes entre si. Consequências obrigatórias:

- `DisplayInventory.WorkAreas(visual)` converte `Screen.WorkingArea` para DIPs pelo
  `TransformFromDevice` da janela — é o espaço onde `Window.Left/Top` vivem.
- **Qual monitor está sob o mouse** pergunta-se a `MonitorFromPoint`/`GetMonitorInfo` chamados sob
  `SetThreadDpiAwarenessContext(PER_MONITOR_AWARE_V2)` (restaurando o contexto depois), e o nome do
  dispositivo é casado com `Screen.AllScreens`. Medido: com o mouse a x=3915 no segundo monitor, o
  processo (sistema-DPI) resolvia o ponto para o primeiro.
- `MonitorPlacement` (puro, testável): `IsReachable(grupo, áreas)` = a **faixa de título** (32 px de
  altura, ou a do grupo se menor) sobrepõe alguma área em ao menos 48 px de largura e 16 de altura (só
  encostar num canto não vale); `IndexOfOwner` (maior interseção; sem interseção, o centro mais próximo;
  −1 só sem áreas); `AdjacentIndex(de, áreas, ±1)` (monitores ordenados pelo centro horizontal, **dando a
  volta** nas pontas, como o Win+Shift+seta do Windows; nulo com um monitor); `MapBetween` (mantém a posição
  relativa e tudo dentro); `ClampInto` (se o grupo é maior que a área, fica com o canto superior esquerdo);
  `AvoidStacking` (passo 28, até 12 tentativas); `ArrangeCentered(tamanhos, área, gap=20)`.
- **Um grupo nunca fica fora de um monitor** (`KeepOnScreen`): ao soltar o arrasto do título ou do
  ladrilho, ao expandir e ao redimensionar, os **quatro lados** voltam para dentro da área útil. Se a
  soltura deixa o grupo entre dois monitores, ele vai para o monitor **onde estava o mouse**. A barra
  de tarefas pode estar em qualquer lado: use sempre a área útil, nunca a tela inteira.
- **Resgate:** `DesktopOrganizerService` observa `SystemEvents.DisplaySettingsChanged`, segura as
  gravações de posição por 3 s (o Windows embaralha janelas durante a troca) e, depois de 1,5 s de
  estabilização, leva ao monitor mais próximo o grupo cujo "lar" (posição gravada) ficou fora de todos —
  mantendo a posição relativa quando conhece o layout anterior — **sem gravar** a posição de resgate,
  para o grupo voltar ao lar quando o monitor voltar.
- Diálogos pequenos (nome, renomear, importar) abrem **perto do mouse** (centralizados na horizontal sob
  o ponteiro, 12 px abaixo, empurrados para dentro da área útil do monitor do cursor); um grupo novo nasce
  ali também. Posicionamento por `SetWindowPos` em pixels físicos.

### 11. Ordem das janelas

Todos os grupos vivem na camada da área de trabalho (abaixo das janelas comuns). "Trazer todos para
frente" / "mandar para trás" usam `SetWindowPos` (`HWND_TOP`/`HWND_BOTTOM`, sem ativar). "Mostrar área de
trabalho" não minimiza essas janelas, só as cobre: `RestoreIfMinimized` alterna `Topmost` para trazê-las
de volta. "Reunir no centro" empilha em cascata (24 px) no centro da área útil.

### 12. Aparência: compartilhar e padrões

*Compartilhar com os outros grupos ▸* → *Aplicar a todos os grupos agora ▸* e *Usar como padrão para novos
grupos ▸*, cada um com: **Aparência completa · Só a cor de fundo · Só a imagem de fundo · Só a
opacidade · Só o espaçamento entre ícones · Só o tamanho do ícone**; e *Usar papel de parede em todos os
grupos*. Só o aspecto escolhido se move (compartilhar a imagem não impõe a cor, e vice-versa; "cor" leva
junto a cor de texto derivada). Como padrão: a cor vai para o tema padrão (e antes disso cada grupo que
ainda usava o tema padrão recebe uma cópia dele, para só os grupos *criados depois* mudarem); os outros
aspectos vão para `GroupDefaults`, aplicados em todo grupo novo (inclusive o de "Importar atalhos").

### 13. Acoplar todos os grupos

*Acoplar todos os grupos* empilha os grupos **abertos** numa coluna, **fechados**, a partir da posição e
com a largura (≥160) do grupo clicado (ou do mais acima/à esquerda quando vem da bandeja/área de
trabalho); ordem pela posição atual (Y, depois X). **Antes de acoplar, guarda por grupo** posição,
tamanho, aberto/fechado, estilo e posição do painel (`GroupPlacement`). Acoplado, tudo vai ao `config.json`
(reabrir o app volta acoplado).
- Cada membro vira um painel de altura de barra de título; um grupo em pasta de apps vira barra.
- A **seta de expandir** de um membro abre **só ele** (o que estava aberto fecha) e os de baixo deslizam
  para baixo (animação do `Top`, 1,4 × `AnimationDurationMs`, `QuadraticEase`); fechar puxa de volta;
  **nunca dois abertos**. O aberto recebe a altura que tinha antes de acoplar, cortada ao que cabe até o
  rodapé da área útil; se nem as barras cabem, a pilha sobe (seção 2, `DockLayout`).
- *Expandir/colapsar todos* (menu/bandeja/área de trabalho) fecha o aberto. Redimensionar fica desligado; trocar o
  estilo (Exibição ▸) continua valendo e muda o estilo guardado, aplicado ao desacoplar.
- **Arrastar o título de qualquer membro move a pilha inteira, ao vivo**; ao soltar, `KeepOnScreen`
  aplica-se e a pilha regrava. Os comandos "todos para este monitor / para o monitor N / reunir" movem a pilha.
- Grupos abertos/criados durante o acoplamento entram no fim (aparência guardada antes); fechar um tira-o
  da pilha sem deixar buraco; grupos fechados mantêm lugar e aparência guardados.
- **Desacoplar** devolve cada grupo exatamente ao que foi guardado.

### 14. Configurações e diálogos

- **Janela de Configurações** (`SettingsWindow`, janela com moldura própria, 640 de largura, altura pelo
  conteúdo, centralizada, não maximiza): **Aparência** (Tema: Seguir o Windows/Claro/Escuro; Idioma:
  Automático + os 8) · **Comportamento** (Abrir menu com: clique simples/duplo; atalho global do menu com
  Ctrl/Alt/Shift/Win + tecla; "Restaurar grupos ocultos" com os mesmos controles e a explicação) ·
  **Importar e exportar** (*Importar atalhos…*, *Importar configuração (JSON)…*, *Exportar configuração
  (JSON)…* e uma dica) · **Salvar** / **Fechar**. Trabalha numa cópia (`Clone`). **Salvar** aplica só o
  que está na janela; os grupos só são substituídos **se um JSON foi importado** (antes, Salvar sempre
  trocava os grupos pela cópia de quando a janela abriu, desfazendo mudanças feitas com ela aberta). O
  JSON importado é normalizado (nomes únicos) e, ao salvar, passa por adoção/recuperação de atalhos.
  Exportar usa o estado atual. Abrir pela segunda vez só traz a janela existente para frente
  (restaura se minimizada, `Topmost` alternado, `SetForegroundWindow`).
- **`TextPromptWindow`**: rótulo, caixa (selecionada), OK (padrão)/Cancelar; vazio não aceita; um
  *validador* opcional devolve o motivo, mostrado sob a caixa **na cor de destaque** (nunca vermelho) com a
  janela aberta. **`ImportTargetWindow`**: lista dos grupos + "(Novo grupo…)", com o campo de nome quando
  esse é escolhido. **`LaunchItemEditWindow`** (420×450): Nome, Tipo (sem `GroupLink`), Destino +
  Procurar (pasta → `OpenFolderDialog`), Argumentos, Diretório de trabalho, Modo de execução, Ícone
  personalizado + Procurar, "Fixar no desktop"; valida nome/tipo/destino e nome único no grupo.
- **Janelas do app** (`ModernWindow`): sem moldura do sistema; barra de título de 38 px, margem de 14 px
  para a sombra (blur 28, profundidade 6, opacidade 0,34), cantos 8, minimizar e fechar; arrastável pela barra.

### 15. Atalho de grupo na barra de tarefas

O Windows **não deixa um programa fixar sozinho** na barra (medido no Windows 11 26H2: a pasta
`…\Quick Launch\User Pinned\TaskBar` não é a barra — a barra é o valor binário `Taskband\Favorites`, e a
interface do shell para fixar devolve sucesso fora do Explorer sem fazer nada). Então *Criar atalho na
barra de tarefas* grava um `.lnk` em **`<Menu Iniciar do usuário>\Programas\Smart Dock Groups\<nome do
grupo>.lnk`** (known folder, nunca caminho fixo; "nome (6 primeiros do id)" se outro grupo usa o mesmo
nome), com destino no próprio exe, argumentos `--desktop-action=focus-group:<id>`, ícone do app, descrição
= nome do grupo e **AppUserModelID próprio** `SmartDockGroups.Group.<id>` (via `IPropertyStore`; sem ele
todos os atalhos aparecem como o mesmo programa e, fixado um, o seguinte já lê "Desafixar"). Abre o
Explorer com o arquivo selecionado e um balão (10 s) ensina: botão direito → *Fixar na barra de tarefas*
(no Windows 11, "Mostrar mais opções" ou Shift+clique direito), ou digitar o nome no Iniciar. O atalho é
achado pelo **argumento (o `Id`)**, não pelo nome: renomear o grupo renomeia o arquivo; remover o grupo o
apaga. Clicar nele → pipe → o grupo abre/expande/vem à frente, pulsa e seleciona o primeiro ícone; id
desconhecido mostra um balão "Grupo não encontrado.".

### 16. Tema do app e controles

Dois dicionários de recursos (apêndice A), escolhidos por `AppThemeService` (Sistema lê
`AppsUseLightTheme`; falha → escuro). Fonte `Segoe UI Variable Text, Segoe UI`, 13 pt (11,5 pequeno);
comandos de 28 px, campos de 27 px, cantos 4 (6 nos maiores), anel de foco de 2 px; estilos para
`TextBlock` (global, usa `AppTextBrush`), `Button` (+ primário em `AppAccentBrush`, ícone, fechar,
barra de título), `TextBox`, `CheckBox`, `ComboBox`/`ComboBoxItem`, `Slider`, `ScrollBar` (fina, 10 px, polegar com margem 2,5 — **não** largura fixa — e modelo próprio para a horizontal: trilha horizontal, sem inverter a direção, comandos Página à esquerda/direita),
`GroupBox`, e `ItemTemplate` de rótulo para listas de objetos anônimos (`AppLabelItemTemplate`, necessário
para o ComboBox mostrar o texto). `LocExtension` (`{loc:Loc chave}`) traduz em XAML. Sem editor de tema
visual: o tema do grupo muda por cor de fundo/imagem/opacidade/compartilhar. (O repositório ainda tem uma
janela `ThemeEditWindow` que **nenhum caminho abre** — código morto, junto com as chaves `theme.*`; não a recrie.)

### 17. Ícone do aplicativo

`tools/IconForge` (console WPF) desenha o `.ico` por vetores, **um quadro por tamanho** (16, 20, 24, 32,
40, 48, 64, 96, 128, 256 — nunca reduzindo o de 256, que vira lama a 16): placa arredondada (raio 24%,
inset 4%, gradiente diagonal azul `#5FC0FF`→`#1F5FE0`, brilho no alto nos tamanhos > 24), **quatro
quadrados arredondados** 2×2 (branco 95%) — o de baixo à direita em **amarelo-âmbar `#FFC43D`** com um
brilho de quatro pontas branco nos tamanhos ≥ 48 — com margem 22% (20% nos pequenos) e vão 8% (alinhados a
pixel inteiro nos pequenos). Uso: `IconForge <saida.ico> [pasta-previa] [variante]`. *(O âmbar é o único
amarelo do produto e nunca aparece junto a vermelho.)*

### 18. Idiomas

Oito: **de, en, es, it, ja, pl, pt, ru**, em `Localization/Strings.<código>.json` (objeto plano
chave→texto, UTF-8). `LocalizationService`: `Get(chave)` (plano B: inglês; depois a própria chave),
`Format(chave, args)`, `GetForLanguage(código, chave)`, `DetectLanguage()` (cultura de UI do Windows,
senão `en`), `SetLanguage`. **Toda chave existe nos oito arquivos, com os mesmos `{0}`/`{1}`** — um teste (`LocalizationCompletenessTests`) exige isso e exige que toda chave pedida pelo código exista; sem ele, seis idiomas ficaram meses sem 10 chaves e mostravam "Close group" e "Paste" em inglês. Os apêndices C (inglês) e D
(português) trazem **todas** as chaves e os textos; os outros seis idiomas são tradução fiel do
inglês, mantendo `{0}`, `{1}`, `\n`, `...` e as reticências.

### 19. Compilar, empacotar, distribuir

- `NuGet.Config` na raiz acrescenta (nunca substitui) ao `nuget.org` os padrões
  `Microsoft.NETCore.App.Runtime.*`, `Microsoft.WindowsDesktop.App.Runtime.*`,
  `Microsoft.AspNetCore.App.Runtime.*`, `Microsoft.NETCore.App.Host.*`, `Microsoft.NETCore.App.Ref`,
  `Microsoft.WindowsDesktop.App.Ref`, `Microsoft.AspNetCore.App.Ref`, `runtime.win-x64.*`, `xunit*`,
  `Microsoft.NET.Test.Sdk`, `Microsoft.TestPlatform.*`, `Microsoft.CodeCoverage`, `Newtonsoft.Json`.
- Compilar: `mkfile r` na raiz (o `.slnx` é reconhecido); empacotar: `mkfile p
  src/SmartDockGroups.App/SmartDockGroups.App.csproj`. Sem `mkfile`, `dotnet build SmartDockGroups.slnx -c Release`.
- **Instalador** (`src/SmartDockGroups.App/tools/build_installer.ps1` + `SmartDockGroups.iss`):
  `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
  -p:PublishReadyToRun=true` (pré-compila: menos compilação a cada partida) e depois **Inno Setup**
  (`ISCC.exe`, `winget install --id JRSoftware.InnoSetup`) com `/DMyAppVersion=<VERSION>` →
  `dist\SmartDockGroupsSetup-<versão>.exe`. Script: `AppId={{C9832DF6-615D-457C-A598-869C2033FFEC}`
  (fixo, identifica atualizações; **nunca gerar outro**), instala **por usuário** em
  `{localappdata}\Programs\SmartDockGroups` **sem elevação** (`PrivilegesRequired=lowest`), x64,
  `lzma2` sólido, `WizardStyle=modern`, idiomas inglês e português-BR, atalho no Menu Iniciar
  (`{group}`), ícone de desktop **opcional** (desmarcado), entrada em Adicionar/Remover Programas,
  executar ao final (`nowait postinstall skipifsilent`). O desinstalador **não** apaga
  `%AppData%\SmartDockGroups`. Editor: "Alexandre Chagas Sousa".
- Distribuição: release do GitHub (`alxchs/SmartDockGroups`, público) com o instalador anexado à tag da versão.

### 20. Como provar (parte da entrega)

**Testes xUnit** (`tests/SmartDockGroups.Tests`, 101 testes; o projeto vê os tipos `internal` do App):
geometria de monitor (alcançável, dono, vizinho, mapeamento reversível, anti-empilhamento, centralizar),
contrato do `config.json` (ida e volta de todos os campos, serialização determinística, leitura de
formatos antigos, ids estáveis), cache de ícones (corte de margens, URIs de protocolo, versão `v3`,
`.url` renomeado: resolve com **um** candidato e **descarta** sob ambiguidade), renderização do ladrilho,
posicionamento do diálogo (centralizar, perto do cursor, empurrar para dentro), nomes únicos, aspectos
compartilháveis, padrões de grupo novo, recuperação de atalho perdido (por nome, por prefixo, duas cópias
iguais aceitas, dois atalhos diferentes de mesmo nome **recusados**), navegação por teclado com voltas
(layout embaralhado de propósito), limite de 30 e atalho de grupo, pilha acoplada (expandir empurra,
só um aberto, sobe perto do rodapé, desacoplar restaura, persiste), verbos do menu da área de trabalho, e a completude dos idiomas (mesmas chaves e marcadores nos oito arquivos; toda chave usada existe).

**`GroupProbe`** (console WPF com manifesto PerMonitorV2, em C#): sobe o app de verdade numa pasta
isolada, mede a geometria das janelas, o SHA-256 da captura de cada grupo (`PrintWindow` com
`PW_RENDERFULLCONTENT`) e lê o **menu de contexto inteiro por UI Automation**; compara dois relatórios
(`compare_reports.py`). Regra: antes de comparar dois builds, rode a sonda **duas vezes no mesmo build** e
exija `IDENTICO` (um oráculo que oscila aprova ou reprova por sorte). Modo `--verify-lote` roda o app
sobre uma **cópia** da configuração real e percorre, com mouse e teclado de verdade: reparo na
inicialização, colar em grupo livre, rolagem, Ctrl+F, menus, nome repetido, remover grupo com atalhos,
App Folder, novo grupo perto do mouse, compartilhar opacidade, importar atalhos, atalho na barra;
`--dock`, `--drag` (arrastar entre grupos), `--menu-edges`, `--batch` (ordem do menu, arrastes até as
quatro bordas e sobre a divisa entre monitores, 12 passos de teclado, limite, atalho de grupo);
`--clean-cache` (instalação limpa) e variáveis `SDG_TEST_APPTHEME=Light`, `SDG_TEST_TEXT=<cor>`. Provar
**no executável publicado** (self-contained), nos dois cenários de cache: com o `IconCache` copiado
("Upgrade") e vazio ("Instalação limpa").

**Capturas:** só recortadas na janela do app; nunca tela cheia; nunca versionar captura que mostre dados
pessoais (o repositório é público); abrir cada PNG antes de aprovar.

### 21. Documentação e agentes

- `docs/OVERVIEW.md`: arquitetura, decisões e o que foi medido (não só o que foi escrito); `docs/PROMPT.md`:
  histórico por seção; este arquivo: especificação do zero. **Toda mudança de comportamento atualiza os
  três no mesmo commit.**
- `AGENTS.md` (o que a agy/outros agentes leem) e `CLAUDE.md`: autonomia para leitura/busca/build/teste e
  commits locais; **`git push` só com confirmação**; compilar com `mkfile`; nunca vermelho; disciplina de
  verificação (nenhuma afirmação de ausência sem busca exaustiva; nenhuma feature de interface pronta sem
  abrir o app; nenhum contorno pontual para sintoma repetido); seção "Comece por aqui".
- `tests/README.md`: o que cada teste e cada modo da sonda cobre e como rodá-los.

### 22. Entrega

Compile em Release sem erro nem aviso; rode todos os testes; gere o instalador; abra o app com uma
configuração de teste de quatro grupos e mostre por captura recortada: (a) um grupo com organização
por nome, (b) o menu do grupo completo, (c) um arraste de ícone de um grupo para outro, (d) o mesmo grupo
como App Folder, (e) os grupos acoplados com um expandido. Informe o tamanho do instalador e diga, para
cada afirmação técnica, o comando que a mediu.

---

## Apêndices (parte do prompt)

### Apêndice A — Paleta do app (`Theming/Dark.xaml` e `Theming/Light.xaml`)

<!-- BEGIN:palette -->
| Chave | Escuro | Claro |
|---|---|---|
| `AppBackgroundBrush` | `#1B1B1B` | `#F3F3F3` |
| `AppSurfaceBrush` | `#262626` | `#FFFFFF` |
| `AppSurface2Brush` | `#2E2E2E` | `#FAFAFA` |
| `AppSurface3Brush` | `#333333` | `#F0F0F0` |
| `AppBorderBrush` | `#3D3D3D` | `#E0E0E0` |
| `AppBorderStrongBrush` | `#525252` | `#C8C8C8` |
| `AppTextBrush` | `#F2F2F2` | `#1A1A1A` |
| `AppSecondaryTextBrush` | `#B8B8B8` | `#616161` |
| `AppTextFaintBrush` | `#8F8F8F` | `#8A8A8A` |
| `AppAccentBrush` | `#4AA3F0` | `#0F6CBD` |
| `AppAccentHoverBrush` | `#6CB6F5` | `#115EA3` |
| `AppAccentSoftBrush` | `#1E3A52` | `#E8F1FB` |
| `AppDangerBrush` | `#F26B62` | `#B3261E` |
| `AppDangerSoftBrush` | `#3A1F1D` | `#FDECEB` |
| `AppControlBackgroundBrush` | `#2E2E2E` | `#FFFFFF` |
| `AppControlHoverBrush` | `#333333` | `#F0F0F0` |
| `AppShadowColor` | `#000000` | `#7A000000` |

Cada chave é um `SolidColorBrush` (a última, `AppShadowColor`, é um `Color`) num `ResourceDictionary`; o app troca o dicionário inteiro ao mudar o tema e todos os controles usam `DynamicResource`.
<!-- END:palette -->

### Apêndice B — Ícones de linha (`Theming/Icons.xaml`, grade 24×24, traço 1,7, pontas arredondadas, nunca preenchidos)

<!-- BEGIN:icons -->
Cada ícone é um `StreamGeometry` (a sintaxe de dados do `Path`):

- `IconAdd`: `M12 5v14M5 12h14`
- `IconDuplicate`: `M11 9h8a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1h-8a1 1 0 0 1-1-1v-8a1 1 0 0 1 1-1z M15 5H6a2 2 0 0 0-2 2v9`
- `IconRename`: `M4 20h4l10-10-4-4L4 16z M14 6l4 4`
- `IconDelete`: `M5 7h14M9 7V5h6v2M7 7l1 13h8l1-13`
- `IconFolder`: `M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z`
- `IconFile`: `M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z M14 3v5h5`
- `IconOpenExternal`: `M14 3h7v7 M21 3l-9 9 M19 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h5`
- `IconUp`: `M12 19V6M6 12l6-6 6 6`
- `IconDown`: `M12 5v13M6 12l6 6 6-6`
- `IconBack`: `M19 12H5M12 19l-7-7 7-7`
- `IconChevronRight`: `M9 6l6 6-6 6`
- `IconChevronDown`: `M6 9l6 6 6-6`
- `IconChevronUp`: `M6 15l6-6 6 6`
- `IconCheck`: `M5 13l4 4L19 7`
- `IconLock`: `M6 11h12v9H6z M9 11V8a3 3 0 0 1 6 0v3`
- `IconGrid`: `M4 4h7v7H4z M13 4h7v7h-7z M4 13h7v7H4z M13 13h7v7h-7z`
- `IconSort`: `M4 7h13M4 12h9M4 17h5`
- `IconIconSize`: `M4 10V4h6 M20 14v6h-6 M4 4l6 6 M20 20l-6-6`
- `IconMove`: `M12 2v20M2 12h20 M9 5l3-3 3 3 M9 19l3 3 3-3 M5 9l-3 3 3 3 M19 9l3 3-3 3`
- `IconGather`: `M4 4h5v5H4z M15 4h5v5h-5z M4 15h5v5H4z M15 15h5v5h-5z M10 12h4M12 10v4`
- `IconColor`: `M12 3s6 6.6 6 10a6 6 0 0 1-12 0c0-3.4 6-10 6-10z`
- `IconImage`: `M4 5h16v14H4z M4 16l4.5-4.5 3.5 3.5 3-3L20 17 M9 9.5a1 1 0 1 1-2 0 1 1 0 0 1 2 0z`
- `IconOpacity`: `M12 3a9 9 0 0 0 0 18z M12 3a9 9 0 0 1 0 18`
- `IconSpacing`: `M7 4v16 M17 4v16 M10 12h4 M12 10l2 2-2 2`
- `IconStylePanel`: `M4 5h16v14H4z M4 9h16`
- `IconStyleAppFolder`: `M5 5h5v5H5z M14 5h5v5h-5z M5 14h5v5H5z M14 14h5v5h-5z`
- `IconMore`: `M5.2 12a0.8 0.8 0 1 0 1.6 0 0.8 0.8 0 1 0-1.6 0z M11.2 12a0.8 0.8 0 1 0 1.6 0 0.8 0.8 0 1 0-1.6 0z M17.2 12a0.8 0.8 0 1 0 1.6 0 0.8 0.8 0 1 0-1.6 0z`
- `IconFolderOpen`: `M3 8a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v1H6.5L3 17z M3 17l3.5-6H22l-3.5 6z`
- `IconShield`: `M12 3l7 3v5.5c0 4.3-3 7.2-7 8.5-4-1.3-7-4.2-7-8.5V6z`
- `IconCopy`: `M9 9h9a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1z M15 5H6a2 2 0 0 0-2 2v9`
- `IconShellMenu`: `M4 6h16M4 12h16M4 18h16`
- `IconProperties`: `M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z M12 11v5 M12 7.5v.5`
- `IconCut`: `M6 9a3 3 0 1 0 3-3L15 15M6 15a3 3 0 1 0 3 3L15 9M15 9l4-4M15 15l4 4`
- `IconPaste`: `M19 4h-3.5a2 2 0 0 0-4 0H8a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2zM12 4a1 1 0 1 1 2 0 1 1 0 0 1-2 0z`
- `IconSearch`: `M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14zM21 21l-5-5`
- `IconClose`: `M6 6l12 12M6 18L18 6`
- `IconSettings`: `M12 9.2a2.8 2.8 0 1 0 0 5.6 2.8 2.8 0 0 0 0-5.6z M19.4 15a1.5 1.5 0 0 0 .3 1.7l.1.1a1.8 1.8 0 1 1-2.5 2.5l-.1-.1a1.5 1.5 0 0 0-2.5 1v.3a1.8 1.8 0 1 1-3.6 0V20a1.5 1.5 0 0 0-2.5-1l-.1.1a1.8 1.8 0 1 1-2.5-2.5l.1-.1a1.5 1.5 0 0 0-1-2.5H5a1.8 1.8 0 1 1 0-3.6H5.2a1.5 1.5 0 0 0 1-2.5l-.1-.1a1.8 1.8 0 1 1 2.5-2.5l.1.1a1.5 1.5 0 0 0 2.5-1V4a1.8 1.8 0 1 1 3.6 0v.2a1.5 1.5 0 0 0 2.5 1l.1-.1a1.8 1.8 0 1 1 2.5 2.5l-.1.1a1.5 1.5 0 0 0 1 2.5H20a1.8 1.8 0 1 1 0 3.6h-.2a1.5 1.5 0 0 0-1.4.9z`
- `IconPower`: `M12 4v8 M7.5 6.8a7 7 0 1 0 9 0`
- `IconStartup`: `M12 20V7 M6 13l6-6 6 6 M5 4h14`

`IconStrokeThickness=1.7` e `IconNominalSize=24`. `AppIcons.Create(chave, pincel, tamanho)` põe o desenho num canvas de exatamente 24×24, traça com `StrokeThickness=1.7`, pontas e junções arredondadas, sem preenchimento, e o escala num `Viewbox` (sem o canvas fixo, um ícone baixo seria esticado até os mesmos limites de um alto e o conjunto perderia o peso comum).
<!-- END:icons -->

### Apêndice C — Textos em inglês (`Strings.en.json`)

<!-- BEGIN:strings-en -->
146 chaves, todas usadas pelo código. `{0}`, `{1}`, `{2}` são argumentos de `string.Format`; a sequência `\n` é quebra de linha. (O arquivo do repositório ainda carrega 33 chaves legadas sem uso — do antigo editor de temas e de menus removidos —; uma regeneração pode omiti-las.)

```json
{
  "tray.settings": "Settings...",
  "tray.newDesktopGroup": "New desktop group...",
  "tray.startWithWindows": "Start with Windows",
  "tray.exit": "Exit",
  "desktop.toggleCollapseAll": "Expand/collapse all groups",
  "desktop.gatherAll": "Gather groups to center",
  "desktop.contextMenuRoot": "Smart Dock Groups",
  "desktop.allAppFolder": "All groups: app folder style",
  "desktop.allPanel": "All groups: panel style",
  "settings.title": "Smart Dock Groups - Settings",
  "settings.appearance": "Appearance",
  "settings.theme": "Theme",
  "settings.language": "Language",
  "settings.themeSystem": "Follow Windows",
  "settings.themeLight": "Light",
  "settings.themeDark": "Dark",
  "settings.languageAuto": "Automatic (Windows language)",
  "settings.behavior": "Behavior",
  "settings.openMenuWith": "Open menu with",
  "settings.singleClick": "Single click",
  "settings.doubleClick": "Double click",
  "settings.globalHotkey": "Global hotkey",
  "settings.restoreGroupsHotkey": "Restore hidden groups",
  "settings.restoreGroupsHotkeyHint": "Show Desktop (and Win+M) can hide your groups behind the desktop without bringing them back, since they have no taskbar button to restore from. This shortcut brings them back.",
  "settings.export": "Export configuration (JSON)...",
  "settings.import": "Import configuration (JSON)...",
  "settings.save": "Save",
  "settings.close": "Close",
  "settings.importErrorMessage": "Could not import the selected file.",
  "settings.jsonFilter": "JSON file (*.json)|*.json",
  "item.title": "Menu item",
  "item.name": "Name",
  "item.type": "Type",
  "item.target": "Target",
  "item.browse": "Browse...",
  "item.arguments": "Arguments",
  "item.workingDirectory": "Working directory",
  "item.executionMode": "Execution mode",
  "item.customIcon": "Custom icon",
  "item.pinToDesktop": "Pin to desktop",
  "item.validationError": "Name, type and target are required.",
  "item.iconFilter": "Icons and executables (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|All files (*.*)|*.*",
  "item.allFilesFilter": "All files (*.*)|*.*",
  "item.open": "Open",
  "item.rename": "Rename...",
  "item.removeFromGroup": "Remove from group",
  "item.renamePrompt": "New name:",
  "item.removeConfirm": "Remove \"{0}\" from this group?",
  "group.rename": "Rename...",
  "group.backgroundColor": "Background color...",
  "group.backgroundImage": "Background image...",
  "group.removeBackgroundImage": "Remove background image",
  "group.opacity": "Opacity",
  "group.areaOpacity": "Area",
  "group.titleOpacity": "Title bar",
  "group.remove": "Remove group...",
  "group.openFolder": "Open folder",
  "group.stylePanel": "Style: panel",
  "group.styleAppFolder": "Style: app folder",
  "group.iconSpacing": "Icon spacing",
  "group.iconHGap": "Horizontal",
  "group.iconVGap": "Vertical",
  "group.deleteConfirm": "Remove group \"{0}\" and all its pinned items?",
  "group.namePrompt": "Group name:",
  "group.imageFilter": "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
  "group.arrangeIcons": "Arrange icons automatically",
  "group.searchPlaceholder": "Find...",
  "group.searchCount": "{0} found",
  "group.sortByName": "Name",
  "group.iconSize": "Icon size",
  "group.iconSizeSmall": "Small",
  "group.iconSizeMedium": "Medium",
  "group.iconSizeLarge": "Large",
  "group.newMenu": "New",
  "group.newTextFileName": "New Text Document",
  "group.folderNamePrompt": "Folder name:",
  "group.expand": "Expand",
  "group.collapse": "Collapse",
  "common.ok": "OK",
  "common.cancel": "Cancel",
  "common.appName": "Smart Dock Groups",
  "overlay.back": "Back",
  "overlay.empty": "This group is empty",
  "group.menu": "Group menu",
  "item.runAsAdmin": "Run as administrator",
  "item.openFileLocation": "Open file location",
  "item.copyPath": "Copy as path",
  "item.properties": "Properties",
  "item.standardMenu": "Standard menu",
  "group.duplicate": "Duplicate group",
  "group.taskbarShortcut": "Create taskbar shortcut",
  "group.taskbarShortcutHint": "Group shortcut created in the Start menu (Smart Dock Groups folder). To pin it: right-click it → Pin to taskbar (on Windows 11, under \"Show more options\"), or type the group's name in Start and pin the result.",
  "group.notFound": "Group not found.",
  "group.copySuffix": "{0} (copy)",
  "group.shareVisual": "Appearance",
  "group.applyVisualToAll": "Apply to all groups now",
  "group.setAsDefaultVisual": "Use as default for new groups",
  "group.wallpaperAsBackground": "Use desktop wallpaper as background",
  "group.wallpaperForAll": "Use wallpaper on all groups",
  "group.wallpaperUnavailable": "Windows is not showing a wallpaper picture right now.",
  "item.cut": "Cut",
  "item.copy": "Copy",
  "group.paste": "Paste",
  "group.close": "Close group",
  "desktop.openAllGroups": "Open all desktop groups",
  "desktop.closeAllGroups": "Close all desktop groups",
  "desktop.groupsMenu": "Desktop groups",
  "group.windowOrder": "Desktop window order",
  "group.bringAllToFront": "Bring all groups to front",
  "group.sendOthersToBack": "Send other groups to back",
  "group.sendAllToBack": "Send all groups to back",
  "group.bringAllToThisMonitorCentered": "Bring all groups to this monitor (centered)",
  "group.sendAllToMonitor": "Send all groups to Monitor {0} ({1}x{2})",
  "group.sendAllToMonitorPrimary": "Send all groups to Monitor {0} (Primary, {1}x{2})",
  "group.viewMenu": "View",
  "group.newGroup": "New group...",
  "group.importShortcuts": "Import shortcuts...",
  "group.importShortcutsTitle": "Select the shortcuts (Ctrl+A selects the whole folder)",
  "group.importShortcutsFilter": "Shortcuts and programs (*.lnk;*.url;*.exe)|*.lnk;*.url;*.exe|All files (*.*)|*.*",
  "group.nameTaken": "There is already an item named \"{0}\" in this group. Choose another name.",
  "group.deleteWithContentsConfirm": "The group \"{0}\" has {1} shortcut(s). Removing the group will lose them.\n\nRemove the group anyway?",
  "group.shareWithOthers": "Share with the other groups",
  "group.aspectAll": "Whole appearance",
  "group.aspectColor": "Background colour only",
  "group.aspectImage": "Background image only",
  "group.aspectOpacity": "Opacity only",
  "group.aspectSpacing": "Icon spacing only",
  "group.aspectIconSize": "Icon size only",
  "item.locateTarget": "Locate missing shortcut...",
  "item.locateTargetTitle": "Where is the shortcut \"{0}\"?",
  "item.targetMissing": "Shortcut not found:\n{0}\n\nRight-click and use \"Locate missing shortcut...\".",
  "item.removeWithContentsConfirm": "Remove \"{0}\"? The {1} shortcut(s) inside will be lost.",
  "item.thisGroup": "Group \"{0}\"",
  "settings.importExport": "Import and export",
  "settings.importShortcuts": "Import shortcuts...",
  "settings.importShortcutsHint": "\"Import shortcuts\" opens a folder: select as many shortcuts as you like (Ctrl+A selects all) and choose the group that receives them. A JSON configuration replaces every group when you click Save.",
  "settings.importTargetPrompt": "Add {0} shortcut(s) to the group:",
  "settings.importNewGroup": "(New group...)",
  "settings.importDone": "{0} shortcut(s) added to the group \"{1}\".",
  "group.dockAll": "Dock all groups",
  "group.undockAll": "Undock groups",
  "group.newMenuShortcut": "Shortcut...",
  "group.newMenuGroup": "Group...",
  "group.newMenuGroupLink": "Shortcut to a group",
  "group.limitReached": "The limit is {0} items per group ({1} not added). It is better to create a new group.",
  "group.linkRefused": "A group cannot hold a shortcut to itself, nor two shortcuts to the same group."
}
```
<!-- END:strings-en -->

### Apêndice D — Textos em português (`Strings.pt.json`)

<!-- BEGIN:strings-pt -->
146 chaves, todas usadas pelo código. `{0}`, `{1}`, `{2}` são argumentos de `string.Format`; a sequência `\n` é quebra de linha. (O arquivo do repositório ainda carrega 33 chaves legadas sem uso — do antigo editor de temas e de menus removidos —; uma regeneração pode omiti-las.)

```json
{
  "tray.settings": "Configurações...",
  "tray.newDesktopGroup": "Novo grupo no desktop...",
  "tray.startWithWindows": "Iniciar com o Windows",
  "tray.exit": "Sair",
  "desktop.toggleCollapseAll": "Expandir/colapsar todos os grupos",
  "desktop.gatherAll": "Reunir grupos no centro",
  "desktop.contextMenuRoot": "Smart Dock Groups",
  "desktop.allAppFolder": "Todos os grupos: estilo pasta de app",
  "desktop.allPanel": "Todos os grupos: estilo painel",
  "settings.title": "Smart Dock Groups - Configurações",
  "settings.appearance": "Aparência",
  "settings.theme": "Tema",
  "settings.language": "Idioma",
  "settings.themeSystem": "Seguir o Windows",
  "settings.themeLight": "Claro",
  "settings.themeDark": "Escuro",
  "settings.languageAuto": "Automático (idioma do Windows)",
  "settings.behavior": "Comportamento",
  "settings.openMenuWith": "Abrir menu com",
  "settings.singleClick": "Clique simples",
  "settings.doubleClick": "Duplo clique",
  "settings.globalHotkey": "Atalho global",
  "settings.restoreGroupsHotkey": "Restaurar grupos ocultos",
  "settings.restoreGroupsHotkeyHint": "Mostrar área de trabalho (e Win+M) pode esconder seus grupos atrás da área de trabalho sem trazê-los de volta, pois eles não têm botão na barra de tarefas para restaurar. Este atalho os traz de volta.",
  "settings.export": "Exportar configuração (JSON)...",
  "settings.import": "Importar configuração (JSON)...",
  "settings.save": "Salvar",
  "settings.close": "Fechar",
  "settings.importErrorMessage": "Não foi possível importar o arquivo selecionado.",
  "settings.jsonFilter": "Arquivo JSON (*.json)|*.json",
  "item.title": "Item do menu",
  "item.name": "Nome",
  "item.type": "Tipo",
  "item.target": "Destino",
  "item.browse": "Procurar...",
  "item.arguments": "Argumentos",
  "item.workingDirectory": "Diretório de trabalho",
  "item.executionMode": "Modo de execução",
  "item.customIcon": "Ícone personalizado",
  "item.pinToDesktop": "Fixar no desktop",
  "item.validationError": "Nome, tipo e destino são obrigatórios.",
  "item.iconFilter": "Ícones e executáveis (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|Todos os arquivos (*.*)|*.*",
  "item.allFilesFilter": "Todos os arquivos (*.*)|*.*",
  "item.open": "Abrir",
  "item.rename": "Renomear...",
  "item.removeFromGroup": "Remover do grupo",
  "item.renamePrompt": "Novo nome:",
  "item.removeConfirm": "Remover \"{0}\" deste grupo?",
  "group.rename": "Renomear...",
  "group.backgroundColor": "Cor de fundo...",
  "group.backgroundImage": "Imagem de fundo...",
  "group.removeBackgroundImage": "Remover imagem de fundo",
  "group.opacity": "Opacidade",
  "group.areaOpacity": "Área",
  "group.titleOpacity": "Barra de título",
  "group.remove": "Remover grupo...",
  "group.openFolder": "Abrir pasta",
  "group.stylePanel": "Estilo: painel",
  "group.styleAppFolder": "Estilo: pasta de apps",
  "group.iconSpacing": "Espaçamento entre ícones",
  "group.iconHGap": "Horizontal",
  "group.iconVGap": "Vertical",
  "group.deleteConfirm": "Remover o grupo \"{0}\" e todos os seus itens fixados?",
  "group.namePrompt": "Nome do grupo:",
  "group.imageFilter": "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
  "group.arrangeIcons": "Organizar ícones automaticamente",
  "group.searchPlaceholder": "Localizar...",
  "group.searchCount": "{0} encontrado(s)",
  "group.sortByName": "Nome",
  "group.iconSize": "Tamanho do ícone",
  "group.iconSizeSmall": "Pequeno",
  "group.iconSizeMedium": "Médio",
  "group.iconSizeLarge": "Grande",
  "group.newMenu": "Novo",
  "group.newTextFileName": "Novo Documento de Texto",
  "group.folderNamePrompt": "Nome da pasta:",
  "group.expand": "Expandir",
  "group.collapse": "Colapsar",
  "common.ok": "OK",
  "common.cancel": "Cancelar",
  "common.appName": "Smart Dock Groups",
  "overlay.back": "Voltar",
  "overlay.empty": "Este grupo está vazio",
  "group.menu": "Menu do grupo",
  "item.runAsAdmin": "Executar como administrador",
  "item.openFileLocation": "Abrir local do arquivo",
  "item.copyPath": "Copiar como caminho",
  "item.properties": "Propriedades",
  "item.standardMenu": "Menu padrão",
  "group.duplicate": "Duplicar grupo",
  "group.taskbarShortcut": "Criar atalho na barra de tarefas",
  "group.taskbarShortcutHint": "Atalho do grupo criado no Menu Iniciar (pasta Smart Dock Groups). Para fixar: clique com o botão direito nele → Fixar na barra de tarefas (no Windows 11, em \"Mostrar mais opções\"), ou digite o nome do grupo no Iniciar e fixe o resultado.",
  "group.notFound": "Grupo não encontrado.",
  "group.copySuffix": "{0} (cópia)",
  "group.shareVisual": "Aparência",
  "group.applyVisualToAll": "Aplicar a todos os grupos agora",
  "group.setAsDefaultVisual": "Usar como padrão para novos grupos",
  "group.wallpaperAsBackground": "Usar papel de parede como fundo",
  "group.wallpaperForAll": "Usar papel de parede em todos os grupos",
  "group.wallpaperUnavailable": "O Windows não está exibindo uma imagem de papel de parede no momento.",
  "item.cut": "Recortar",
  "item.copy": "Copiar",
  "group.paste": "Colar",
  "group.close": "Fechar grupo",
  "desktop.openAllGroups": "Abrir todos os grupos",
  "desktop.closeAllGroups": "Fechar todos os grupos",
  "desktop.groupsMenu": "Grupos da Área de Trabalho",
  "group.windowOrder": "Ordem na área de trabalho",
  "group.bringAllToFront": "Trazer todos os grupos para frente",
  "group.sendOthersToBack": "Enviar todos para trás (exceto este)",
  "group.sendAllToBack": "Enviar todos para trás",
  "group.bringAllToThisMonitorCentered": "Trazer todos os grupos para este monitor centralizados",
  "group.sendAllToMonitor": "Enviar todos os grupos para o Monitor {0} ({1}x{2})",
  "group.sendAllToMonitorPrimary": "Enviar todos os grupos para o Monitor {0} (Principal, {1}x{2})",
  "group.viewMenu": "Exibição",
  "group.newGroup": "Novo grupo...",
  "group.importShortcuts": "Importar atalhos...",
  "group.importShortcutsTitle": "Selecione os atalhos (Ctrl+A seleciona todos da pasta)",
  "group.importShortcutsFilter": "Atalhos e programas (*.lnk;*.url;*.exe)|*.lnk;*.url;*.exe|Todos os arquivos (*.*)|*.*",
  "group.nameTaken": "Já existe um item chamado \"{0}\" neste grupo. Escolha outro nome.",
  "group.deleteWithContentsConfirm": "O grupo \"{0}\" tem {1} atalho(s). Se você remover o grupo, esses atalhos serão perdidos.\n\nDeseja remover o grupo mesmo assim?",
  "group.shareWithOthers": "Compartilhar com os outros grupos",
  "group.aspectAll": "Aparência completa",
  "group.aspectColor": "Só a cor de fundo",
  "group.aspectImage": "Só a imagem de fundo",
  "group.aspectOpacity": "Só a opacidade",
  "group.aspectSpacing": "Só o espaçamento entre ícones",
  "group.aspectIconSize": "Só o tamanho do ícone",
  "item.locateTarget": "Localizar atalho perdido...",
  "item.locateTargetTitle": "Onde está o atalho \"{0}\"?",
  "item.targetMissing": "Atalho não encontrado:\n{0}\n\nClique com o botão direito e use \"Localizar atalho perdido...\".",
  "item.removeWithContentsConfirm": "Remover \"{0}\"? Os {1} atalho(s) que estão dentro serão perdidos.",
  "item.thisGroup": "Grupo \"{0}\"",
  "settings.importExport": "Importar e exportar",
  "settings.importShortcuts": "Importar atalhos...",
  "settings.importShortcutsHint": "\"Importar atalhos\" abre uma pasta: selecione quantos atalhos quiser (Ctrl+A seleciona todos) e escolha o grupo que vai recebê-los. A configuração em JSON substitui todos os grupos ao clicar em Salvar.",
  "settings.importTargetPrompt": "Adicionar {0} atalho(s) ao grupo:",
  "settings.importNewGroup": "(Novo grupo...)",
  "settings.importDone": "{0} atalho(s) adicionado(s) ao grupo \"{1}\".",
  "group.dockAll": "Acoplar todos os grupos",
  "group.undockAll": "Desacoplar grupos",
  "group.newMenuShortcut": "Atalho...",
  "group.newMenuGroup": "Grupo...",
  "group.newMenuGroupLink": "Atalho para um grupo",
  "group.limitReached": "O limite é de {0} itens por grupo ({1} não adicionado(s)). O ideal é criar um novo grupo.",
  "group.linkRefused": "Um grupo não pode ter um atalho para si mesmo nem dois atalhos para o mesmo grupo."
}
```
<!-- END:strings-pt -->

### Apêndice E — Valores de referência

<!-- BEGIN:constants -->
**Janela do grupo** (`Desktop/DesktopGroupWindow.cs`)

- `MinIconScale = 0.5`
- `MaxIconScale = 3.0`
- `TileSize = 80`
- `MinCanvasPadding = 8`
- `CanvasPaddingRatio = 0.05`
- `FolderTapTolerance = 5`
- `MinGroupWidth = 140`
- `MinGroupHeight = 120`
- `ResizeEdgeThickness = 6`
- `ResizeCornerSize = 14`
- `AppFolderDesktopScale = 1.4`
- `GA_ROOT = 2`
- `SWP_NOSIZE = 0x0001`
- `SWP_NOMOVE = 0x0002`
- `SWP_NOACTIVATE = 0x0010`
- `SelectedTileOpacityWhileDragging = 0.35`

**Ladrilho do App Folder** (`Desktop/AppFolderTile.cs`)

- `PlateSize = 92`
- `MosaicColumns = 3`
- `MosaicCapacity = MosaicColumns * MosaicColumns`

**Folha do App Folder** (`Desktop/GroupOverlayWindow.cs`)

- `TileWidth = 118`
- `TileHeight = 128`
- `MinSheetWidth = 360`
- `MinSheetHeight = 260`

**Monitores** (`Desktop/MonitorPlacement.cs`)

- `MinVisibleWidth = 48`
- `MinVisibleHeight = 16`
- `TitleStripHeight = 32`

**Atalhos globais** (`Services/GlobalHotkeyService.cs`)

- `WmHotKey = 0x0312`
- `MenuHotkeyId = 0xA1F3`
- `RestoreHotkeyId = 0xA1F4`

- `DockLayout.MinExpandedHeight = 120`
- `GroupLimits.MaxEntries = 30`
<!-- END:constants -->
