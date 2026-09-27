# Testes

Duas camadas, com propósitos diferentes. As duas precisam passar antes de qualquer
publicação.

## 1. `SmartDockGroups.Tests` — asserções automatizadas (xUnit)

```
dotnet test tests/SmartDockGroups.Tests
```

47 testes, ~1 s. Cobrem a lógica que não tem tela, utilitários de ícone e posicionamento multi-monitor:

- **`MonitorPlacementTests`** — a geometria que resgata um grupo quando o conjunto de
  monitores mudou. O `docs/OVERVIEW.md` registrava que essa matemática tinha sido conferida
  por um programa de console "não commitado neste repositório"; estas são aquelas asserções,
  agora commitadas. Inclui a propriedade de reversibilidade (ida e volta entre monitores não
  pode deslocar o grupo).
- **`GroupConfigurationTests`** — o contrato do arquivo `config.json`: round-trip de todos os
  campos de grupo, determinismo da serialização, leitura de configurações escritas por
  versões anteriores (`Grid`/`ByType`, `AreaTransparent`/`TitleTransparent`) e a identidade
  estável do grupo (`Id`) de que o atalho da barra de tarefas depende.
- **`IconCacheServiceTests`** — extração e corte de margens transparentes vazias em ícones
  (`TrimTransparentMargins`), resolução de URIs de protocolo (`msteams:`, `ms-settings:`, `http:`),
  e integridade de cache com destinos variados.
- **`IconVisualCaptureTests`** — renderização do ladrilho de pasta de app (`AppFolderTile`)
  com validação visual do mosaico 3×3.
- **`PromptPositioningTests`** — cálculo de centralização de janelas por monitor com DPI heterogêneo,
  garantia de contenção na área de trabalho útil, conversão física para DIPs WPF, e validação interativa
  em ambiente multi-monitor real (`Screen.AllScreens`) com medição de retângulos e capturas.

O projeto enxerga os tipos `internal` do App por um `InternalsVisibleTo` declarado em
`src/SmartDockGroups.App/AssemblyInfo.cs`.

> Os pacotes do xUnit precisaram de entradas novas no `NuGet.Config` da raiz. A máquina tem um
> `packageSourceMapping` global restrito, e sem o mapeamento o `dotnet restore` recusa os
> pacotes com `NU1100` — o mesmo motivo pelo qual os runtime packs já estavam lá.

## 2. `GroupProbe` — caracterização da aplicação de verdade

```
dotnet build -c Release tests/GroupProbe
tests/GroupProbe/bin/Release/net10.0-windows/GroupProbe.exe ^
  --exe <caminho do SmartDockGroups.App.exe> ^
  --fixture tests/GroupProbe/fixtures/probe-config.json ^
  --out <pasta de saída> ^
  --label <nome>
```

Sobe o app de verdade com uma configuração fixa, espera as janelas aparecerem, e grava:

- a geometria e os estilos estendidos de cada janela de grupo;
- **uma captura PNG de cada grupo** (via `PrintWindow` com `PW_RENDERFULLCONTENT`, que é o que
  funciona em janela `AllowsTransparency`), com o SHA-256 no relatório;
- **o menu de contexto do grupo inteiro**, lido por UI Automation — nomes, ordem, habilitado e
  submenus expandidos. Os menus do WPF publicam árvore de automação de verdade; foram os
  ladrilhos de ícone, construídos à mão, que não publicavam (ver `docs/PROMPT.md`, item 20);
- a configuração gravada depois da execução, com os `Id` normalizados para `<id-N>` — sem isso
  dois relatórios do mesmo build nunca seriam iguais, porque os `Id` são GUID novos.

**Para que serve:** rodar contra dois builds e comparar os relatórios. O que a refatoração
mudou nos grupos aparece como diff; o que ela não mudou deixa o relatório idêntico.

### Modos especializados de verificação

A sonda também oferece rotinas automáticas de validação e captura do app real:
- `--verify-defect-b`: executa o app real em seu modo original (System DPI Aware sem manifesto) enquanto a sonda utiliza PerMonitorV2 para medição precisa de coordenadas físicas, posiciona o cursor real em cada monitor, dispara o menu de contexto do grupo ("Renomear..."), valida a contenção do diálogo na área útil, gera capturas recortadas das janelas (`B-fluxo-real-monitorN.png`) preservando total privacidade, além de testar o caminho de "Novo grupo".
- `--capture-teams`: sobe o app real com atalho do Teams nos modos Painel e Mosaico, gerando as capturas `A-app-real-painel.png` e `A-app-real-mosaico.png`.

### Segurança da configuração do usuário

A sonda faz backup do `%AppData%\SmartDockGroups\config.json`, **confere o backup por hash** e
**aborta antes de abrir qualquer coisa** se o backup não bater. No fim restaura e confere de
novo. Nenhuma execução deixa a configuração real alterada.

### Limite conhecido

O menu usa um ícone próprio de "visto" em vez de `ToggleState`, então o relatório traz
`toggle: null` mesmo em item marcado. Quem cobre o estado visual do visto é o hash da captura.

## Linha de base

`tests/baseline/` guarda o relatório e as capturas da **master em b46789b (v1.1.1.0)**, que é a
referência contra a qual a higienização foi comparada. Regerar só quando o comportamento de
grupo mudar de propósito — e, quando isso acontecer, dizer no commit o que mudou e por quê.
