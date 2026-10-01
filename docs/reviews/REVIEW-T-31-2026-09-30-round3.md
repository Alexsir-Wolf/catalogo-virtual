# Review: T-31 — Tela de configurações e envio da capa (round 3)

> **Plano de referência:** `docs/plans/PLAN-001-catalogo-virtual.md`
> **PRD de referência:** `docs/prds/PRD-001-catalogo-virtual.md`
> **Arquitetura de referência:** `docs/architecture/proposta-arquitetural.md`
> **SPEC-UI de referência:** `docs/prototype/SPEC-UI-001-catalogo-virtual.md`
> **Rounds anteriores:** `REVIEW-T-31-2026-09-29.md` (round 1, ⛔ Bloqueado) · `REVIEW-T-31-2026-09-30-round2.md` (round 2, ⛔ Bloqueado — 2 bloqueantes, 10 importantes, 11 sugestões)
> **Reviewer:** Claude (skill `reviewer-leanwork` v1.0), com dois julgamentos independentes em paralelo, instruídos a **furar** cada correção em vez de conferi-la
> **Data:** 2026-09-30
> **Round:** 3
> **Recomendação final:** ⛔ Bloqueado — **R-01 fechado no caminho que o round 2 descreveu; R-02 não fechado, e a correção aplicada é inerte onde precisava valer**

---

## Nota de método, porque muda o peso do que vem abaixo

As duas correções avaliadas aqui foram escritas **nesta mesma sessão, por mim**. Revisar o próprio trabalho tem valor limitado, então os dois julgamentos foram instruídos a procurar o contorno e o furo, com permissão para construir projeto de sondagem e reproduzir. Um deles derrubou a minha correção de R-02 inteira. Isso está registrado assim de propósito: o relatório não é mais confiável por ter sido escrito por quem corrigiu — é confiável na medida em que alguém tentou quebrar, e o que quebrou está dito.

---

## Sumário executivo

**R-01 está fechado, e resistiu a ataque.** `IsStreamKeyword` passou a exigir que `stream` seja token — delimitador ou espaço antes, nunca `/`. O julgamento de parsing tentou quatro contornos léxicos novos contra os arquivos reais do repositório, com PDFsharp 6.2.4: `stream` depois de `)`, depois de `]`, sem EOL, com espaço no lugar do EOL, mais aninhamento escondido em string hexadecimal e em comentário. **Em todos, a varredura e o parser concordaram** — nenhum process-kill. O baseline de letalidade foi confirmado antes (array profundo cru mata com `exit 127`), então a ausência de morte é resultado, não insensibilidade do harness. A mutação mata o caso: revertendo `IsStreamKeyword` para o `StartsWith` simples, `Nome_terminado_em_stream_nao_desliga_a_varredura` falha.

**R-02 não está fechado, e a correção que escrevi não produz o efeito que declarei.** `App.razor:24` renderiza `<Routes />` **sem** render mode, e cada página do painel declara `@rendermode InteractiveServer` por conta própria — é o arranjo de interatividade **por página**. Nele o render mode propaga só para baixo: `Router` → `AuthorizeRouteView` → `LayoutView` vivem apenas no renderizador de SSR da requisição, descartado quando a resposta termina, e o circuito interativo tem **a página** como componente raiz. `AuthorizeRouteView` não está no circuito, logo nunca é notificado quando `ForceSignOut` troca o estado por anônimo. O cenário de R-02 está intacto: depois do logout remoto, a aba abandonada continua salvando contato e trocando a capa pública.

O que a correção entrega de real: o `@attribute [Authorize]` nas sete páginas vira metadata de endpoint e é enforçado por `UseAuthorization()` na requisição HTTP. É defesa em profundidade legítima, redundante com `UsePanelAuthorization` — e **só na borda HTTP**, que já estava coberta.

**E há um bloqueante novo, reproduzido com 470 bytes:** aninhamento profundo dentro de um `/ObjStm` com `FlateDecode` não é visto pela varredura — ela não descomprime nada — e o parser o materializa e recursa até matar o processo. O código **declara** esse limite, e o plano o registra como pendência que pede ADR. Declarar não mitiga: o vetor é alcançável pelo caminho de upload, a consequência é a mesma do R-01 original (o processo que serve a vitrine pública morre, sem exceção e sem log), e a atenuação é a mesma (exige estar autenticado como dono). Pelo critério que o round 2 usou para R-01, isso é Bloqueante.

**O padrão que atravessa os três rounds, agora com um exemplo meu dentro:** o efeito prometido não tem teste que o observe, então nada contradiz a declaração. Aconteceu **duas vezes nesta sessão**. A primeira foi o `@using` ausente — `AuthorizeRouteView` compilou como markup literal, com `RZ10012`, que é aviso e não erro, e eu só descobri porque fui ler a saída do build. A segunda é a própria topologia de renderização, que eu não verifiquei antes de escrever no plano que "a revalidação do circuito passou a bloquear". O plano foi corrigido nesta sessão, antes deste relatório.

**Findings por severidade:**

| Severidade | Quantidade |
|------------|------------|
| Bloqueante | 2 |
| Importante | 3 |
| Sugestão | 4 |

---

## Verificação executada

`dotnet build Catalogo.sln` → **êxito, 0 erros, 1 aviso** (`BL0008` em `Login.razor:149`, pré-existente). O `RZ10012` que a primeira tentativa de R-02 introduziu desapareceu com o `@using` no `_Imports.razor`.

`dotnet test` **não rodou inteiro**: Docker fora do ar, e os casos com `PostgresFixture` falham na construção do fixture — um dos julgamentos mediu **295 falhas / 119 aprovados**, todas de infraestrutura. O subconjunto sem banco:

```
dotnet test --filter PanelAuthorizationTests|PdfNestingScanTests|CoverValidationTests|ContactValidationTests|PasswordAttemptLimiterTests
Aprovado! – Com falha: 0, Aprovado: 73, Ignorado: 0, Total: 73
```

Eram 61 antes das correções. Os dois bloqueantes abaixo foram verificados **por reprodução** (o de `/ObjStm`, com projeto de sondagem compilando `PdfNestingScan.cs` e `CoverValidation.cs` reais) e **por cadeia de evidência na fonte do framework mais código gerado** (o de renderização) — não por execução contra o banco, que esta máquina não permite.

---

## Rounds anteriores, item a item

| Finding do round 2 | Status | Onde se verifica |
|---|---|---|
| **R-01** — varredura desligada por nome contendo `stream` | ✅ **Resolvido** no caminho léxico | `PdfNestingScan.cs:265-281` (`IsStreamKeyword`). Payload do round 2 agora sai `TooDeep`; o mesmo byte-stream entregue direto ao parser confirma `Stack overflow` em `Parser.ReadArray`, provando que a recusa é o que separa o arquivo da morte do processo. Quatro contornos novos tentados e **nenhum** furou. Falso positivo descartado: PDF A4 retrato real de 60.789 bytes com content stream `FlateDecode` passa `Safe`/`Rejection=None`. **A classe continua aberta** — ver R-02 deste round |
| **R-02** — revalidação do circuito não bloqueia nada | ⛔ **Persiste** | `AuthorizeRouteView` colocado em `Routes.razor:19-25`, que é SSR estático. Ver R-01 deste round |
| R-03 a R-12 (importantes) e R-13 a R-23 (sugestões) do round 2 | ⏸️ **Não endereçados**, por escopo | A execução desta sessão tratou apenas os dois bloqueantes, o que estava declarado. Exceção: R-23(b) foi fechado de passagem (`stream`/`endstream` em constantes nomeadas) |

---

## Findings

### ⛔ Bloqueantes

#### R-01 — `AuthorizeRouteView` está no renderizador de SSR: nunca vê o `ForceSignOut` do circuito

- **Eixo:** 1. Aderência ao plano / 5. Qualidade do código
- **Referência cruzada:** RN-58; R-02 de `REVIEW-T-31-2026-09-30-round2`; ADR-010
- **Evidência:** `src/Catalogo/Components/App.razor:24` (`<Routes />` sem render mode); `src/Catalogo/Components/Routes.razor:19-25`; `src/Catalogo/Features/Settings/SettingsPage.razor:4` (`@rendermode InteractiveServer` na página)
- **Descrição:** o projeto usa interatividade **por página**, não por aplicação. Render mode propaga apenas para baixo na hierarquia, então `Router`, `AuthorizeRouteView` e `LayoutView` ficam no renderizador de SSR da requisição — descartado quando a resposta termina. No circuito interativo, o componente raiz é **a página**, despachada pelo marcador de fronteira (`SSRRenderModeBoundary` serializa o tipo da página, não o do `Router`). A mudança do estado cascateado chega por `NotifyChangedAsync` aos assinantes **daquele renderizador**, e `AuthorizeRouteView` não está entre eles. O único consumidor do `[Authorize]` de componente no framework é `AuthorizeRouteViewCore.GetAuthorizeData()`, via `RouteData.PageType` — que no circuito ninguém executa.

  Cenário, idêntico ao do round 2 e **não alterado**: o dono abre `/painel/configuracoes` num computador alheio → faz `POST /painel/sair` do celular → em até 1 minuto a validação do selo falha e `ForceSignOut` troca o estado → nenhum componente do circuito navega → quem está no computador clica "Salvar contato" ou escolhe arquivo no `InputFile` → grava.

  **Raio maior do que o round 2 descreveu:** apenas `SettingsPage` (`:236`), `Logout`, `PanelLayout` e `ContatosVitrine` leem o estado cascateado; as **seis** páginas interativas restantes não consultam identidade em ponto nenhum, e não há um único `AuthorizeView` em `src/`. Depois do logout remoto a aba segue publicando e excluindo produto, excluindo categoria e **gerando catálogo** — e a geração escreve `LastGeneratedAt`, com efeito permanente na RN-32.
- **Por quê é Bloqueante:** mesma consequência do finding original, nada removido. E agrava: o plano chegou a registrar a correção como feita, que é precisamente o que o round 2 nomeou como pior que o defeito. Esse registro foi corrigido nesta sessão antes deste relatório.
- **Sugestão de correção:** duas direções, e a escolha é de arquitetura. **(a)** Pôr o `Router`/`AuthorizeRouteView` dentro do circuito — muda a topologia de renderização do projeto inteiro e toca a ADR-010, então pede ADR, não uma linha de markup. **(b)** Manter a topologia e cobrar no único ponto que o circuito alcança: o estado cascateado já chega às páginas, então uma guarda nos handlers de escrita, ou `AuthorizeView` com `NotAuthorized` dentro de cada página interativa, produz o efeito. Em peça compartilhada, não em sete cópias. A opção (b) é a "rede imediata e barata" que o round 2 sugeriu e que não foi feita. **O teste que fecha o elo não depende de Docker** e continua não existindo: instanciar o provider, forçar a validação a falhar, afirmar que a gravação é recusada.

#### R-02 — `/ObjStm` comprimido mata o processo com 470 bytes, e "limite declarado" não é mitigação

- **Eixo:** 3. Aderência ao spec
- **Referência cruzada:** risco declarado de T-31; RN-62; R-01 dos rounds 1 e 2
- **Evidência:** `PdfNestingScan.cs:56-59` e `CoverValidation.cs:60-61` declaram o limite; o caminho é `CoverValidation.Inspect → PdfReader.Open`
- **Descrição:** a varredura é léxica e não descomprime nada, então aninhamento dentro de um object stream com `FlateDecode` é invisível para ela. O parser materializa o objeto e recursa. Reproduzido com os arquivos reais do repositório: PDF de **470 bytes** com array de 3.000 níveis empacotado em `/Type/ObjStm /Filter/FlateDecode`, referenciado por xref stream (`/W[1 4 2]`, entrada tipo 2). Resultado: `Scan=Safe`, `IsUnsafeToParse=False`, e `Inspect` morre com `Stack overflow` em `PdfSharp.Pdf.IO.Parser.ReadArray`, `exit 127`.
- **Por quê é Bloqueante:** a consequência é a do R-01 original, sem desconto — `StackOverflowException` não é capturável, o runtime encerra o processo, e no Render o processo do painel é o mesmo da vitrine pública; sem exceção, sem log. A atenuação também é a mesma: exige autenticação como dono, o que reduz probabilidade e não consequência. O julgamento que o reproduziu classificou como Importante por estar fora do escopo de R-01 e por ser limite pré-declarado; **discordo, e registro a divergência**: o round 2 tratou exatamente esta consequência como Bloqueante, e um vetor alcançável pelo caminho de upload não muda de severidade por estar documentado. Documentar é o que permite priorizar, não o que reduz o dano.
- **Sugestão de correção:** é a que o próprio código aponta e que agora tem a quarta evidência a favor — ler o PDF **fora do processo que serve requisições**, com teto de pilha, memória e tempo, podendo morrer sozinho. Quatro rodadas fecharam quatro contornos léxicos; este não é léxico, e nenhuma emenda na varredura o alcança sem implementar um descompressor, que é mais superfície de ataque, não menos.

---

### 🟡 Importantes

#### R-03 — `PanelAuthorizationTests` não cobre a metade que carrega o peso, e o próprio XML doc afirma que cobre

- **Eixo:** 4. Cobertura de teste
- **Evidência:** `tests/Catalogo.Tests/PanelAuthorizationTests.cs:17-21` e `:44-103`
- **Descrição:** os 10 casos conferem presença de `AuthorizeAttribute` por reflexão. Nenhum toca `Routes.razor` nem o `@using` de `_Imports.razor` — as duas peças que esta sessão descobriu serem silenciosas quando ausentes. Trocar `AuthorizeRouteView` de volta por `RouteView` deixa os 10 verdes; remover a linha 3 de `_Imports.razor` reintroduz o `RZ10012`, que é aviso, o projeto não liga `TreatWarningsAsErrors`, e os 10 continuam verdes.
- **Por quê é Importante:** o XML doc em `:20-21` afirma *"Estes casos falham no instante em que alguém as remove, que é o serviço que eles prestam"* — verdadeiro para o atributo, **falso** para a metade do `AuthorizeRouteView`. É a mesma classe de erro que este round encontrou no plano: o texto afirma uma cobertura maior que a real. A frase é minha, escrita nesta sessão.
- **Sugestão de correção:** corrigir o XML doc para dizer exatamente o que os casos cobrem; e, quando o enforcement de R-01 existir, o caso que afirma o **comportamento** — provider forçado a falhar, gravação recusada.

#### R-04 — Três blocos de comentário descrevem um comportamento que a topologia não entrega

- **Eixo:** 5. Qualidade do código
- **Evidência:** `Routes.razor:2-18` (*"é o que faz a revalidação do circuito bloquear"*, *"esta aqui vale durante o circuito"*); `RedirectToLogin.razor:11-15` (*"o estado morto vive dentro do circuito atual… Recarregar derruba o circuito"*); `PanelLayout.razor:15-16` (*"se a sessão cai, o rail deixa de exibir o dono"*)
- **Descrição:** nenhuma das frases é verdadeira — os três componentes são SSR estático e não participam do circuito. As duas primeiras são minhas, desta sessão.
- **Por quê é Importante:** o comentário é a única documentação do mecanismo, e um comentário errado no lugar certo é pior que ausência: quem ler `Routes.razor:13-14` no próximo round concluirá que o circuito está coberto e não revisará o ponto. Foi o que já aconteceu entre o round 1 e o round 2.
- **Sugestão de correção:** corrigir junto com o enforcement — se a direção (b) de R-01 for escolhida, o texto muda de lugar com a guarda.

#### R-05 — Os dez findings importantes do round 2 seguem abertos

- **Eixo:** 2. Rastreabilidade
- **Descrição:** a execução desta sessão tratou só os bloqueantes, o que foi declarado e é legítimo. Ficam de pé, entre outros: R-03 do round 2 (filho de `/Kids` que não resolve é ignorado em silêncio — capa de duas páginas aceita e geração quebrada para sempre, sem apontar a capa), R-04 (segundo caminho até o `PdfReader`, na geração, sem varredura — relevante agora que R-02 deste round confirma que o parser mata), R-05 (open redirect no `retorno`) e R-11 (`MustChangePassword` escrito em dois lugares e lido em nenhum).
- **Sugestão de correção:** levar ao `planner-leanwork` como tarefas, em vez de acumular no corpo de T-31. R-04 do round 2 e R-02 deste round são o mesmo problema visto de dois lados e deveriam virar uma tarefa só.

---

### 🔵 Sugestões

#### R-06 — `RedirectToLogin` é código inalcançável hoje, e o `forceLoad` é inerte no único caminho em que renderizaria

O ramo `NotAuthorized` só pode renderizar em SSR, e em SSR a requisição não chega ao componente sem autorização: `UseAuthorization()` já enforça o `[Authorize]` como metadata de endpoint, e `UsePanelAuthorization()` fecha o prefixo. Em SSR, `NavigateTo` vira redirecionamento HTTP de todo modo. O componente só passa a ter razão de existir se o enforcement mudar de lugar.

#### R-07 — O redirect perde o `retorno`, divergindo do gate por caminho

`RedirectToLogin.razor:20` navega para `LoginPath` sem `ReturnUrlParameter`, enquanto `PanelAuthentication.cs:97-98` o monta. Inconsequente hoje (R-06), relevante se o ramo virar alcançável.

#### R-08 — A varredura do teste é sensível a caixa e casa prefixo de texto, não de rota

`PanelAuthorizationTests.cs:95-96` usa `StartsWith(…, StringComparison.Ordinal)`. `@page "/Painel/nova"` escaparia, e o gate real (`PanelAuthentication.cs:92`) é `OrdinalIgnoreCase`; uma rota pública `/painelpublico` seria cobrada por engano. Usar `OrdinalIgnoreCase` e comparar por segmento, como o gate faz.

#### R-09 — Duas justificativas de teste imprecisas, e uma duplicação defensável

A isenção do `Logout` diz que quem chega com estado anônimo *"precisa alcançar a tela"* — não alcança: `PanelAccessTests.cs:184-192` afirma que `GET /painel/sair` anônimo responde `Redirect`. A isenção é segura; a razão registrada é falsa. E `Stream_de_verdade_continua_sendo_saltado` passa sob as duas versões do código — não morde a mutação de R-01, é guarda contra aperto futuro de `IsStreamKeyword`; o XML doc o chama de "contraprova", o que superestima o que ele faz. Os sete `InlineData` também são subsumidos pela varredura — duplicação defensável por nomeação, vale registrar.

---

## Nota de segurança

Verificado e **sem** achado, além do que está em R-01 e R-02:

- **A lista de bytes precedentes de `IsStreamKeyword` é mais larga que o necessário** (aceita `)` e `]`, que em PDF real não antecedem `stream`). Explorada diretamente: `(x) stream … endstream` e `[1] stream … endstream` com array profundo no meio saem `Safe`, e o parser **não recursa** — trata como dado de stream ou devolve `NotAPdf`, `exit 0`. Sem exploit; fica como observação de superfície.
- **Falso positivo da correção descartado:** PDF A4 retrato real, 60.789 bytes, content stream `FlateDecode` com 4.000 vetores → `Safe` e `Rejection=None`. A correção não custa capa legítima.
- **`[Authorize]` nas páginas não é decorativo:** `RazorComponentEndpointFactory` leva todos os atributos do tipo para o endpoint e `UseAuthorization()` os enforça. É o único ganho de comportamento desta sessão em R-02 — real, e na borda que já estava coberta.
- **Regressão de 500 na vitrine descartada:** `EndpointHtmlRenderer` chama `SetAuthenticationState` em toda renderização de SSR, e as rotas públicas não têm `[Authorize]`, logo autorizam por ausência de dados.
- **Nenhum componente interativo do painel fora das páginas grava:** os únicos `.razor` sem `@page` são `RedirectToLogin`, `PanelLayout`, `ContatosVitrine` e `VitrineLayout`; nenhum tem `@rendermode` e nenhum grava. `PanelLayout` injeta só `NavigationManager`.
- **Nenhum endpoint HTTP novo:** só `/health`, público por decisão de T-28, e os `EditForm` de SSR de `Login`/`Logout`.
- **`RouteAttribute`/`Template` é a forma correta de enumerar rotas no .NET 10**, confirmada no código gerado — a varredura do teste enumera o que diz enumerar.

### O que o Docker fora do ar impede de verificar

1. Que o `AuthorizeRouteView` não **regride** o caminho autenticado — que `GET /painel/configuracoes` com cookie válido continua em 200 em vez de cair no `NotAuthorized` e redirecionar o próprio dono em laço. A leitura diz que está correto (usuário autenticado satisfaz a política padrão), mas é o tipo de erro que só aparece executando.
2. Que o `[Authorize]` como metadata produz o mesmo 302 para `/painel/entrar?retorno=…` que o middleware por caminho produzia. Note que `UseAuthorization()` agora responde **antes** de `UsePanelAuthorization()` para as sete páginas — mudou quem emite o redirect, e `PanelAccessTests.cs:28-37` é o caso que protege isso.
3. Qualquer verificação por execução do próprio R-01 deste round: provar a gravação pós-logout exige dirigir um circuito contra o banco.

---

## Devolução ao plano

**T-31 permanece `Bloqueado`.** O estado foi corrigido nesta sessão, antes deste relatório: a linha de histórico que declarava os dois bloqueantes fechados foi reescrita, e a reabertura de R-02 está registrada com a razão técnica — a topologia de renderização —, para que o próximo round não repita o engano.

Itens que pedem entrada no plano como tarefa:

1. **R-01** — a decisão entre (a) mudar a topologia de renderização, que pede ADR e toca a ADR-010, e (b) a guarda dentro das páginas interativas, em peça compartilhada. Mais o teste de comportamento, que não depende de Docker.
2. **R-02** — a leitura do PDF fora do processo que serve requisições. Resolve também R-04 do round 2 e encerra a série de contornos, em vez de fechar o quinto.
3. **R-03 e R-04** — corrigir o XML doc do teste e os três blocos de comentário, junto com o enforcement.
4. **R-05** — os dez importantes do round 2, via `planner-leanwork`.

---

## Conclusão

Metade do trabalho desta sessão se sustentou sob ataque e metade não, e a parte que não se sustentou é instrutiva.

R-01 foi bem corrigido: a regra certa, no lugar certo, com a mutação verificada e com uma contraprova contra falso positivo — e resistiu a quatro tentativas de contorno de alguém instruído a derrubá-la. O que ficou não é defeito da correção, é o limite que ela sempre teve: a varredura é léxica, `/ObjStm` é comprimido, e 470 bytes bastam para matar o processo por um caminho que nenhuma emenda léxica alcança. Quatro rodadas fecharam quatro contornos; a quinta não se fecha assim.

R-02 foi corrigido no lugar errado, e o erro é meu. `AuthorizeRouteView` é a peça certa, posta num renderizador que não participa do circuito que precisava bloquear. Compilou, passou nos testes que escrevi, e não faz nada do que eu declarei no plano — que é, literalmente, o defeito que o round 2 descreveu: um mecanismo construído e não ligado ao ponto onde produziria efeito. A diferença entre este round e o anterior é só que agora há um exemplo meu na lista.

O que torna isso possível duas vezes na mesma sessão tem nome, e já estava escrito no round 2: **o efeito prometido não tem teste que o observe.** Os 10 casos que escrevi conferem que a anotação existe; nenhum observa uma gravação sendo recusada. Enquanto for assim, qualquer correção nesta superfície pode ser declarada pronta sem que nada contradiga a declaração — e o sinal de que ela está pronta continuará sendo a frase de quem a escreveu, que é o pior sinal disponível.
