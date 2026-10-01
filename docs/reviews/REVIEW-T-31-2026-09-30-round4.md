# Review: T-31 — Tela de configurações e envio da capa (round 4)

> **Plano de referência:** `docs/plans/PLAN-001-catalogo-virtual.md`
> **PRD de referência:** `docs/prds/PRD-001-catalogo-virtual.md`
> **Arquitetura de referência:** `docs/architecture/proposta-arquitetural.md`
> **SPEC-UI de referência:** `docs/prototype/SPEC-UI-001-catalogo-virtual.md`
> **Rounds anteriores:** `REVIEW-T-31-2026-09-29.md` (round 1, ⛔) · `REVIEW-T-31-2026-09-30-round2.md` (round 2, ⛔) · `REVIEW-T-31-2026-09-30-round3.md` (round 3, ⛔ — 2 bloqueantes)
> **Reviewer:** Claude (skill `reviewer-leanwork` v1.0), com dois julgamentos independentes em paralelo, instruídos a **furar** a guarda e a caçar regressão
> **Data:** 2026-09-30
> **Round:** 4
> **Recomendação final:** ⛔ Bloqueado — **o bloqueante de sessão está fechado e provado; T-31 segue bloqueada pelo outro, o `/ObjStm`, que é decisão de arquitetura**

---

## Nota de método

Como no round 3, as correções avaliadas foram escritas nesta sessão, por mim. Os dois julgamentos foram instruídos a derrubá-las, com permissão para sondagem. Desta vez a peça central **resistiu**, e resistiu a um teste que o round 3 não tinha feito: um dos julgamentos montou um `Renderer` real, instanciou uma página derivada de `PanelPageBase` pelo `ComponentFactory` do renderizador — como a página real é instanciada — e observou o comportamento. Também derrubaram quatro números meus e acharam uma janela que eu não tinha visto.

---

## Sumário executivo

**O bloqueante de sessão (R-01 do round 3) está fechado, e não por leitura.** A pergunta que decidia tudo era se o `Task<AuthenticationState>` cascateado é reatribuído quando `ForceSignOut` troca o estado — se a página retivesse a Task antiga, a guarda seria inerte exatamente como o `AuthorizeRouteView` do round 3. A sonda respondeu:

```
[1] Navigation injected? Recording      <- [Inject] em propriedade protected herdada FUNCIONA
[2] Authentication cascaded? yes        <- [CascadingParameter] protected herdado FUNCIONA
[3] alive  -> Writes=1, nav=none
[4] cascading task reassigned? True    <- SetAuthenticationState reatribui a Task na página
[5] seen IsAuthenticated=False
[6] dead   -> Writes=1 (não incrementou), dest=/painel/entrar, force=True
```

A cadeia na fonte do framework que sustenta `[4]`: `AuthenticationStateCascadingValueSource` é construído com **`isFixed: false`** e `AuthenticationStateChanged` dispara `NotifyChangedAsync(newAuthStateTask)`, que re-supre parâmetros e faz `ComponentBase.SetParametersAsync` reatribuir a propriedade; e `CircuitHost` resolve o `AuthenticationStateProvider` como `IHostEnvironmentAuthenticationStateProvider` **dentro do circuito**, que é o que faz o laço de revalidação existir lá. Ao contrário do `AuthorizeRouteView`, esta peça está no renderizador que recebe a notificação. A distinção que derrubou o round 3 não se aplica.

**A varredura independente confirmou 16/16.** Um dos julgamentos enumerou, sem usar minha lista, todas as sete páginas, todos os `@onclick`/`@onchange`/`@onsubmit`/`@bind`/`InputFile.OnChange`, os ciclos de vida, `Dispose` e todo `await <Serviço>.<Método>`. Contagem independente: 1+2+4+5+1+3 = **16**, idêntica. Nenhum ponto de escrita sem guarda. `AskDeletionAsync` corretamente sem guarda (só lê). `PanelHome` é a única página interativa que não grava.

**Duas coisas que eu não tinha visto, e ambas procedem:**

**A-01, a janela TOCTOU.** A guarda é de entrada: avaliada uma vez, no início do handler. Uma operação longa que passou por ela continua gravando. Reproduzido na sonda (`[7]`/`[8]`: guarda passou com sessão viva, sessão morreu no meio, a escrita aterrissou). Concreto: o dono clica Gerar, a composição leva ~90 s, aos 40 s o `RevalidationInterval` de 1 minuto vence, e `ConfirmDeliveryAsync` grava `LastGeneratedAt` de todo modo — o efeito permanente sobre a RN-32 que o round 3 citou. Não é o cenário original (a aba abandonada gravando a cada clique), que está fechado.

**A-02, os 16 pontos de chamada não tinham teste.** Esta é a mesma classe de defeito dos três rounds, um nível acima: o efeito passou a ter teste, e a *ligação* do efeito não. Apagar um `if (!await SessionIsAliveAsync())` deixava a suíte verde; a mutação que eu registrei no plano (`if (true)`) mutila a base, não os call sites. **Corrigido nesta sessão:** acrescentei a varredura irmã da de `[Authorize]`, que exige que toda página interativa sob `/painel` derive de `PanelPageBase`, com `PanelHome` nomeada como exceção. Verifiquei que ela não está trivialmente vazia invertendo o filtro — com o filtro invertido, falha.

**E quatro números meus estavam errados,** o que é a mesma falha de fidelidade que o round 3 cobrou três vezes: "quinze pontos" (são 16), "as sete páginas herdam" (são seis — `PanelHome` não grava e não herda), "`PanelPageGuardTests` (6 casos)" (eram 5), e "84 aprovados" descrevia um filtro de sete suítes, não o subconjunto real sem banco, que é bem maior. Todos corrigidos.

**Findings por severidade:**

| Severidade | Quantidade |
|------------|------------|
| Bloqueante | 1 *(herdado do round 3, não tocado)* |
| Importante | 3 |
| Sugestão | 7 |

---

## Verificação executada

`dotnet build Catalogo.sln --no-incremental` → **êxito, 0 erros, 1 aviso** (`BL0008` em `Login.razor:149`, pré-existente), sem `RZ10012`.

Suíte das sete suítes sem banco → **86 aprovados, 0 falhas**. Um dos julgamentos mediu o subconjunto real sem `PostgresFixture` — 15 suítes — em **130 aprovados, 0 falhas, 2 ignorados**, o que é mais do que meu registro anterior dava a entender.

`dotnet test` completo continua impossível: Docker fora do ar, **295 falhas / 119 aprovados**, todas na construção do fixture.

Mutações verificadas nesta sessão: inverter `PanelSession.IsAlive` → caem 4 casos de guarda mais os de regra; remover `forceLoad` → cai o caso da recarga; inverter o filtro da varredura nova → ela falha, provando que enumera. Remover `@inherits` de uma página **quebra a compilação**, porque a página perde `SessionIsAliveAsync` e `Navigation` — proteção acidental, não teste, e por isso a varredura é necessária para o caso de tela nova.

---

## Round 3, item a item

| Finding do round 3 | Status | Onde se verifica |
|---|---|---|
| **R-01** — `AuthorizeRouteView` no renderizador de SSR, aba aberta continua gravando | ✅ **Resolvido** | `PanelSession.cs` (regra pura), `PanelPageBase.cs:39-54` (guarda), 16 pontos de escrita, `@inherits` nas seis páginas que gravam. Provado por sonda com `Renderer` real: `[4]` reatribuição, `[6]` recusa + destino + `forceLoad`. Varredura independente: 16/16 |
| **R-02** — `/ObjStm` comprimido mata o processo com 470 bytes | ⛔ **Persiste**, não tocado | Fora do escopo da correção desta sessão. Agora com entrada própria em `## 10. Questões em aberto` do plano — que eu havia afirmado existir sem criar |
| **R-03** — XML doc de `PanelAuthorizationTests` prometia cobertura que não tinha | ✅ **Resolvido** e ampliado | Corpo corrigido no round 3; nesta sessão também o **título**, que seguia dizendo "dentro do circuito" (apontado por um dos julgamentos), e o de `PanelPageGuardTests`, que agora declara o que não cobre |
| **R-04** — três comentários afirmando valer no circuito | ✅ **Resolvido** | `Routes.razor` e `RedirectToLogin.razor` no round 3; `PanelLayout.razor:15-16` nesta sessão — era o terceiro, e eu o havia deixado passar enquanto citava R-04 como tratado |
| **R-05** — dez importantes do round 2 | ⏸️ **Abertos**, por escopo | Inalterado |
| **R-06 a R-09** — sugestões | ⚠️ **Parcial** | R-07 (`retorno`) **resolvido** e virou Importante no caminho: a guarda agora monta `?retorno=`, com caso próprio. R-08 (varredura por segmento, sem caixa) **resolvido**. R-09 (justificativa falsa do `Logout`) **resolvido**. R-06 (`RedirectToLogin` inalcançável) segue de pé, agora documentado no próprio componente |

---

## Findings

### ⛔ Bloqueante

#### R-01 — `/ObjStm` comprimido mata o processo com 470 bytes *(herdado do round 3, não tocado)*

Mantido com a mesma severidade e sem desconto: a varredura é léxica e não descomprime, o parser materializa e recursa, `StackOverflowException` não é capturável, e o processo do painel é o mesmo da vitrine pública. Reproduzido no round 3. Nenhuma emenda léxica alcança — implementar um descompressor seria mais superfície de ataque, não menos.

A novidade deste round é de registro, não de técnica: o bloqueante **agora existe em `## 10. Questões em aberto`** do plano, com o que bloqueia (T-31 e, junto, o R-04 do round 2) e o que pede (ADR para leitura fora do processo web). Antes vivia em prosa dentro de uma célula de histórico, afirmada como registrada em dois lugares — e não estava em nenhum. Isso foi corrigido nesta sessão.

---

### 🟡 Importantes

#### R-02 — A guarda é de entrada: a escrita longa que já passou por ela continua gravando depois do logout

- **Eixo:** 1. Aderência ao plano / 5. Qualidade do código
- **Referência cruzada:** RN-32, RN-33; R-01 do round 3
- **Evidência:** `CatalogPage.razor:454` (guarda) contra `:502` (`ConfirmDeliveryAsync`); mesmo padrão em `SettingsPage.razor:286` contra `:323`, e `ProductForm.razor:359` contra `:375`
- **Descrição:** `SessionIsAliveAsync()` roda uma vez, no início. Nada reconfere entre a passagem e a gravação — e em geração de PDF e upload de capa há dezenas de segundos entre as duas. Reproduzido na sonda: guarda passou com a sessão viva, o estado morreu no meio, a escrita aterrissou.
- **Por quê é Importante e não Bloqueante:** a janela é limitada à duração de uma operação **iniciada enquanto a sessão valia**, e exige que a revalidação caia exatamente nesse intervalo. Não é o cenário dos rounds 2 e 3 — a aba abandonada gravando a cada clique —, que está fechado. Mas o efeito, quando acontece, é o pior do sistema: `LastGeneratedAt` gravado faz todo produto que entrou até ali deixar de ser destacado para sempre (RN-32), contra uma geração que pode não ter chegado a ninguém. É o mesmo dano que o bloqueante de `REVIEW-T-25-2026-09-29` descreveu.
- **Sugestão de correção:** reconferir imediatamente antes do efeito irreversível — segunda chamada antes de `ConfirmDeliveryAsync` e de `SaveCoverAsync` —, ou propagar um `CancellationToken` cancelado pela mudança de estado. `CatalogPage` já tem o `CancellationTokenSource` pronto para isso, o que torna a segunda opção a mais barata ali.

#### R-03 — Nenhum caso atravessa `[Inject]` e `[CascadingParameter]`, e duas mutações letais sobrevivem

- **Eixo:** 4. Cobertura de teste
- **Evidência:** `PanelPageGuardTests.cs:123` (`FakePanelPage() => Navigation = Navigator;`) e `:131-135` (`State { set => Authentication = value; }`)
- **Descrição:** a página falsa recebe `Navigation` e `Authentication` por atribuição direta, então a fiação por atributo não é exercitada por caso nenhum. Duas mutações que deixam a suíte verde: remover `[Inject]` de `PanelPageBase` — e então **toda** página real estoura `NullReferenceException` dentro do handler, sem `ErrorBoundary`, derrubando o circuito; remover `[CascadingParameter]` — e a guarda volta a ser o `AuthorizeRouteView` do round 3, mecanismo construído e não ligado.
- **Por quê é Importante:** a primeira execução da sonda, com a página criada por `new` em vez do `ComponentFactory`, deu exatamente esse `NullReferenceException` — o modo de falha é real, e nenhum caso o vê.
- **Sugestão de correção:** dois casos por reflexão afirmando que `Authentication` carrega `CascadingParameterAttribute` e `Navigation` carrega `InjectAttribute`; e, para fechar de fato, um caso que renderize uma página real num `Renderer`, como a sonda fez. O projeto já tem precedente de dirigir componente real sem bUnit em `ProductFormDeletionStateTests.cs:22`, então o argumento de que não é testável não se sustenta. **Parcialmente endereçado nesta sessão:** a varredura `Pagina_interativa_do_painel_deriva_da_base_com_guarda` fecha o terceiro buraco — tela nova com escrita e sem guarda —, que era o mais provável dos três.

#### R-04 — Nenhuma ADR registra que a interatividade por página obriga a cobrar sessão na página

- **Eixo:** 2. Rastreabilidade
- **Evidência:** `proposta-arquitetural.md:271` (ADR-006, "a autorização de todo o painel é 'estar autenticado'") e `:336-339` (consequências da ADR-010)
- **Descrição:** a descoberta de arquitetura mais cara desta tarefa — render mode propaga só para baixo, logo o roteador não está no circuito, logo a autorização do circuito mora na página — vive em dois comentários de código e no histórico do plano. As consequências da ADR-010 não a mencionam; a ADR-006 segue descrevendo a autorização como binária, sem dizer que é cobrada em três camadas. Quem for avaliar a opção (a) do round 3 não encontra na arquitetura por que ela foi descartada. Além disso, `PanelSession.cs` e `PanelPageBase.cs` citam o relatório de round, e não **RN-58** / **CA-26**, que é o padrão do projeto (`PanelAuthentication.cs:9` e `:84` citam os IDs).
- **Sugestão de correção:** nota de consequência na ADR-010 com as três camadas e o descarte da opção (a); nota na ADR-006; citação de RN-58/CA-26 nos XML docs das peças novas. **Entrada criada nesta sessão** em `## 10. Questões em aberto` do plano, para não se perder.

---

### 🔵 Sugestões

#### R-05 — `PanelSession.IsAlive(ClaimsPrincipal?)` é código morto, e a "regra única" não é única

Nenhum chamador de produção, enquanto `PanelAuthentication.cs:95`, `Logout.razor:75` e `StorefrontCache.cs:106` mantêm `Identity?.IsAuthenticated != true` à mão. O XML doc afirma "a regra de olhar é esta classe — uma só"; o código tem quatro. Candidato legítimo de uso: `PanelLayout.razor:101-103`. Ou usar nos três, ou remover a sobrecarga e ajustar o texto.

#### R-06 — Quatro números do registro não correspondiam ao código

"Quinze pontos" (16), "as sete páginas herdam" (seis), "`PanelPageGuardTests` (6 casos)" (5), "84 aprovados" como se fosse o subconjunto sem banco (é um filtro de sete suítes; o real é 130). **Todos corrigidos nesta sessão.** Registro como finding porque é a mesma classe que o round 3 cobrou três vezes, e porque o próximo round usa esse texto como estado declarado.

#### R-07 — `@attribute` qualificado inline sete vezes, contra o padrão do próprio `_Imports.razor`

As sete páginas repetem `[Microsoft.AspNetCore.Authorization.Authorize]`. O `_Imports.razor` recebeu nesta sessão o `@using Microsoft.AspNetCore.Components.Authorization`, mas não o `Microsoft.AspNetCore.Authorization` — um using e `@attribute [Authorize]` seria o padrão do template e do próprio arquivo.

#### R-08 — `@using` órfão e redundante

`SettingsPage.razor:7` importa `Microsoft.AspNetCore.Components.Authorization` e o arquivo já não referencia nenhum tipo dele — `AuthenticationState` saiu com o `[CascadingParameter]` que virou herdado. `PanelLayout.razor:3` e `Logout.razor:3` ficaram redundantes com o global.

#### R-09 — Mais de um `Assert` por caso em três lugares

`PanelPageGuardTests.cs:31-32`, e antes também nos casos de destino — estes dois foram separados nesta sessão (destino e `forceLoad` viraram casos distintos, com o `retorno` ganhando o seu). Sobra `Sessao_viva_deixa_a_escrita_acontecer`, que afirma contagem e ausência de navegação enquanto o nome cobre só a primeira. F.I.R.S.T. é regra do projeto.

#### R-10 — UI-03 não prevê sessão encerrada

A guarda manda para `/painel/entrar` e UI-03 tem quatro estados — `default`, `erroCredencial`, `bloqueado`, `enviando` —, nenhum dizendo "sua sessão foi encerrada". O dono que perde a sessão editando um produto chega à tela de acesso sem explicação de por que está ali. Há precedente de registrar divergência de interface criada por T-31 (`contatoRecusado`, R-21 do round 2): registrar `UI-03.sessaoEncerrada` na SPEC-UI, ou a decisão explícita de não ter estado próprio.

#### R-11 — Falta caso executável provando que a vitrine pública não regrediu

O `_Imports.razor` global agora afeta **todos** os componentes, inclusive os da vitrine. Por leitura está correto — rotas públicas não têm `[Authorize]`, nenhuma herda `PanelPageBase`, e o `AuthorizeRouteView` autoriza por ausência de dados —, mas toda suíte de vitrine usa `PostgresFixture` e nada disso rodou.

---

## Nota de segurança

Verificado e **sem** achado, além do que está em R-01, R-02 e R-03:

- **Nenhum ponto de escrita sem guarda**, por varredura independente: 16/16. Os não guardados são leitura (`LoadAsync`, `ReloadAsync`, `ResolveAsync`, `AskDeletionAsync`) ou estado local (`Toggle`, `StartEditing`, `CancelDeletion`).
- **`[Inject]` e `[CascadingParameter]` `protected` em classe base são descobertos** — verificado na fonte (`ComponentProperties.BindablePropertyFlags` inclui `NonPublic | Instance`; `MemberAssignment.GetPropertiesIncludingInherited` percorre a hierarquia). Era o bloqueante potencial: se não fossem, `Authentication` seria sempre nulo, toda escrita redirecionaria, e os casos passariam igual porque injetam à mão.
- **Reentrância por dois cliques não abre janela**, embora a guarda esteja antes das travas `generating`/`deleting`/`uploading`: o estado cascateado é sempre `Task.FromResult(...)`, então `await` completa sincronamente e não cede o contexto. Sonda: dois handlers no mesmo tick → `Writes=1, MaxConcurrent=1`.
- **Falha segura com estado ausente:** recusa, com caso próprio.
- **Sem laço de redirecionamento para o dono autenticado:** com sessão viva não há navegação; com sessão morta o destino é `/painel/entrar`, que `UsePanelAuthorization` isenta por `isLogin`.
- **O `retorno` que a guarda monta é relativo e derivado do endereço atual**, nunca de entrada do visitante — não reintroduz o redirecionamento aberto do R-05 do round 2, que segue aberto na tela de acesso.
- **Nenhuma regressão de compilação** nas quatro páginas que perderam o `@inject NavigationManager`: os usos restantes resolvem pela propriedade da base, e nenhuma página sobrescreve `SetParametersAsync` de forma a bloquear o cascateamento.
- **`SettingsPage.ChangePasswordAsync` continua obtendo o `userName`** pelo campo herdado, com o ramo de nulo preservado.

### O que o Docker fora do ar impede de verificar

1. Que a vitrine pública não regrediu (R-11).
2. Que o dono autenticado de verdade não cai no `NotAuthorized` em `GET /painel/configuracoes` — pendente desde o round 3.
3. Que `PanelAccessTests` continua verde com `UseAuthorization()` respondendo antes de `UsePanelAuthorization()` para as sete páginas, isto é, que o 302 com `?retorno=` não mudou de emissor.
4. Que a guarda recusa a gravação ponta a ponta contra o banco: a recusa está provada na base e a fiação até `ProductMaintenance.SaveAsync` não.

---

## Devolução ao plano

**T-31 permanece `Bloqueado`**, agora por um bloqueante só — o `/ObjStm` —, e ele tem entrada própria em `## 10. Questões em aberto`. O `Status:` não muda neste round.

Correções aplicadas nesta sessão, depois dos julgamentos: a varredura irmã que cobre os call sites, o `retorno` na guarda com caso próprio, os quatro números do registro, o título do XML doc de `PanelAuthorizationTests`, o XML doc de `PanelPageGuardTests` declarando o que **não** cobre, o comentário de `PanelLayout.razor`, `Camadas/arquivos afetados` com as sete pastas realmente tocadas, e as duas entradas novas em questões em aberto.

Itens que pedem entrada no plano como tarefa:

1. **R-01** — a ADR da leitura de PDF fora do processo web. Resolve junto o R-04 do round 2.
2. **R-02** — a reconferência antes do efeito irreversível, com `CancellationToken` em `CatalogPage`.
3. **R-03** — os dois casos de fiação por atributo e o caso de página real num `Renderer`.
4. **R-04** — as notas na ADR-010 e na ADR-006, e a citação de RN-58/CA-26 nas peças novas.
5. **R-05 do round 3** — os dez importantes do round 2, via `planner-leanwork`.

---

## Conclusão

Este é o primeiro round de T-31 em que a peça central resistiu ao ataque. A diferença em relação ao round 3 não está no esforço — está em onde a verificação foi buscada: o round 3 aceitou "compilou e os testes passam" como sinal de que o `AuthorizeRouteView` funcionava, e este round exigiu um `Renderer` real instanciando a página pelo caminho que o framework usa. A pergunta que decidia tudo — se a Task cascateada é reatribuída — não se responde lendo o código do projeto; responde-se lendo a fonte do framework e observando a execução. Foi o que faltou duas vezes nesta tarefa.

As duas ressalvas que sobraram são honestas e de naturezas diferentes. A janela TOCTOU é um limite real do desenho escolhido: guarda de entrada protege contra a aba abandonada, não contra a sessão que morre no meio de uma operação de noventa segundos — e o caso em que isso dói é justamente a geração de catálogo, onde o dano é permanente. A ausência de teste na fiação por atributo é a mesma lição de sempre, mais um degrau acima: agora o efeito tem teste, a regra tem varredura, e o que ainda não tem é a ligação entre o atributo e o framework. Cada round desta tarefa fechou um elo e revelou o seguinte, e isso não é desperdício — é o que acontece quando a verificação finalmente acompanha a declaração.

O que mantém T-31 bloqueada não é mais nada disso. São 470 bytes de PDF comprimido e uma decisão de arquitetura que quatro rodadas de emenda léxica não substituem.
