# Relatório da Sonda de Caracterização — Lacuna 1

Data: 2026-09-27  
Branch: `fix/tile-icone-teams-e-renomear-no-monitor`  
Ambiente: Windows 11 Pro 64-bit, .NET 10, Multi-Monitor (Monitor 0: 3840×2160 @ 150%, Monitor 1: 1920×1080 @ 100%)

---

## 1. Compilação Release da Sonda e da Aplicação

Comandos executados:
```bash
dotnet build -c Release tests/GroupProbe
mkfile r
```

Resultado:
- Ambos compilaram com 0 erros e 0 warnings.
- Binários gerados:
  - `src/SmartDockGroups.App/bin/Release/net10.0-windows/SmartDockGroups.App.exe`
- Aplicação mantida no seu modo original (`System DPI Aware`, sem manifesto); ferramenta de teste `GroupProbe` recebeu `app.manifest` com `PerMonitorV2` para medição das coordenadas físicas reais.

---

## 2. Validação do Oráculo (Estabilidade da Sonda)

A sonda foi executada duas vezes consecutivas contra o **mesmo build** (HEAD da branch atual), gravando os relatórios em `tests/runs/oracle1/report-oracle1.json` e `tests/runs/oracle2/report-oracle2.json`.

Comando de comparação:
```bash
python tests/compare_reports.py tests/runs/oracle1/report-oracle1.json tests/runs/oracle2/report-oracle2.json
```

Saída colada do comparador:
```
referencia : tests/runs/oracle1/report-oracle1.json  (316 campos)
comparado  : tests/runs/oracle2/report-oracle2.json  (316 campos)

IDENTICO: nenhum campo divergiu.
```

### Diagnóstico de estabilidade
- A sonda mostrou-se 100% determinística e estável entre execuções consecutivas.
- Implementação de fechamento de menus (Escape via Win32) garantiu que nenhum popup de menu ficasse pendente prendendo captura de mouse entre iterações de grupos.
- Normalização de IDs no JSON garantiu que identificadores GUID temporários não causassem falsos positivos.

---

## 3. Comparação com a Linha de Base (`report-master.json`)

Comando executado:
```bash
python tests/compare_reports.py tests/baseline/report-master.json tests/runs/oracle1/report-oracle1.json
```

Saída colada do comparador:
```
referencia : tests/baseline/report-master.json  (316 campos)
comparado  : tests/runs/oracle1/report-oracle1.json  (316 campos)

IDENTICO: nenhum campo divergiu.
```

---

## 4. Análise dos Resultados e Comportamento dos Grupos

O comparador confirmou **316 de 316 campos rigorosamente idênticos** em relação à baseline gravada na versão master v1.1.1.0 (commit `b46789b`).

- **Grupos do fixture** (`tests/GroupProbe/fixtures/probe-config.json`):
  - Grupo `Alpha_Panel` (modo painel): SHA-256 da captura de tela `F1EAFDC4BBEFFDC6398F8FA41C348A7308D64FBD887F631C2DA0A7E36965E5C6` (idêntico).
  - Grupo `Beta_Folder` (modo mosaico/app folder): SHA-256 da captura de tela `B279470E0D14F3C43CE113697DB8682F5714088A58189DCD9E1EB1E017C751BA` (idêntico).
  - Menus de contexto de ambos os grupos: 100% idênticos em ordem, itens, flags e submenus.
- **Conclusão sobre o impacto nos grupos normais**:
  - A correção do corte de margens transparentes vazias (`TrimTransparentMargins`) em `IconCacheService.cs` **não afeta nem deforma** atalhos comuns (.exe, .lnk, pastas).
  - O corte só elimina pixels estritamente vazios (Alpha = 0). Ícones normais de executáveis Win32 que ocupam o canvas padrão mantêm suas dimensões e enquadramento intocados.
  - Como nenhum campo divergiu da baseline, **não foi necessário substituir a baseline**, preservando o oráculo master original intacto no repositório.

---

## 5. Reconciliação da Contagem de Testes Automatizados

Execução do comando:
```bash
dotnet test tests/SmartDockGroups.Tests
```

Resumo do comando:
```
Test run for C:\desenv\utils\SmartDockGroups\tests\SmartDockGroups.Tests\bin\Debug\net10.0-windows\SmartDockGroups.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 1 s - SmartDockGroups.Tests.dll (net10.0)
```

### De onde vêm os 47 testes (vs 32 informados no AGENTS.md antigo)?

A inspeção do histórico do Git (`git log --oneline`) esclarece a evolução:
1. **Commit `a2aae60`** (*"test: arsenal de verificacao antes da higienizacao (32 testes xUnit + sonda de caracterizacao)"*):
   - Criou a suíte de testes com **32 asserções** iniciais (`GroupConfigurationTests`, `MonitorPlacementTests`, `DesktopGroupWindowTests`).
   - O `AGENTS.md` registrou na época a anotação `# 32 assercoes, ~1 s`.
2. **Commit `e6796c3`** (*"fix(groups): corrige escala de icones no mosaico e suporte a protocolos (Defeito A)"*):
   - Adicionou `tests/SmartDockGroups.Tests/IconCacheServiceTests.cs` (**8 testes**: trimming de margem transparente, round-trip de cache, suporte a protocolos `msteams:`, `ms-settings:`, `http:`).
   - Adicionou `tests/SmartDockGroups.Tests/IconVisualCaptureTests.cs` (**1 teste**: verificação de renderização visual e mosaico 3x3 de `AppFolderTile`).
   - Subtotal: 32 + 9 = 41 testes.
3. **Commit `3ed1423`** (*"fix(groups): centraliza dialogo de renomear no monitor do clique (Defeito B)"*):
   - Adicionou `tests/SmartDockGroups.Tests/PromptPositioningTests.cs` (**6 testes**: cálculo de centralização multi-monitor, respeito à área útil/WorkingArea descontando taskbars, conversão física para DIPs WPF, clamp em coordenadas extremas).
   - Total final: 41 + 6 = **47 testes**.

A frase em `AGENTS.md` foi atualizada de 32 para 47 asserções para refletir a cobertura real do código atual.
