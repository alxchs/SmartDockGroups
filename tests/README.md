# Testes

Duas camadas, com propósitos diferentes. As duas precisam passar antes de qualquer
publicação.

## 1. `SmartDockGroups.Tests` — asserções automatizadas (xUnit)

```
dotnet test tests/SmartDockGroups.Tests
```

101 testes, ~4 s. Cobrem a lógica que não tem tela, utilitários de ícone e posicionamento multi-monitor:

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
- **`GroupRulesTests`** — as regras de 2026-09-30: nome único dentro do grupo (maiúsculas e
  espaços ignorados, numeração " (2)", normalização de configs antigas mantendo o primeiro),
  compartilhar um aspecto da aparência sem arrastar os outros, `GroupDefaults` aplicando só
  o escolhido e sobrevivendo a salvar/recarregar, diálogo aberto sob o ponteiro e empurrado
  para dentro da área útil, e a recuperação de atalho perdido — pelo nome, pelo prefixo,
  aceitando duas cópias do mesmo atalho e **recusando** dois atalhos diferentes de mesmo nome.
- **`DockTests`** — "acoplar todos": a pilha fechada é uma barra depois da outra, expandir
  um empurra os de baixo pela altura extra, só um fica aberto, o aberto é cortado ao que cabe
  e a pilha sobe perto do rodapé sem passar do topo da área útil; desacoplar devolve a
  posição, o tamanho, o estado e o estilo; o estado sobrevive a salvar/recarregar; os verbos
  do menu da área de trabalho são únicos e têm rótulo nos 8 idiomas.
- **`NavigationAndLimitTests`** — navegação por teclado pela posição na tela (layout embaralhado de
  propósito: a ordem de criação difere da visual), com voltas nas pontas; limite de 30 entradas
  (subpastas contam; grupo antigo acima do limite não aceita mais nada); atalho de grupo (nunca
  para si, nunca dois para o mesmo, nome único).
- **`LocalizationCompletenessTests`** — cada idioma tem exatamente as chaves do inglês e os mesmos `{0}`/`{1}`;
  toda chave que o código ou o XAML pede existe. (Sem isso, uma chave faltando só aparece como texto em
  inglês numa interface em outro idioma.)
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

**Padrão desde 2026-09-30: pasta isolada.** O app aceita `SMARTDOCKGROUPS_DATA_DIR`; a sonda
cria `<out>\data-<label>`, sobe o app apontando para ela e só encerra o processo que ela mesma
abriu. A configuração real, o registro e a instância aberta do usuário ficam intocados.

`--legacy-real-config` (para builds anteriores, que ignoram a variável) volta ao modo antigo:
backup do `%AppData%\SmartDockGroups\config.json`, **conferido por hash**, **abortando antes de
abrir qualquer coisa** se o backup não bater; no fim restaura e confere de novo. Esse modo
encerra todas as instâncias do app — inclusive a do usuário.

### `--verify-lote`

```
tests/GroupProbe/bin/Release/net10.0-windows/GroupProbe.exe --verify-lote ^
  --exe <SmartDockGroups.App.exe> --out <pasta> [--clean-cache] [--reuse-data]
```

Copia a configuração real (só leitura) e o cache de ícones para `<pasta>\data`, acrescenta dois
grupos de teste ("Livre (teste)", em posição livre, e "Rolagem (teste)", baixo e cheio) e percorre
os cenários do lote de 2026-09-30, gravando `report-lote.json` e capturas recortadas `lote-*.png`.
`--drag` arrasta um ícone de um grupo para outro com o mouse real e mede onde ele caiu (os quadros saem com a tela inteira: nunca versionar); `--batch` roda a ordem do menu, os arrastes até as bordas (inclusive sobre a divisa entre monitores), a
navegação por teclas, o limite de 30 e o atalho de grupo; `--dock` roda só o acoplamento (inclusive os comandos com o app fechado, relançando o exe
com `--desktop-action=`); `--menu-edges` mede os menus com o grupo em cada canto de cada monitor.
`SDG_TEST_LANG=de` roda o app em outro idioma; `SDG_TEST_APPTHEME=Light` roda o app em tema claro (como num PC com Windows claro) e `SDG_TEST_TEXT=#404040` dá ao grupo de teste uma cor
de texto que destoa — para ver como os menus ficam.
`--clean-cache` = cenário "Instalação limpa"; `--reuse-data` reaproveita a pasta de uma rodada
anterior (atalhos já adotados). Os rótulos procurados nos menus são os do idioma da configuração
copiada (hoje inglês).

### Limite conhecido

O menu usa um ícone próprio de "visto" em vez de `ToggleState`, então o relatório traz
`toggle: null` mesmo em item marcado. Quem cobre o estado visual do visto é o hash da captura.

## Linha de base

`tests/baseline/` guarda o relatório e as capturas da **master em b46789b (v1.1.1.0)**, que é a
referência contra a qual a higienização foi comparada. Regerar só quando o comportamento de
grupo mudar de propósito — e, quando isso acontecer, dizer no commit o que mudou e por quê.
