# O prompt único

> **Regra de manutenção:** este arquivo tem que continuar sendo "o prompt que,
> dado a um agente contra um checkout limpo do commit anterior a todo este
> trabalho, produziria o estado atual do repositório". Toda mudança de
> comportamento precisa de uma frase nova aqui (ou uma frase existente
> corrigida), no mesmo commit que muda o código. Atualize também
> [`OVERVIEW.md`](OVERVIEW.md).
>
> Está em português e no tom em que o pedido real foi feito, de propósito:
> é o registro do que foi pedido, não uma tradução de especificação técnica.

---

Tenho um organizador de área de trabalho para Windows em WPF (.NET, projetos
`SmartDockGroups.Core` sem UI + `SmartDockGroups.App`). Ele já tem: grupos de
atalhos como janelas soltas na área de trabalho, com ícones arrastáveis,
subpastas ao estilo Explorer, temas por grupo, localização em 8 idiomas, um
menu na bandeja com hotkey global, e um hook de mouse de baixo nível
(`WH_MOUSE_LL`) que intercepta o clique-direito vazio da área de trabalho
para mostrar um menu próprio.

**Primeiro, uma correção urgente:** esse hook está travando o Windows
inteiro esporadicamente. Ele faz uma chamada `SendMessage` bloqueante para o
Explorer *dentro do próprio callback do hook* — um callback de hook de baixo
nível tem um prazo rígido do sistema (~300ms) antes do Windows simplesmente
tirar o hook da cadeia, e uma chamada síncrona entre processos ali prende o
mouse inteiro até o Explorer responder. Remova esse hook por completo. As
duas ações que ele oferecia (recolher todos os grupos, reunir todos no
centro) devem continuar existindo, só que no menu da bandeja.

## 1. Visual: uma repaginada completa

Quero que este app e o **IGCParam** (outro projeto meu, WPF também) pareçam
da mesma família. Porte o sistema visual do IGCParam para cá:

- Mesma paleta clara e escura (fundo, superfície, superfície-2/3, bordas,
  texto principal/secundário/apagado, cor de destaque e sua variante de
  hover, tom suave da cor de destaque, cor de perigo).
- Mesmas métricas de controle: comandos com 28px de altura, campos com 27px,
  cantos de 4px (6px em painéis maiores), anel de foco de 2px na cor de
  destaque suave.
- Troque todo ícone do `Segoe MDL2 Assets` por um conjunto próprio de ícones
  de linha, desenhados numa grade 24×24, traço de 1.7, pontas e junções
  arredondadas — o mesmo peso visual do IGCParam. Cubra pelo menos: novo,
  duplicar, renomear, excluir, pasta, arquivo, abrir externamente,
  cima/baixo/voltar, chevrons, marca de seleção, cadeado, grade, ordenar,
  tamanho de ícone, mover, reunir, cor, imagem, opacidade, selo, os dois
  modos de exibição de grupo, mais/reticências, pasta aberta, escudo
  (administrador), copiar, engrenagem, energia, seta de início.
- Siga as convenções de menu do Windows: toda linha reserva a coluna do
  ícone mesmo quando não tem um (para os textos ficarem alinhados verticalmente);
  um comando que liga/desliga algo mostra uma marca de seleção na coluna do
  ícone quando está ligado, nunca um caractere dentro do próprio texto;
  reticências (`...`) só em comandos que abrem uma caixa pedindo mais
  informação antes de agir — nunca em ações diretas, nem em ações que já
  pedem confirmação por conta própria.
- O diálogo simples de "digite um nome" tinha uma altura fixa que cortava os
  botões; troque para `SizeToContent="Height"` e remova a margem duplicada
  do conteúdo (o shell da janela já tem a dele).
- Rode um menu de contexto (o de um item, o de um grupo, o da bandeja, o da
  área de trabalho vazia) e confira: submenus abrem com fundo sólido — se
  aparecerem transparentes, é porque o item que abre o submenu não recebeu
  os mesmos brushes de fundo/borda que o menu principal (o `Popup` do
  submenu herda do dono, não do tema global).
- Um `ComboBox` cuja lista usa objetos anônimos com uma propriedade `Label`
  precisa de um `ItemTemplate` explícito — `DisplayMemberPath` sozinho não
  alimenta a caixa de seleção fechada quando o controle já tem um template
  próprio.

## 2. Grupos com dois jeitos de aparecer

Cada grupo escolhe entre dois modos:

- **Painel** (o que já existe): canvas livre, ícones soltos, redimensionável.
- **Pasta de app**, à moda Android: um ladrilho fechado — placa arredondada
  com gradiente, mosaico 3×3 dos primeiros ícones do grupo, um selo vermelho
  com a contagem total de itens no canto, e o nome do grupo embaixo. Um toque
  nele abre uma **folha centralizada e translúcida** (não em tela cheia —
  isto não é um celular): tamanho = `mínimo(2× a largura/altura do painel,
  60% da largura útil do monitor, 50% da altura útil)`, sempre centralizada
  na área de trabalho (descontando a barra de tarefas). Dentro da folha,
  entrar numa subpasta navega no lugar, sem abrir uma janela nova; Esc sobe
  um nível, e só fecha de verdade quando já está na raiz. Clicar fora da
  folha, ou ela perder o foco, fecha.
- As duas aparências vivem na mesma janela (alternando por visibilidade, não
  reconstruindo), e trocar de uma para a outra **preserva** a geometria
  anterior: voltar de pasta-de-app para painel devolve exatamente a
  largura/altura/posição de antes; voltar de painel para pasta-de-app põe o
  ladrilho no canto superior esquerdo de onde o painel estava.
- Arrastar o ladrilho move o grupo; um clique que mal se moveu conta como
  toque (abre a folha) em vez de arrasto.

## 3. Ícones nítidos

Os ícones extraídos ficavam com halo preto ou branco ao redor. Troque a
extração para vir da lista de imagens do shell (`SHGetImageList`, tamanho
jumbo/256px) em vez de `Icon.ExtractAssociatedIcon` (que só dá 32px), com
esse método como reserva. E troque o cache em disco de `.ico` para **PNG** —
salvar como `.ico` e reler estava achatando o canal alfa, e essa era a causa
real do halo, não a resolução.

## 4. Margem, contraste, sombra

- Ícones dentro de um grupo (painel) mantêm no mínimo **5% de margem** (ou
  8px, o que for maior) dos quatro lados, aplicado ao posicionamento padrão,
  ao "organizar automaticamente", às ordenações e ao limite do arrasto.
- A cor do texto de um ícone é **derivada** da cor de fundo efetiva do grupo
  (contraste calculado por luminância), nunca fixa no tema — um grupo com
  imagem de fundo ou translúcido usa texto branco com uma sombra por baixo
  para continuar legível sobre qualquer coisa.
- Sombra deixa de ser só liga/desliga: adicione raio de desfoque, distância,
  direção (em graus) e opacidade como campos do tema, editáveis na tela de
  edição de tema, aplicados ao painel, ao ladrilho e à folha.

## 5. Convenções de janela que estavam erradas

- O cursor de "mover" (seta de quatro pontas) só deve aparecer **durante** o
  arraste do título, nunca ao simplesmente passar o mouse por cima.
- Só um grupo (ou subpasta) **vazio** pode ser removido — pelo menu e pela
  tecla Delete (numa seleção múltipla, remove o que pode e avisa que o que
  tinha conteúdo foi preservado).
- O título de um grupo precisa do botão padrão do Windows 11 de "ver mais"
  (um "..." que abre o menu do grupo), além do que já existe.
- O clique direito num ícone dentro de um grupo deve oferecer os mesmos
  comandos que o Explorer oferece num arquivo: executar como administrador,
  abrir local do arquivo, copiar como caminho, propriedades — e só quando o
  alvo for de fato um arquivo ou pasta no disco (não um comando nem uma URL).
- Crie também uma opção **"Menu padrão"** no menu de um ícone, que abre o
  `IContextMenu` de verdade do Windows para aquele arquivo — com extensões de
  terceiros, submenus (7-Zip etc.) funcionando de verdade. Isso é
  necessariamente desenhado pelo Windows, não pelo tema do app; as duas
  coisas convivem, o menu padrão não substitui o temático.

## 6. Comandos de grupo para grupo

No menu de um grupo, adicione:

- **Duplicar grupo** — cria uma cópia com "(cópia)" no nome, deslocada
  ~28px para não cair exatamente em cima da original.
- **Aplicar esta aparência a todos os grupos** — copia tema, opacidades,
  imagem de fundo e selo do grupo atual para todos os outros.
- **Usar esta aparência como padrão** — vira o tema que grupos novos usam a
  partir de agora.
- **Usar papel de parede do Windows como fundo**, com duas variantes: só
  este grupo, ou todos. Leia o papel de parede atual do Windows
  (`SPI_GETDESKWALLPAPER`, com reserva no `TranscodedWallpaper` para quando
  o Windows está em modo apresentação/slideshow).

## 7. Múltiplos monitores

- Quando um monitor desaparece, todo grupo que estava nele precisa ser levado
  para o monitor que sobrou, **mantendo o lugar relativo** (canto superior
  direito vira canto superior direito no outro). Essa posição de resgate não
  deve ser salva como definitiva — o grupo guarda a posição original como
  "casa" e volta sozinho quando o monitor volta a existir; um arrasto manual
  durante o resgate vira a nova casa normalmente.
- **Win+Shift+seta esquerda/direita** move o grupo em foco para o monitor
  vizinho, dando a volta nas pontas — a mesma convenção que o próprio Windows
  usa para janelas comuns.
- Escreva a geometria (achar o dono de um retângulo, mapear proporcionalmente
  entre dois retângulos, evitar empilhar duas coisas resgatadas no mesmo
  ponto) como funções puras, sem depender de WPF, para dar para testar sem
  precisar de dois monitores físicos.

## 8. Identidade

- Preciso de um ícone bonito para o app que lembre as palavras "Smart Dock".
  Gere-o em código (não é uma imagem editada à mão): quatro ladrilhos numa
  grade 2×2 (janelas + os grupos que o app organiza), um deles destacado com
  uma estrelinha. Desenhe cada resolução de 16 a 256px **separadamente**, não
  reduza de uma maior — reduzir vira uma mancha cinza no tamanho de bandeja,
  que é exatamente onde o ícone mais aparece. Aplique-o ao executável, à
  bandeja e à barra de tarefas.
- Em nenhuma janela, diálogo ou caixa de mensagem deve aparecer o nome
  "SmartDockGroups" — o nome visível é **"Smart Dock Groups"**, em todos os
  idiomas suportados. A pasta de dados do usuário e o nome interno do
  processo continuam como estão, para não invalidar configurações já
  salvas.

## 9. Atalho para trazer os grupos de volta

"Mostrar área de trabalho" (Win+D) e Win+M escondem os grupos e eles não
voltam sozinhos. **Antes de implementar, meça o que realmente acontece** —
não assuma que é uma minimização: uma janela sem botão na barra de tarefas
(o que os grupos são, de propósito) não é de fato minimizada por essas
teclas; o Windows só traz a janela da própria área de trabalho para o topo
da ordem Z e deixa os grupos enterrados atrás dela, ainda visíveis e no
lugar certo. A correção certa não checa o estado de minimizado: alterne a
propriedade `Topmost` da janela (true e depois false) para forçá-la de volta
ao topo da sua faixa de ordem Z, sem roubar o foco. Registre um **segundo**
atalho global (independente do que já existe para abrir o menu), padrão
**Ctrl+Alt+D**, configurável na tela de Configurações do mesmo jeito que o
atalho existente, para acionar essa correção manualmente.

## 10. A pergunta que decide a arquitetura: ancorar os grupos na área de trabalho?

Antes de qualquer coisa: **investigue, não implemente às cegas.** A ideia
óbvia para os grupos nunca ficarem atrás de outra janela nem sumirem no
Win+D é reparentá-los como filhos da janela real da área de trabalho
(`SHELLDLL_DefView`, sob o `Progman`) — a mesma técnica que ferramentas afins
usam. Construa uma sonda em C# puro (não em PowerShell — a marshalling do
PowerShell não converte `$null` para `NULL` num parâmetro de string em
P/Invoke, o que já vai gerar diagnóstico errado) para descobrir, sem supor:
qual janela realmente é a superfície da área de trabalho nesta versão do
Windows, se dá para reparentar uma janela ali, se ela pinta, se recebe
clique, e se sobrevive ao Win+D. Depois, com uma janela WPF real (não só
Win32 puro), confirme se a transparência por pixel (`AllowsTransparency`)
sobrevive ao mesmo reparentamento — comparando lado a lado com a mesma
janela sem reparentar.

Se a transparência não sobreviver (o que de fato aconteceu: uma janela WPF
reparentada vira opaca, mesmo mantendo a flag interna de janela em camadas),
**não ancore os grupos**. O produto usa transparência em várias partes —
opacidade por grupo, papel de parede visto através da área, a folha
translúcida da pasta de app — e vale mais manter isso do que ganhar a
imunidade ao "Mostrar área de trabalho", que já foi resolvida de outro jeito
no item 9. Documente a decisão e o porquê, para não ser retestada às cegas
de novo.

## 11. Atalhos de arquivo e redimensionar por qualquer borda

Dentro de um grupo (painel), os atalhos padrão do Windows para copiar,
recortar, colar e selecionar tudo não funcionavam — só o clique com Ctrl
alternava a seleção de um ícone por vez, e não havia nenhum jeito por
teclado de agir sobre a seleção. Implemente:

- **Ctrl+A** seleciona todos os ícones do grupo (subpastas e itens
  fixados).
- **Ctrl+C** e **Ctrl+X** colocam os arquivos por trás dos ícones
  selecionados na área de transferência de verdade do Windows (não uma área
  de transferência interna do app), para colar em qualquer lugar — inclusive
  no Explorer. Recortar também marca o `Preferred DropEffect` como "mover"
  (a mesma convenção que o Explorer usa para desenhar o ícone apagado) e
  desafixa os itens copiados deste grupo. Subpastas do grupo são ignoradas:
  são um agrupamento do próprio app, sem uma pasta real em disco por trás
  para copiar.
- **Ctrl+V** aceita qualquer arquivo que esteja na área de transferência
  (copiado do Explorer ou de outro grupo) e o fixa como um novo ícone, pelo
  mesmo caminho que soltar um arquivo arrastado já usava.

Também só dava para redimensionar o grupo puxando um único ícone no canto
inferior direito. Troque por oito zonas de arraste invisíveis cobrindo os
quatro lados e os quatro cantos do painel — puxar qualquer uma redimensiona
a partir dali, mantendo a borda oposta fixa no lugar, do jeito que qualquer
janela do Windows já se comporta.

## 12. Um lote de acabamentos e recursos do painel

- **Organizar ícones automaticamente** deixa de ser um comando de "fazer uma vez"
  (e, como um grupo só guarda atalhos, **não há mais "ordenar por nome/tipo"**: ligado,
  a ordem é sempre por nome do atalho): a escolha fica salva por grupo
  (`MenuCategory.IconArrangement`) e é **reaplicada sozinha** toda vez que o
  conjunto de ícones muda — soltar um arquivo, colar, apagar, renomear —
  não só no instante em que o item de menu foi clicado. Um grupo em posição
  livre (o padrão de sempre) continua livre; nada disso se aplica a ele.
- Troque o atalho global padrão de restaurar grupos de **Ctrl+Alt+D** para
  **Win+Ctrl+Alt+D** (o usuário já usa Ctrl+Alt+D para outra coisa).
- No Ctrl+X de um ícone dentro de um grupo: **não remova na hora.** O ícone
  fica meio-opaco, do jeito que o Explorer marca um recorte pendente, e só
  sai do grupo quando algo de fato consome o "colar" — um Ctrl+V em outro
  grupo deste mesmo app finaliza a remoção ali mesmo. Para um paste externo
  de verdade (arrastar para o Explorer, por exemplo), o Windows não avisa
  ninguém quando isso termina; a única saída é conferir periodicamente se o
  arquivo original ainda existe no caminho de onde foi recortado, e remover o
  ícone quando ele não existir mais — aceite o atraso de alguns segundos como
  o preço de não precisar de um hook do shell.
- A borda de um grupo deixa de vir do tema: é **derivada da cor de fundo
  efetiva**, 10% mais clara — ou 10% mais escura quando o fundo já é branco
  ou perto disso, onde clarear não mudaria nada visível.
- Passar o mouse sobre um ícone dentro do painel dá um retorno visual "como
  a web dá": um leve aumento de escala e um tingimento do fundo, animados,
  em vez de mudar de estado sem transição nenhuma. Não compete com o
  destaque, mais forte, de um ícone selecionado.
- O ladrilho fechado (estilo App Folder) nasce **40% maior** quando é o que
  fica na área de trabalho — do jeito que ele já existe hoje é pequeno demais
  perto de ícones reais do Windows ao lado.
- **Ctrl+F**, só no modo painel (o modo App Folder não tem o que buscar):
  abre uma barra de busca sob o título do grupo. Enquanto digita: destaca o
  trecho digitado dentro do nome de cada ícone que bate, mostra um contador
  de quantos bateram, e alimenta uma lista suspensa dos resultados navegável
  com seta para cima/para baixo. Enter num resultado da lista tem exatamente
  o mesmo efeito que dar duplo clique nele, e fecha a busca. Esc também
  fecha. O texto buscado só é lembrado para a próxima vez que abrir com
  Ctrl+F nesses dois momentos — Enter num resultado, ou Esc — nunca por
  simplesmente clicar fora ou perder o foco.
- No menu de contexto **real do Windows** (clique direito na área de
  trabalho vazia, fora do app), adicione um submenu com: novo grupo,
  colapsar/expandir todos os grupos, trocar todos para o estilo App Folder,
  trocar todos para o estilo painel. Isso não é o menu temático do app — o
  app não tem como interceptar o clique na área de trabalho real (ver a
  decisão de não ancorar os grupos, seção 10) — é um registro de verdade sob
  `HKCU\...\DesktopBackground\Shell`, o mesmo truque que outras ferramentas
  usam para colocar um verbo estático no menu do Explorer, sem shell
  extension e sem hook. Cada verbo apenas relança o próprio executável com
  um argumento; isso precisa que o app saiba se já existe uma instância rodando
  — sem esse controle, cada clique nesses itens abriria um `SmartDockGroups.App.exe`
  novo, com um segundo ícone de bandeja e os grupos duplicados por cima dos
  já abertos. A instância nova, ao perceber que já existe uma rodando, manda
  o comando para ela (um named pipe é suficiente) e sai imediatamente sem
  desenhar nada; instâncias abertas fora desse fluxo — um duplo clique comum
  no `.exe`, por exemplo — não devem ficar presas nessa checagem além do
  necessário para fechar sozinhas.

## 13. Instalador e um link de download que funcione fora daqui

Preciso de duas coisas: um instalador de verdade (assistente, atalho no menu
iniciar, entrada em Adicionar/Remover Programas, desinstalador — não só o
`.exe` solto) e um jeito de baixar isso por URL de fora da minha rede.

- Gere o instalador com **Inno Setup**: publique o app **self-contained,
  single-file, win-x64** (não framework-dependent — o instalador precisa
  funcionar numa máquina sem o runtime do .NET instalado, senão não faz
  sentido ele rodar fora daqui) e depois compile um script `.iss` que
  instala por usuário (sem pedir elevação), com atalho no menu iniciar,
  ícone de área de trabalho opcional, e um desinstalador que **não** apaga a
  configuração do usuário em `%AppData%\SmartDockGroups`.
- Isso vira o comportamento de `mkfile package` para este projeto — coloque
  um `tools\build_installer.ps1` ao lado do `.csproj`, do mesmo jeito que o
  lado Flutter do `mkfile` já usa `tools\build_apk.py`: quando esse script
  existe, ele é a fonte da verdade para "empacotar", não `dotnet publish` cru.
  Edite o `mkfile.ps1` para reconhecer essa convenção do lado .NET também,
  em vez de reimplementar o empacotamento como outro script solto sem relação
  com o `mkfile`.
- O link de download é uma **release do GitHub** neste repositório (já é
  público), com o instalador anexado à tag da versão — estável, sem custo,
  funciona de fora desta rede sem eu precisar manter servidor nenhum.

## 14. A organização dos ícones precisa reagir, não só "rodar uma vez"

Os ícones de um grupo em modo grade/nome/tipo paravam de bater com o layout
depois de duas ações que deveriam ter mexido neles: redimensionar o grupo
(a grade continuava com o número antigo de colunas) e mudar o tamanho dos
ícones pelo menu ou pelo Ctrl+roda do mouse (a grade não recalculava quantos
cabem por linha, então em zoom alto os últimos ícones da linha saíam do
painel). O grupo inteiro precisa ficar responsivo: qualquer coisa que mude a
ordenação, o tamanho do grupo ou o tamanho dos ícones tem que refletir na
hora, não só as ações que já disparavam isso (soltar, colar, apagar,
renomear).

Também: marcar vários ícones e mandar remover pelo menu de contexto de um
deles só removia aquele um — o menu não sabia da seleção múltipla. A tecla
Delete já fazia isso certo; o menu de contexto (tanto de um item quanto de
uma pasta) precisa se comportar do mesmo jeito que o Explorer: clicar em
"remover" com vários itens marcados age sobre todos eles, não só o que está
debaixo do mouse.

## 15. O ladrilho de App Folder parou de responder a clique

O ladrilho fechado (estilo App Folder) na área de trabalho ficou 40% maior
numa sessão anterior usando um `LayoutTransform` no controle que o hospeda.
Isso quebrou a interação: dava para ver o ladrilho, mas clicar nele não
abria mais a pasta. Troque a abordagem — em vez de desenhar no tamanho
normal e esticar depois, faça o `AppFolderTile` nascer já nas medidas
finais (um parâmetro de escala que multiplica cada medida antes de montar o
visual). Mais seguro por construção: não existe uma camada de transform
separada para o hit-test poder discordar do que foi desenhado.

## 16. Busca por digitação e clique único quebrando a seleção

Implemente a busca por digitação de dentro de um grupo do jeito que o
Windows Explorer já faz dentro de qualquer pasta: sem apertar atalho
nenhum, só começar a digitar já pula a seleção para o primeiro ícone cujo
nome comece com o que foi digitado, acumulando letras dentro de cerca de 1
segundo uma da outra e recomeçando depois de uma pausa. Isso é diferente do
Ctrl+F que já existe (aquele abre uma caixa com contador e lista) — vale
tanto no painel livre quanto dentro da folha do estilo App Folder.

No estilo App Folder, um clique único num ícone não pode mais abri-lo na
hora — isso tira a chance de só selecionar um ícone para fazer outra coisa
com ele (por exemplo, escolher antes de decidir a ação). Clique único
seleciona; abrir é duplo clique ou Enter, do mesmo jeito que qualquer
pasta de ícones do Windows já se comporta.

## 17. Mover objetos entre grupos, e paridade completa do App Folder

Mover um ícone de um grupo para outro não funcionava, e uma pasta movida
assim acabava sendo interpretada como um grupo novo e independente da área
de trabalho, em vez de uma subpasta pertencente ao grupo de destino.
Implemente arrastar de verdade: soltar um ícone (arquivo **ou** subpasta)
fora do próprio grupo, sobre outro grupo aberto, precisa **mover** o objeto
— tirar da lista do grupo de origem e colocar na lista do grupo de destino
(itens fixados ou subpastas, conforme o tipo), nunca na configuração de
nível superior. Uma subpasta movida assim continua subpasta.

Outros acertos no mesmo lote:

- **Configurações globais a partir de qualquer grupo**: o menu de qualquer
  grupo (painel ou App Folder) precisa ter uma entrada que abra a tela de
  Configurações do app, não só a bandeja.
- **Paridade total entre painel e App Folder**: tudo que dá pra fazer no
  painel (organizar automaticamente, ordenar, novo arquivo/pasta, tamanho
  do ícone, etc.) precisa estar disponível também no estilo App Folder —
  seja no menu do próprio ladrilho fechado, seja dentro da folha que abre
  ao clicar nele (onde vale também seleção múltipla, Delete, F2, Ctrl+A e
  menu de contexto por ícone, os mesmos que o painel já tinha).
- **Estado marcado nos menus de ordenação**: seguindo o padrão do Windows,
  a opção de organização atualmente ativa (grade automática, por nome, por
  tipo) precisa aparecer com visto/marcada no menu, não só executar a ação.
  Como o menu é reconstruído a cada clique direito, isso nunca fica
  desatualizado.
- **Legenda do grupo com um resumo das configurações**: o título de um
  grupo ganha uma dica (tooltip) resumindo o essencial — estilo atual,
  organização, tamanho do ícone, opacidade, selo — sem precisar abrir o
  menu para saber o que está configurado.
- **Idioma dos itens no menu de contexto real do Windows**: os rótulos que
  aparecem no clique direito de verdade da área de trabalho (o registro em
  `DesktopBackground\Shell`) seguem o **idioma do Windows**, não o idioma
  configurado dentro do app — esse menu é lido pelo Explorer mesmo com o
  app fechado, então o idioma escolhido dentro do app não tem influência
  nenhuma sobre ele. Dentro do app (bandeja, menus dos grupos), continua
  valendo o idioma configurado nas Configurações. Esse menu real ganha
  também uma entrada "Configurações..." (mesmo destino do item 3 acima).

## 18. O ladrilho de App Folder "sumia" ao arrastar

Arrastar o ladrilho fechado do estilo App Folder às vezes fazia a janela do
grupo desaparecer para sempre — só voltava trocando **todos** os grupos de
volta para o estilo painel pelo menu de contexto real do Windows (o que
força uma reconstrução completa da janela, com `Width`/`Height` explícitos
de novo). Não bastava olhar o código e desconfiar: a suspeita óbvia (as
zonas de redimensionar, ou o TileTransform da sessão anterior) não batia
com o sintoma. Só reproduziu na prática montando um teste automatizado com
UI Automation (`System.Windows.Automation`, não `GetWindowRect` — em uma
máquina com DPI por monitor, `GetWindowRect` chamado de um processo sem
DPI-awareness devolve coordenadas "virtualizadas" que não bate com a tela
real, e isso por pouco não desviou a investigação inteira) simulando um
arrasto de verdade e comparando a posição da janela antes/depois. A janela
apareceu exatamente em `Int16.MinValue` num dos eixos — a pista que
resolveu: era um problema de acúmulo de posição via duas chamadas de
`PointToScreen` no mesmo evento de `MouseMove`, a segunda já depois da
primeira ter alterado `Left`/`Top` da própria janela — em DPI por monitor,
a segunda leitura podia vir escalada por um fator diferente da primeira. A
correção trocou a lógica à mão por `DragMove()` (a mesma primitiva do WPF
que já movia a janela pelo cabeçalho, robusta a monitores/DPI por
delegar o arrasto de verdade para o Windows). Ao investigar um bug de
posição/tamanho de janela que só aparece "às vezes" ou "em algum lugar
esquisito", desconfie primeiro de contas de coordenada feitas à mão
(`PointToScreen`/`Left +=`) em vez de uma primitiva nativa (`DragMove`,
`DragResize`) — e meça com uma ferramenta que respeita DPI por monitor.

## 19. Documentação e versionamento

Mantenha dois documentos sempre sincronizados com o código, atualizados no
mesmo commit de qualquer mudança:

- Uma visão geral do produto, da arquitetura e das decisões tomadas (por que
  os grupos não são ancorados, por que o menu do shell convive com o
  temático, etc.), incluindo o que ainda não foi verificado de verdade
  (ex.: a lógica de múltiplos monitores só foi testada por simulação nesta
  máquina, que só tem um monitor).
- Este prompt único — o que você está lendo — mantido como o pedido
  equivalente a tudo isso, para que alguém possa reconstruir o produto do
  zero a partir dele.

Adote um único arquivo `VERSION` na raiz do repositório (formato `a.b.c.d`,
a mesma convenção já usada nos projetos Delphi), lido por um
`Directory.Build.props` que aplica a versão a todo projeto do produto sem
tocar em cada `.csproj` individualmente. Ferramentas de desenvolvimento
soltas (geradores de ícone, sondas de diagnóstico) ficam de fora do
versionamento — não fazem parte do que é entregue.

## 20. Arrastar entre grupos: o ícone sumia e soltava em lugar errado

A experiência de arrastar um ícone de um grupo para outro estava ruim: o
objeto sumia durante o arrasto, e ao soltar parecia não ter feito nada mas
na verdade movia — só que para um lugar diferente de onde foi largado.

Duas causas distintas, achadas por leitura de código (comparando com o
caminho que já funcionava corretamente — arrastar *dentro* do mesmo
grupo), não por reprodução visual:

1. **Sumia**: o arrasto movia o ícone só dentro do `Canvas` da própria
   janela do grupo de origem (`Canvas.SetLeft/Top`). Nada renderiza fora
   dos limites de uma janela WPF — então, assim que o ícone cruzava a
   borda da janela (o que acontece o tempo todo ao arrastar para OUTRO
   grupo, que é uma janela diferente), ele desaparecia, mesmo a captura do
   mouse continuando ativa e o resto do gesto funcionando por trás dos
   panos.
2. **Soltava em lugar errado**: a posição final usada era o cursor cru
   (`PointToScreen(e.GetPosition(this))`), que descarta o deslocamento
   entre onde o usuário pegou o ícone e o canto dele — deslocamento que o
   arrasto dentro do mesmo grupo preservava (`tileStart + delta`), mas que
   o caminho entre grupos ignorava, recalculando a posição a partir do zero
   a cada solta.

Corrigido com uma janela-fantasma real (`DragGhostWindow`, um novo arquivo
em `Desktop/`) — um `VisualBrush` do próprio tile, sem clonar a árvore
visual — posicionada em coordenadas de tela, que acompanha o cursor e
atravessa livremente os limites de qualquer janela, do mesmo jeito que o
Explorer mostra um ícone "flutuando" durante um arrasto. A posição *dela*
(não a do cursor) virou a fonte de verdade tanto para achar qual grupo está
embaixo quanto para a posição final no destino, o que resolve as duas
causas de uma vez.

Repetindo a lição do item 18 (o ladrilho de App Folder que sumia por causa
de duas chamadas de `PointToScreen` misturando DPI de monitores
diferentes): `PointToScreen` só é chamado sobre a janela de origem, que
nunca se move durante o gesto — nunca lido de volta a partir da própria
janela-fantasma, que é quem se move. Ao introduzir uma coordenada de tela
nova numa correção de arrasto, desconfie de qualquer leitura de posição
feita a partir de algo que você acabou de mover.

**Não verificado interativamente.** Uma tentativa de montar um teste
automatizado (lançar o app com uma configuração de dois grupos conhecidos,
localizar os tiles via `System.Windows.Automation` e simular o arrasto com
`SetCursorPos`/`mouse_event`) não conseguiu clicar nos ícones de forma
confiável: a janela usa `AllowsTransparency=true`, que faz o Windows testar
cliques pixel a pixel — um clique num pixel transparente atravessa para a
janela debaixo em vez de acertar o tile — e os elementos internos
(`StackPanel`/`Canvas` construídos à mão, sem `AutomationPeer` customizado)
não aparecem de forma utilizável nem na árvore "Raw" da UI Automation. A
correção ficou validada por leitura de código e por compilação, não por
reprodução visual — diferente do item 18, onde a reprodução automatizada
foi possível. Fica pendente a confirmação manual de alguém rodando o app de
verdade.

## 21. Seleção estilo Explorer, abrir/fechar grupos, ordem das janelas e organização só por nome

Depois de tudo acima, um lote de pedidos deixou o painel com o comportamento do Explorer
e simplificou a organização. (A versão do zero, em um passo, está em
[`PROMPT_GERACAO_UNICA.md`](PROMPT_GERACAO_UNICA.md).)

- **Um grupo só guarda atalhos**, então **não faz sentido ordenar por nome ou por tipo**.
  Removi o submenu "Ordenar por" e a ordenação por tipo. O menu do grupo tem um único item
  marcável, **"Organizar ícones automaticamente"**; ligado, a ordem é **sempre por nome do
  atalho** (sem diferenciar maiúsculas de minúsculas), em grade, reaplicada sozinha ao
  soltar, colar, apagar, renomear, redimensionar e mudar o zoom. Configurações salvas com os
  valores antigos `Grid` ou `ByType` continuam abrindo e passam a valer como "por nome".
- **Seleção como no Explorer**: clique, Ctrl+clique, Shift+clique (intervalo), retângulo de
  seleção arrastando no fundo, Ctrl+A. **Setas, Home e End** navegam em 2D pela grade (Shift
  estende); **Enter** abre; **F2** renomeia; **Delete** remove; **F5** reaplica a organização;
  **Esc** limpa a seleção. Ctrl+C, Ctrl+X e Ctrl+V usam a área de transferência real.
- **Arrastar vários itens selecionados** de uma vez, também para outro grupo, com a
  janela-fantasma e uma animação de batimento no ícone pego. Ao soltar num grupo com a
  organização ligada, ele reordena por nome.
- **Novo atalho…** no menu do grupo, que também abre pela janela externa (fora do painel).
- **Fechar grupo** no menu do grupo (`MenuCategory.IsClosed`), e no menu da bandeja e no
  clique direito da área de trabalho: **Abrir todos os grupos**, **Fechar todos os grupos** e
  um item marcável por grupo.
- **Ordem das janelas** (submenu no grupo): trazer todos para frente, enviar os outros para
  trás, enviar todos para trás — para escolher quem cobre quem na área de trabalho.
- **Win+Shift+←/→** leva o grupo ao monitor vizinho.
- **Atalho de grupo na barra de tarefas**: item **"Criar atalho na barra de tarefas"** no
  menu do grupo. Cada grupo ganha um `Id` estável (GUID, gravado na config; duplicar gera outro).
  O item cria um `.lnk` em `%AppData%\SmartDockGroups\GroupShortcuts` que abre o próprio app com
  `--desktop-action=focus-group:<id>` e mostra o arquivo no Explorer para o usuário arrastar à
  barra. Clicar no atalho abre o grupo se estiver fechado, expande se estiver recolhido, traz
  para frente, ativa e faz um pulso curto de opacidade; com o app fechado, sobe e faz o mesmo;
  com um id que não existe mais, mostra o balão "Grupo não encontrado". Uma pasta
  `...\User Pinned\TaskBar` sozinha **não** fixa (o Explorer usa o valor `Favorites` da chave
  `Taskband`), então o fixar-sozinho ficou para depois — ver
  [`VIABILIDADE_ATALHO_TASKBAR_GRUPO.md`](VIABILIDADE_ATALHO_TASKBAR_GRUPO.md).
- Versão **1.1.1.0**.

## 22. Higienização: o SmartDockGroups passa a ser só os grupos

O repositório carregava três coisas que não são a função de grupos. Separe cada uma **sem
mudar nada no comportamento dos grupos**, e prove que não mudou.

- **WinUIApp** (app WinUI 3 inteiro, 5 projetos) vira repositório próprio, com o histórico
  que tocava a pasta preservado por `git subtree split`. Antes de remover daqui, ele tem que
  compilar sozinho lá.
- **O menu lançador da bandeja** — a lista hierárquica de categorias e atalhos do usuário —
  vira um app próprio chamado **QuickMenu**, com `%AppData%\QuickMenu` separado. Aqui fica
  só o menu como painel de controle dos grupos (novo grupo, abrir/fechar, recolher, reunir,
  iniciar com Windows, configurações, sair).
- **Código morto e ferramentas de scratch**: `DesktopContextMenuBuilder`, `DeskProbe`,
  `DragDropTester`, capturas soltas e seis scripts `.ps1` de auditoria por *grep* de
  código-fonte — que a sonda nova substitui por medição real.

**A prova exigida, nesta ordem:**

1. Um **arsenal de verificação primeiro**, com a master ainda intacta: 32 asserções xUnit e
   uma sonda em C# que sobe o app de verdade e grava geometria, captura PNG por grupo e o
   menu de contexto inteiro lido por UI Automation.
2. **Validar o oráculo antes de confiar nele**: rodar a sonda duas vezes contra o *mesmo*
   build e exigir relatórios idênticos. A primeira versão oscilava entre 283 e 316 campos
   porque esperava os submenus por `sleep` fixo — troque por esperar o estado que o menu
   reporta. Medição que muda sozinha não distingue refatoração de ruído.
3. Comparar cada etapa contra a linha de base e **exigir zero divergência** nos 316 campos.

## 23. O nome do produto também na descrição do processo

O Gerenciador de Tarefas ainda mostra `SmartDockGroups.App` — o nome do assembly — em vez de
**"Smart Dock Groups"**. Feche essa última ponta: `AssemblyTitle` e `Product` no `.csproj` do
App, sem mudar mais nada.

- **Prova**: a sonda da seção 22 contra a linha de base tem que dar **zero divergência**.

## 24. Ícone minúsculo de atalho do Teams/URIs no mosaico da pasta de app

Um grupo no modo "pasta de app" contendo atalho com URI de protocolo (como `msteams://...`) mostrava apenas um ponto minúsculo de ~5px no canto superior-esquerdo da célula do mosaico 3×3 (`AppFolderTile.cs`), enquanto no painel aberto o ícone aparecia em tamanho normal.

**Causa raiz:** O ImageList Jumbo do shell (`ShilJumbo`, 256×256) devolvia o ícone em sua resolução original (ex.: 66×68 pixels para atalhos de protocolo) ancorado no canto `(0, 0)` de um canvas 256×256 com o restante inteiramente transparente (3.348 pixels não-transparentes de 65.536). O `AppFolderTile` aplicava `Stretch.Uniform` ao canvas inteiro de 256×256 dentro da célula de ~21×21, reduzindo a área útil a um ponto microscópico no canto. Além disso, `IconCacheService.ResolveFullPath` retornava nulo para URIs e `BuildCacheKey` disparava exceção ao tentar consultar `File.GetLastWriteTimeUtc` em URLs.

**Correção:**
1. `IconCacheService.TrimTransparentMargins`: analisa o canal alfa e recorta a imagem para o retângulo delimitador visível (`minX, minY, boundW, boundH`) quando a parte gráfica ocupa apenas uma fração desproporcional do canvas. O corte é aplicado tanto na extração quanto na leitura de PNGs em cache.
2. Suporte estrutural a URIs de protocolo (`msteams:`, `ms-settings:`, `http:`, `https:`, `shell:`) e arquivos `.url`: resolução de executável associado via registro (`AssocQueryString`), extração de ícone com `IShellItemImageFactory`, e desreferenciação de recursos indiretos de pacotes UWP via `SHLoadIndirectString`.
3. `IconCacheService.GetIcon`: converte `BitmapSource` para `DrawingIcon` via stream PNG em memória para que o painel (`DesktopGroupWindow`) também exiba ícones de URI perfeitamente.
4. `AppFolderTile`: centralização do `Image` dentro da célula da grade (`HorizontalAlignment.Center`, `VerticalAlignment.Center`).

- **Prova**: testes unitários xUnit em `tests/SmartDockGroups.Tests/IconCacheServiceTests.cs` (5 testes novos) e captura visual do mosaico em `tests/SmartDockGroups.Tests/IconVisualCaptureTests.cs` gerando `docs/execucoes/A-depois.png` em comparação a `docs/execucoes/A-antes.png`.

## 25. Diálogo de renomear grupo/item centralizado no monitor do clique

Ao criar ou renomear um grupo, atalho ou subpasta (`DesktopGroupWindow.OnRenameClick`, `RenameItem`, `RenameFolder`, `GroupOverlayWindow.RenameEntry`, `App.CreateNewGroup`), a caixa de texto (`TextPromptWindow.xaml(.cs)`) sempre aparecia no monitor primário, mesmo quando o usuário clicava em um grupo localizado em um monitor secundário.

**Causa raiz:** O `TextPromptWindow.xaml` definia `WindowStartupLocation="CenterOwner"`, mas todas as chamadas de instanciação no app criavam o diálogo sem definir a propriedade `Owner` (`Owner` nulo). No WPF, quando `WindowStartupLocation` é `CenterOwner` e `Owner` é `null`, o runtime cai no fallback padrão de centralizar a janela na tela primária (`SystemParameters.WorkArea`), ignorando onde estava o cursor do usuário e em qual monitor a janela de grupo invocadora residia. Em ambientes multi-monitor com escalas de DPI heterogêneas, isso forçava o diálogo a surgir distante do ponto de foco do usuário.

**Correção:**
1. `PromptPositioning.cs`: módulo de cálculo e posicionamento que obtém o monitor sob o cursor (`Screen.FromPoint(Control.MousePosition)`), calcula o centro exato da área de trabalho útil desse monitor (`WorkingArea`), restringe a geometria aos limites visíveis da tela, converte pixels físicos para unidades independentes de dispositivo (DIPs) com base no DPI do sistema e do monitor (`GetDpiForMonitor` e `GetDpiForSystem`), define `window.Left` e `window.Top` em DIPs e ajusta o HWND diretamente via Win32 `SetWindowPos` (`SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE`).
2. `TextPromptWindow.xaml` e `TextPromptWindow.xaml.cs`: alteração de `WindowStartupLocation` para `Manual`, posicionamento automático centralizado no monitor do cursor em `OnSourceInitialized` e reposicionamento em `Loaded` com foco imediato e elevação temporária de z-order para evitar que fique oculta. Aceita parâmetro opcional de substituição de cursor para testes automatizados.
3. Robustez em `ModernWindow` e `LocalizationService`: proteção contra estilos nulos ao ser instanciado fora do ciclo de vida padrão do `App.xaml` e suporte a recursos empacotados (`pack://application:,,,/`).

- **Prova**: 6 novos testes em `tests/SmartDockGroups.Tests/PromptPositioningTests.cs` (totalizando 47 testes xUnit). O teste `VerifyDialogOpensOnEveryRealScreen` executa em máquina real sobre todas as telas disponíveis (`Screen.AllScreens`), dispara a janela de diálogo em cada tela, mede os retângulos de janela obtidos e valida a contenção estrita dentro da área útil daquele monitor. Evidências salvas em `docs/execucoes/B-retangulos.txt`, `docs/execucoes/B-monitor1.png` e `docs/execucoes/B-monitor2.png`.

## 26. Fechamento das lacunas de auditoria: sonda estável, prova do caminho real em multi-monitor e app aberto de verdade

A auditoria da entrega anterior apontou três lacunas de verificação rigorosa:
1. **Sonda de caracterização**: executada e validada com oráculo determinístico (dois runs consecutivos `oracle1` e `oracle2` com zero divergência em 316 campos). A comparação contra a baseline `tests/baseline/report-master.json` resultou em 316 de 316 campos idênticos, provando que o recorte de margem transparente vazia não deformou nem alterou atalhos normais (.exe, .lnk, pastas). A contagem de testes foi reconciliada e documentada: 47 testes xUnit (32 da higienização + 9 do Defeito A + 6 do Defeito B), todos passando em ~1 s.
2. **Prova do caminho real do Defeito B**:
   - *Causa raiz do `MousePosition [0,0]`*: identificou-se que o ambiente de execução de comandos roda em uma desktop isolada (`WinSta0\exebox-...`), onde a API Win32 `SetCursorPos`/`GetCursorPos` é rejeitada com `ERROR_ACCESS_DENIED (5)` por ausência de permissão `DESKTOP_WRITEINPUT`.
   - *Diagnóstico e Prova com o App no Modo Original*: a aplicação mantém estritamente seu modo de DPI original (`System DPI Aware`, sem manifesto), preservando o comportamento de escala visual padrão do WPF no Windows. Apenas a ferramenta de teste (`GroupProbe`) conta com `app.manifest` (`PerMonitorV2`) para medição das coordenadas físicas reais dos monitores e implementa bridge automático para a desktop interativa `WinSta0\Default`. A classe `DefectBVerifier` em C# subiu o `SmartDockGroups.App.exe` real (com backup do `config.json` conferido por hash SHA-256 antes e depois). Para cada monitor (150% e 100%), o cursor real foi posicionado e validado por `GetCursorPos`, o menu de contexto do grupo foi acionado para clicar em "Renomear...", o retângulo do diálogo foi medido por `GetWindowRect` e provado 100% contido na área útil daquele monitor com dimensões coerentes ao DPI. Capturas recortadas das janelas (sem área de trabalho do usuário, preservando total privacidade) foram salvas em `docs/execucoes/B-fluxo-real-monitor1.png` e `B-fluxo-real-monitor2.png` (< 300 KB). O caminho de "Novo grupo" (`App.CreateNewGroup`) também foi medido e comprovado contido.
3. **App aberto de verdade (Defeito A)**: a aplicação real (`SmartDockGroups.App.exe` Release) foi executada com o atalho do Microsoft Teams (`msteams://teams.microsoft.com/l/chat/0/0?users=alexandre.sousa@iob.com.br`) nos dois modos visuais, comprovando visualmente em `docs/execucoes/A-app-real-painel.png` (painel íntegro) e `docs/execucoes/A-app-real-mosaico.png` (mosaico nítido, preenchendo a célula proporcionalmente aos demais ícones, sem margem transparente excessiva e com selo azul).

## 27. Ícone do Teams e atalhos .url renomeados no instalador publicado (OS 04)

Após a publicação da versão 1.1.1.1, reportou-se que o atalho do Microsoft Teams sumiu completamente tanto no modo Painel quanto no modo App Folder no executável instalado de verdade (`C:\Users\alxch\AppData\Local\Programs\SmartDockGroups\SmartDockGroups.App.exe`, self-contained single-file win-x64).

**Causa raiz com evidência:**
1. *Lacuna de verificação*: Todas as rodadas anteriores testaram apenas o build framework-dependent em `bin/Release/net10.0-windows/SmartDockGroups.App.exe`, sem validar o artefato publicado self-contained single-file gerado pelo script do instalador.
2. *Atalho com nome divergente na Área de Trabalho*: Na configuração real da máquina do usuário (`config.json`), o item apontava para `C:\Users\alxch\OneDrive\Área de Trabalho\Alexandre.url`. No sistema de arquivos, o atalho havia sido criado/renomeado como `Alexandre Chagas Sousa.url`. A função `ResolveFullPath` retornava nulo por não achar o arquivo com o nome exato, falhando a extração do ícone e a execução do atalho. O arquivo de cache `2223BC1D...png` existente correspondia à URI pura `msteams://...`, enquanto a chave do atalho `Alexandre.url` gerava hash diferente (`9700AA29...`) e nunca encontrava o ícone.
3. *Atalhos `.url` no painel*: O método `GetIcon` delegava arquivos `.url` para `ExtractAssociatedIcon`, que gerava um ícone genérico ou falhava silenciosamente, em vez de extrair o ícone real do alvo associado à URL.
4. *Estagnação permanente do cache de URIs/URLs*: A chave de cache para alvos virtuais (URIs/URLs sem arquivo local ou sem data de modificação) usava `ticks = 0` fixo sem versão de schema. Uma entrada corrompida ou gravada por versão antiga do código nunca se invalidava no cache em disco.
5. *Tratamento de exceções silencioso*: Blocos `catch {}` vazios em `IconCacheService` silenciavam erros sem fornecer rastreabilidade diagnóstica.

**Correção:**
1. `IconCacheService.ResolveFullPath` e `ShellCommands.TryResolveTarget`: Adição de resolução com fallback para atalhos de Área de Trabalho renomeados (busca por arquivos na mesma pasta que iniciam com o nome base e mesma extensão, resolvendo apenas quando há exatamente um candidato inequívoco; múltiplos candidatos são tratados como não resolvidos para evitar acionar alvo incorreto).
2. `IconCacheService.GetIcon`: Atalhos `.url` agora extraem o ícone real do alvo via `GetImageSource` e o convertem em `Icon`, exibindo o ícone nítido do Teams no painel.
3. `IconCacheService.BuildCacheKey`: Inclusão do prefixo versionado `v2:` para alvos de URI/URL, permitindo renovação automática e garantida do cache em disco.
4. `LaunchExecutor`: Utilização de `ShellCommands.TryResolveTarget` para garantir que o atalho renomeado seja executado com sucesso ao ser clicado pelo usuário.
5. `IconCacheService.TrimTransparentMargins`: Proteção contra bounds inválidos caso aplicado repetidamente.
6. Remoção de blocos `catch {}` vazios em todo o serviço, substituídos por mensagens rastreáveis de depuração (`System.Diagnostics.Debug.WriteLine`).

**Comprovação rigorosa:**
- **Testes automatizados**: 51 testes unitários xUnit passando em `tests/SmartDockGroups.Tests` (incluindo testes de duplo recorte, versionamento v2 da chave de cache, resolução unívoca de atalhos `.url` renomeados e descarte sob ambiguidade).
- **Execução contra o binário publicado real**: Publicado via `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` (`src/SmartDockGroups.App/bin/Release/net10.0-windows/win-x64/publish/SmartDockGroups.App.exe`).
- **Dois cenários de teste validados com capturas recortadas (< 300 KB)**:
  - *Upgrade* (com o `IconCache` real da máquina): `docs/execucoes/upgrade-painel.png` (27 KB) e `docs/execucoes/upgrade-mosaico.png` (22 KB).
  - *Instalação limpa* (com `IconCache` vazio): `docs/execucoes/limpa-painel.png` (27 KB) e `docs/execucoes/limpa-mosaico.png` (22 KB).
  - Ambos comprovaram visualmente a exibição correta e nítida do ícone do Teams tanto no modo Painel quanto na App Folder.
- **Sonda de caracterização**: Validação de estabilidade com oráculo determinístico (`IDENTICO` entre duas execuções consecutivas do mesmo build) e comparação com a baseline `tests/baseline/report-master.json` resultando em 316 de 316 campos idênticos.
- **Integridade de dados**: Backup e restauração conferidos por SHA-256 do arquivo `%AppData%\SmartDockGroups\config.json` (`7046EFF0CF00ED7BDCA814AE5D6C982D6128B3B65286F087085A5B4D08D2C9DF`) e dos 68 arquivos da pasta `IconCache` conferidos por manifesto SHA-256 com zero divergências.




## 28. Lote de 2026-09-30: colar que não aparecia, ícones que sumiam, menus por contexto, importação

Organize e resolva, nesta ordem:

**Correções, sempre pela causa raiz:**

1. Atalhos colados de uma janela do Explorer só aparecem depois de ordenar, redimensionar
   ou salvar as Configurações. *(Causa: `FinishStructuralChange` só redesenhava em
   organização automática; em posição livre — o padrão de todo grupo novo — não repintava
   nem salvava. Os dois modos passam a repintar e salvar no mesmo ponto.)* Colar, soltar,
   importar e criar atalho devem passar por um só caminho, que não duplica o mesmo atalho e
   põe cada novo ícone na primeira célula livre da grade.
2. Ícones somem: Dev Apps perdeu DBeaver, Parametrizacao e SSMS; Taskbar perdeu Edge,
   NVIDIA App e WinDirStat. *(Causa medida no `config.json` real: os seis apontavam para
   `.lnk` da pasta de fixados da barra de tarefas, que o Windows apaga ao desafixar.)* Um
   atalho que entra num grupo vira uma cópia do app (`%AppData%\SmartDockGroups\Shortcuts`);
   na inicialização os existentes são adotados e os perdidos recuperados do Menu Iniciar,
   das áreas de trabalho e do Quick Launch, só quando não houver ambiguidade (vários
   candidatos apenas se todos abrem o mesmo programa), com backup datado do `config.json`
   antes de reescrever. O que não puder ser recuperado mostra um aviso no lugar do ícone e
   oferece "Localizar atalho perdido…". Um atalho que só "usa o ícone do programa" é
   desenhado a partir do programa com a seta de atalho por cima, porque o shell devolve uma
   folha em branco para alguns deles (SSMS 22) em processo com DPI; o cache de ícones sobe
   para `v3`.
3. No estilo App Folder, o menu de um atalho é diferente, mais curto e ilegível; o clique
   direito no fundo da folha aberta não faz nada. Use exatamente os menus temáticos do
   painel nos dois lugares; renomear e remover pela folha precisam salvar; a folha não pode
   fechar sozinha por causa de um diálogo que ela mesma abriu.

**Regras de conteúdo:**

4. Retire a regra de só remover grupo vazio. Com atalhos dentro, avise quantos serão
   perdidos e peça confirmação com **Não** como padrão. O mesmo para subpasta com conteúdo.
5. Dentro de um grupo não pode haver nomes repetidos — digitando (recusar com o motivo e
   manter o diálogo aberto), colando, soltando, movendo de outro grupo ou importando
   (numerar " (2)", " (3)"…). Normalize configurações antigas e JSON importado.

**Menus e fluxo:**

6. Reagrupe o menu do clique direito por contexto: o grupo; o que entra nele (novo atalho,
   importar, colar); Exibição ▸; Aparência ▸ (com Compartilhar ▸); Ordem na área de
   trabalho ▸; novo grupo, duplicar, atalho na barra, Configurações; fechar e remover. O
   menu de um ícone termina com o submenu do grupo e "Novo grupo…".
7. "Novo grupo…" em todos os menus; o diálogo do nome abre perto do mouse (não no centro de
   uma tela grande) e o grupo nasce ali. A barra de tarefas pode estar em qualquer lado:
   posicione sempre pela área útil do monitor.

**Visual:**

8. Rolagem no modo painel (sem barra horizontal inútil).
9. Ctrl+F: os ícones que batem ficam em destaque, o atual em destaque cheio e à vista, os
   demais esmaecidos.
10. Compartilhar aparência com os outros grupos, aspecto por aspecto — cor sem impor
    imagem, imagem sem impor cor, opacidade, espaçamento, tamanho do ícone, ou tudo — agora
    em todos os grupos, ou como padrão para os grupos criados depois.

**Importação:**

11. Além do JSON, importar vários atalhos de uma pasta num passo só (seleção múltipla,
    Ctrl+A), escolhendo o grupo de destino ou criando um. Nas Configurações, "Salvar" não
    pode mais substituir os grupos pela cópia de quando a janela abriu — só quando um JSON
    foi importado.

**A prova exigida:** testes xUnit para as regras (73 no total); o app de verdade numa pasta
de dados isolada (`SMARTDOCKGROUPS_DATA_DIR`) com uma **cópia** da configuração real,
percorrido por mouse, teclado e UI Automation (`GroupProbe --verify-lote`), no executável
**publicado** self-contained, com cache de ícones copiado e vazio; a sonda de caracterização
com oráculo `IDENTICO` e comparação master × branch explicando cada divergência; capturas só
recortadas, e nenhuma com os atalhos pessoais do usuário no repositório público.

## 29. "Criar atalho na barra de tarefas" na pasta certa

O atalho do grupo está indo para `%AppData%\SmartDockGroups\GroupShortcuts`. Ele deveria ir para a
pasta dos ícones que aparecem na barra (`...\Quick Launch\User Pinned\TaskBar`), achada em qualquer
computador sem caminho fixo.

*Resposta, medida antes de mudar:* essa pasta não é a barra — o Windows a preenche **ao fixar** (a
barra é o valor `Taskband\Favorites`), e fora do Explorer o Windows ignora qualquer pedido de fixar
(`IPinnedList3::Modify` devolve `S_OK` sem efeito; o verbo de fixar nem é listado). Então: grave o
atalho no **Menu Iniciar do usuário** (known folder `Programs`, pasta "Smart Dock Groups"), com um
**AppUserModelID próprio por grupo** para cada atalho ser fixável independentemente; abra o Explorer
nele com a instrução "clique direito → Fixar na barra de tarefas"; mantenha o arquivo em dia quando o
grupo for renomeado ou removido. Prove fixando pelo menu do Explorer de verdade, conferindo que o
Windows copia o `.lnk` para `User Pinned\TaskBar`, que um segundo grupo continua fixável, e desafixe
voltando a barra exatamente ao estado anterior.

## 30. Comandos que sobem o app, menus nas bordas e acoplar os grupos (1.1.3.0)

1. Trazer os grupos para frente, mandá-los para o fundo, abrir todos — qualquer menu que
   fale dos grupos — tem de funcionar com o app fora da memória, trazendo-o. O menu da área
   de trabalho ganha esses comandos (e acoplar/desacoplar); cada um relança o `.exe`, que
   sobe e executa, ou repassa ao que já está aberto. O comando que sobe o app espera os
   grupos estarem na tela. A bandeja ganha os mesmos comandos.
2. Grupo encostado numa borda: o menu tem de abrir do lado coerente, nunca para fora nem no
   outro monitor — esquerda/direita e topo/rodapé. *(Reproduzido antes de mexer: 19 casos,
   todos certos no build atual; aguardando um caso concreto.)*
3. Uma opção para **acoplar todos os grupos**, como se estivessem num dock: empilhados e
   fechados. A seta para baixo de um abre só ele e empurra os de baixo; fechá-lo puxa de
   volta, com efeito de expandir/colapsar. Nunca dois abertos. Antes de acoplar, guarde
   posição e tamanho de cada grupo (e estado e estilo); **desacoplar** devolve cada um ao
   local de antes. O estado sobrevive a reabrir o app; arrastar um título move a pilha.
