# Regras e Preferências do Projeto SmartDockGroups

## Autonomia Total de Execução (Regra Rígida)

1. **NUNCA pedir permissão, confirmação ou interromper o fluxo para comandos de busca, listagem ou leitura**:
   - `Get-ChildItem`, `dir`, `ls`
   - `Select-String`
   - `git grep`, `git log`, `git status`, `git diff`, `git branch`, `git show`
   - Scripts de teste, auditoria e compilação
   - Esses comandos DEVEM ser executados de forma 100% autônoma, direta e contínua sem nunca perguntar ao Alexandre.

2. **Git**:
   - NUNCA perguntar nada sobre comandos git (leitura, checkout, branch, add, commits locais de trabalho). Executar diretamente com autonomia total.
   - A única restrição é `git push`: NUNCA fazer push sem confirmação explícita do Alexandre naquele momento.

3. **Compilação**:
   - Usar sempre `mkfile r` para release e `mkfile p` para gerar pacote/instalador.

4. **Disciplina de Verificação**:
   - Toda feature com interface precisa ser testada e aberta de verdade.
   - Proibido declarar ausência sem busca exaustiva.
   - Proibido aplicar contorno pontual a sintoma repetido sem investigar a causa raiz.

## Comece por aqui

**O que é:** Smart Dock Groups, um organizador de área de trabalho para Windows (WPF, .NET 10):
grupos de atalhos em janelas livres (painel) ou em pasta de apps. O Core (`src/SmartDockGroups.Core`)
tem modelos e regras sem UI; o App (`src/SmartDockGroups.App`) tem toda a interface.

**Leia antes de mexer, nesta ordem:**
1. `docs/OVERVIEW.md` — o estado atual: como cada parte funciona, por que, e o que foi medido.
   É o mapa; procure por aqui antes de procurar no código.
2. `docs/PROMPT.md` — o histórico do que foi pedido, seção por seção (a última é a mais recente).
3. `tests/README.md` — o que cada teste e cada modo da sonda cobre.
4. `docs/PROMPT_GERACAO_UNICA.md` — a especificação completa do zero (todos os números, regras e textos).
   Seus apêndices são gerados: depois de mudar paleta, ícones, textos ou constantes, rode
   `python tools/atualizar_apendices_prompt.py` e confira o `git diff`.

**Regra de manutenção:** toda mudança de comportamento atualiza `docs/OVERVIEW.md` e `docs/PROMPT.md`
**no mesmo commit**, e a versão (`VERSION`) sobe quando há instalador novo.

**Compilar e empacotar:** `mkfile r` (Release) e `mkfile p src/SmartDockGroups.App/SmartDockGroups.App.csproj`
(instalador em `dist\`). Não chame `dotnet` na mão para o instalador.

**Testar a interface sem tocar nos dados do Alexandre:**
`GroupProbe --verify-lote [--batch|--dock|--drag|--menu-edges] --exe <App.exe> --out <pasta>` roda o app
de verdade (mouse, teclado e UI Automation) numa pasta de dados isolada (`SMARTDOCKGROUPS_DATA_DIR`), com
uma **cópia** da configuração real. O app do Alexandre pode estar aberto: não o encerre, a sonda só
encerra o processo que ela mesma abriu. `SDG_TEST_APPTHEME=Light` testa o app em tema claro.

**Limites que valem sempre:**
- Nunca `git push` sem o Alexandre confirmar naquele momento. Commit local é livre.
- Nunca vermelho (nem vermelho com amarelo) em ícone, seleção ou arte.
- Captura de tela só recortada na janela do app; nunca tela cheia, e nunca versionar captura que mostre
  atalhos ou dados pessoais dele (o repositório é público). Abra cada PNG antes de aprovar.
- A interface do Alexandre está em **inglês** (`Language: "en"`) e a barra de tarefas dele fica **em cima**:
  posicione sempre pela área útil do monitor (`WorkingArea`) e use os rótulos em inglês nos testes de menu.
- Texto novo de interface entra nos **8 idiomas** (`Localization/Strings.*.json`).
- Cota/erro da agy não vira trabalho do Claude e vice-versa: se travar, diga o que travou.

## Verificação: o que vale como prova neste repositório

Existem duas camadas, e as duas rodam antes de qualquer publicação:

```
dotnet test tests/SmartDockGroups.Tests          # 101 assercoes, ~4 s
dotnet build -c Release tests/GroupProbe     # sonda de caracterizacao
```

A sonda sobe o app de verdade e grava um relatório JSON com a geometria das janelas, o
SHA-256 da captura de cada grupo e o menu de contexto inteiro lido por UI Automation. Uso:

```
tests/GroupProbe/bin/Release/net10.0-windows/GroupProbe.exe ^
  --exe <SmartDockGroups.App.exe> --fixture tests/GroupProbe/fixtures/probe-config.json ^
  --out <pasta> --label <nome>
python tests/compare_reports.py tests/baseline/report-master.json <pasta>/report-<nome>.json
```

**Regras que valem para qualquer agente que mexer aqui, inclusive a agy:**

1. **Toque em comportamento de grupo ⇒ rode a sonda e cole a saída do comparador.** "Compila"
   não é prova. Divergência inesperada nos 382 campos (eram 316 antes da reorganização dos
   menus em 2026-09-30) é reprovação, não detalhe.
2. **Valide o oráculo antes de usá-lo.** Antes de comparar dois builds, rode a sonda duas
   vezes contra o *mesmo* build e confirme `IDENTICO`. Um oráculo que oscila reprova ou aprova
   por sorte. Já aconteceu aqui: a primeira sonda dava 283 campos numa execução e 316 na
   seguinte, porque esperava submenu por `sleep` fixo em vez do estado reportado pelo menu.
3. **Prefira a pasta de dados isolada** (`SMARTDOCKGROUPS_DATA_DIR`, padrão da sonda desde
   2026-09-30): o app roda ao lado da instância real sem tocar na configuração dela.
   **Nunca mexa no `%AppData%\SmartDockGroups\config.json` sem backup conferido por hash**, e
   confira a restauração no fim. A sonda já faz isso e **aborta antes de abrir qualquer coisa**
   se o backup não bater — copie esse padrão, não o improvise.
4. **Sonda de Win32 se escreve em C#, não em PowerShell.** O marshalling do PowerShell
   transforma `$null` em string vazia num parâmetro `string` de P/Invoke e já produziu duas
   conclusões erradas neste projeto.
5. **Auditoria por *grep* de código-fonte não conta como teste.** Os seis scripts que faziam
   isso foram removidos justamente porque passavam enquanto a feature estava quebrada. Meça o
   que o app faz, não a aparência do código que deveria fazer.
6. **Antes de declarar uma fase concluída, diga o que você mediu e com que comando.** Uma
   afirmação técnica sem o comando que a sustenta é tratada aqui como não verificada.
7. **Mudança em extração/cache de ícone deve ser testada contra o publish self-contained.**
   Use `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`
   (ou `mkfile p src/SmartDockGroups.App/SmartDockGroups.App.csproj`), testando o executável
   em `bin/Release/net10.0-windows/win-x64/publish/SmartDockGroups.App.exe`, e NÃO apenas o build
   framework-dependent comum.
   Lição do incidente da OS 04 (v1.1.1.1): o build comum rodava em testes locais mas o publicado em produção
   apresentava regressão severa porque atalhos `.url` na Área de Trabalho com nomes renomeados (ex.:
   `Alexandre Chagas Sousa.url` vs `Alexandre.url` no `config.json` resolvidos apenas quando há exatamente um candidato inequívoco; múltiplos candidatos são tratados como não resolvidos para evitar acionar ou exibir alvo incorreto) falhavam na resolução de caminho,
   o cache para URIs/URLs nunca se invalidava sozinho (ticks fixo em 0 sem versionamento de schema) e blocos
   `catch {}` silenciosos ocultavam diagnósticos. Todo teste de ícones deve cobrir obrigatoriamente
   os dois cenários: "Upgrade" (com cópia do `IconCache` real do sistema) e "Instalação limpa" (com `IconCache` vazio).

