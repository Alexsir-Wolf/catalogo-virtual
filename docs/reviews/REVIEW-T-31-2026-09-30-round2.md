# Review: T-31 — Tela de configurações e envio da capa (round 2)

> **Plano de referência:** `docs/plans/PLAN-001-catalogo-virtual.md`
> **PRD de referência:** `docs/prds/PRD-001-catalogo-virtual.md`
> **Arquitetura de referência:** `docs/architecture/proposta-arquitetural.md`
> **SPEC-UI de referência:** `docs/prototype/SPEC-UI-001-catalogo-virtual.md`
> **Round anterior:** `docs/reviews/REVIEW-T-31-2026-09-29.md` (round 1, ⛔ Bloqueado — 1 bloqueante, 7 importantes, 5 sugestões)
> **Reviewer:** Claude (skill `reviewer-leanwork` v1.0), com três julgamentos independentes em paralelo — um de validação de capa e parsing de PDF, um de sessão/autenticação/concorrência, um de aderência ao spec, interface e cobertura
> **Data:** 2026-09-30
> **Round:** 2
> **Recomendação final:** ⛔ Bloqueado — não por escopo, e não por regressão de qualidade: as treze correções do round 1 existem e a maioria está bem feita, mas **duas delas pararam um passo antes do fim**, e nos dois casos o passo que falta é o que produz o efeito prometido

---

## Sumário executivo

Onze dos treze findings do round 1 foram atacados com correções de qualidade acima da média deste projeto, e três merecem destaque pelo diagnóstico, não pelo código: descobrir que o estouro de pilha não estava em `PdfReader.Open` e sim em `PageCount`/`Pages`, que achatam a árvore; descobrir que ligar `OnValidatePrincipal` ao validador do framework com um `AddCookie` avulso devolve **500** em vez de redirecionar, porque `SignOutAsync` desloga três esquemas e só um estava registrado; e reconhecer que reusar o lockout do Identity na troca de senha trocaria tentativa ilimitada por negação de serviço contra o próprio dono. Nenhuma dessas três coisas se descobre lendo documentação — todas foram sondadas.

O que bloqueia são dois findings, e eles têm a **mesma forma**: um mecanismo de defesa foi construído, está correto no que faz, e não está ligado ao ponto onde produziria efeito.

**R-01 — o guarda contra o PDF que derruba o processo é contornável por 20 bytes, e foi reproduzido.** A travessia da árvore de páginas com detecção de ciclo está certa e fecha o caso original do round 1. O problema é a segunda camada: `PdfNestingScan` decide se o arquivo é seguro para entregar ao parser varrendo os bytes **lexicamente**, e reconhece `stream` em qualquer posição — inclusive dentro de um nome como `/Xstream`. Basta isso para a varredura saltar até o próximo `endstream`, engolir o aninhamento profundo que está no meio e devolver `Safe`. O parser do PDFsharp não tem essa regra: ele lê o array e recursa. Reproduzido nesta sessão em projeto isolado, compilando os **arquivos reais** do repositório com PDFsharp 6.2.4 / net10.0: o payload que o teste protege (10.318 bytes) sai `scan=TooDeep → MalformedStructure`; o mesmo payload com `/Xstream 0` acrescentado (10.338 bytes) sai `scan=Safe` e o processo morre com `Stack overflow.`, `Parser.ParseObject`/`Parser.ReadArray` repetidos 2.917 vezes, `exit 127`. É a terceira rodada do mesmo defeito, e a causa-raiz é estrutural: **um guarda léxico na frente de um parser estrutural é contornável por construção**, e onde os dois discordam o guarda se desliga.

**R-02 — a revalidação do circuito não bloqueia nada, e a aba aberta continua gravando depois do logout.** `RevalidatingAuthenticationState` existe, valida pelo critério certo (o selo de segurança, o mesmo do cookie) e está registrada em `Program.cs:69`. Mas `RevalidatingServerAuthenticationStateProvider.ForceSignOut`, na fonte do framework, apenas troca o estado cascateado por anônimo — não encerra o circuito, não navega, não lança. E `Routes.razor:3` usa `RouteView` puro: não há `AuthorizeRouteView`, e **não existe um único `[Authorize]` ou `AuthorizeView` em `src/`** (verificado por varredura). Nenhuma das sete páginas interativas do painel reage ao estado anônimo. Em `SettingsPage.razor`, só `ChangePasswordAsync` lê `Authentication`; `UploadCoverAsync` e `SaveContactAsync` não consultam nada. O resultado é que a frase com que o commit `0e2ecbb` declara a correção — *"A aba continuava salvando contato e trocando a capa publica depois de o dono ter saido, inclusive num computador que nao e dele"* — **continua descrevendo o comportamento atual**. Nenhum teste toca o provider revalidante, então a suíte verde não cobra isso.

Os dois bloqueantes têm em comum uma coisa que vale registrar porque é a lição da tarefa: **o elo final não tem teste, e por isso não tem quem o cobre.** Em R-01 o contorno passa pelo único caso que a suíte usa para provar a defesa; em R-02 o mecanismo inteiro é invisível para a suíte. Nos dois, a correção foi verificada pelo executor por sondagem descartável fora do repositório — evidência de menor força que a suíte, e que não sobrevive à próxima mudança.

**Desacordo entre julgamentos, registrado:** o julgamento de spec concluiu que R-01 estava resolvido, com base no código da travessia e nos casos de teste. O julgamento de parsing o reproduziu quebrado. Fico com a reprodução — é evidência de execução contra leitura de código, e o payload está transcrito abaixo para quem quiser repetir.

**Findings por severidade:**

| Severidade | Quantidade |
|------------|------------|
| Bloqueante | 2 |
| Importante | 10 |
| Sugestão | 11 |

---

## Verificação executada

`dotnet build Catalogo.sln` → **Compilação com êxito, 1 aviso, 0 erros**. O aviso é `BL0008` em `Login.razor:149`, pré-existente e alheio a esta tarefa. O `CS0618` de `PdfDocumentOpenMode.InformationOnly` que o round 1 registrou **desapareceu** — R-02 do round anterior está fechado no compilador.

`dotnet test` **não pôde rodar inteiro**: o Docker Desktop não está no ar nesta máquina (`failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`), e todo caso que depende de `PostgresFixture` falha na construção do fixture, antes de qualquer asserção. Rodei o subconjunto que não toca o banco:

```
dotnet test --filter CoverValidationTests|PdfNestingScanTests|ContactValidationTests|PasswordAttemptLimiterTests
Aprovado! – Com falha: 0, Aprovado: 61, Ignorado: 0, Total: 61
```

O que isso permite e o que não permite: as regras de validação de capa, de contato e o limitador de tentativas estão **verificados por execução**. Tudo que envolve requisição, cookie, circuito ou banco — `PortalSettingsTests`, `PanelAccessTests`, `AuthenticationFlowTests` — é julgado **por leitura**, e está dito onde isso pesa. O bloqueante R-01 foi verificado por reprodução em projeto isolado, compilando os arquivos reais do repositório; o bloqueante R-02, por leitura do código do projeto somada à fonte do `dotnet/aspnetcore`.

---

## Round anterior, item a item

| Finding do round 1 | Status | Onde se verifica |
|---|---|---|
| **R-01** — árvore de páginas recursiva derruba o processo | ⚠️ **Persiste** por outro caminho | Ciclo resolvido: travessia iterativa com `HashSet<PdfObjectID>` em `CoverValidation.cs:239-334`, teto de nós conferido antes do `Push` (`:320-323`), casos em `CoverValidationTests.cs:141,157,288`. Guarda de profundidade criado (`PdfNestingScan`). **Contornável** — ver R-01 deste round |
| **R-02** — `InformationOnly` obsoleto, leitura integral | ✅ **Resolvido** | `CoverValidation.cs:150` usa `Import`; build sem `CS0618`; teto reduzido a 4 MB (`:81`); `Size` como porteiro antes de abrir o stream (`SettingsPage.razor:300-306`); cópia única do tamanho exato (`:316`). O parse continua materializando tudo — declarado no próprio código (`:76-79`) como pendência de arquitetura, não como resolvido |
| **R-03** — troca de senha não invalidava a sessão | ✅ **Resolvido** na borda HTTP | `PanelAuthentication.cs:47-50` (`ValidationInterval = Zero` + `ISecurityStampValidator`), `:59-60` (`AddIdentityCookies()`), `:71-73` (`Events` intocado de propósito). Caso que reusa o mesmo cliente autenticado antes da troca: `PortalSettingsTests.cs:375-391`. Texto da tela corrigido: `SettingsPage.razor:208-211`. **Ressalva:** vale para requisições HTTP; dentro do circuito é inerte — R-02 deste round |
| **R-04** — `LoadAsync` gravava pelo caminho anônimo | ✅ **Resolvido** | `PortalSettingsService.cs:86-95` é `AsNoTracking` + `SingleOrDefault` + `?? new PortalSettings()`, sem `Add`/`SaveChanges`. Seed idempotente em migration própria: `20260929184430_PortalSettingsSeed.cs:24-29`, com `ON CONFLICT ("Id") DO NOTHING` para bancos que já tinham a linha criada em runtime. Contraprova em teste apaga a linha e afirma `Count == 0` após a leitura (`PortalSettingsTests.cs:269-277`, `:285-296`). O `Add` sobrevive só em `TrackedAsync`, alcançável apenas pelas escritas |
| **R-05** — CA-34 sem verificação real | ✅ **Resolvido** | `PortalSettingsTests.cs:541-557` — `[Fact]`, não `SkippableFact`, com `RecordingObjectStorage` (`:609-625`) afirmando o nome gravado, o prefixo `capa/` e o sufixo `.pdf`. Contraprova em `:563-574`: recusa não chega ao armazenamento. O `SkippableFact` do armazenamento real sobrevive em `:115-124`, como o finding pedia |
| **R-06** — estado `salvo` de UI-10 pela metade | ⚠️ **Parcialmente resolvido** | Código fechado: `SettingsPage.razor:266` (os três sinais entram no `StateId`), `:208` (bloco de senha com `data-estado="salvo"`), `:69-74` (confirmação própria do envio aceito, que não existia). **Cobertura não fechada:** o único caso de estado da folha (`PortalSettingsTests.cs:100-109`) afirma só `semCapa`; `salvo`, `capaRecusada`, `enviando`, `senhaIncorreta` e `contatoRecusado` seguem sem asserção. Não há bUnit no projeto, o que é decisão registrada — mas o elo continua aberto |
| **R-07** — WhatsApp sem normalização no servidor | ✅ **Resolvido** | `ContactValidation.cs:31-79` valida na borda: recusa texto sem dígito (`:43-49`), exige 12 a 15 dígitos com mensagem citando o código do país (`:51-59`), confere formato de e-mail (`:71-76`), aplica o teto de 120 caracteres antes do banco (`:106-107`). `PortalSettingsService.cs:109-112` recusa sem gravar nada; `:120` grava só dígitos. Nove casos, executados nesta sessão. **Fecha também o R-03 de `REVIEW-T-20-2026-09-29-round2.md`**, como o finding previa |
| **R-08** — `Valida: CA-38` fechado com metade inexistente | ✅ **Resolvido** | Plano marca RN-67 e RN-65 como parciais (linha 1197) e CA-38 como parcial (1198); T-24 recebeu a outra metade (linhas 955-956). E a metade existe: `CatalogComposer.cs:41-46` lê `PortalSettingsService` e alimenta `DocumentFooter`, com casos em `CatalogDocumentTests.cs:161-192`. Ressalva de elo em R-10 deste round |
| **R-09** — painel não revalidado no circuito, logout inexistente | ⚠️ **Metade resolvida, metade persiste** | Logout **resolvido e bem feito**: `Logout.razor` é estático (justificado em `:14-17`), POST com `AntiforgeryToken` (`:45-49`), renova o selo antes de sair (`:71-80`), redireciona com `forceLoad` (`:86`); testes em `PanelAccessTests.cs:93-116`, `:132-152` (afirma o selo, não só o cookie) e `:169-182` (GET não desloga). Revalidação do circuito: mecanismo criado, **enforcement ausente** — R-02 deste round |
| **R-10** — divergência "miniatura → PDF embutido" fora da SPEC-UI | ✅ **Resolvido** | `SPEC-UI:513` registra a divergência com razão e custo, citando o finding; `:493` e `:505` passaram a dizer "pré-visualização"; e `:499-501` acrescentou `UI-10.contatoRecusado`, que nasceu na implementação |
| **R-11** — tolerância "6%" era absoluta | ✅ **Resolvido** | `AspectRelativeTolerance` (`CoverValidation.cs:99`) e `drift` relativo (`:206`). Régua delimitada pelos dois lados: `CoverValidationTests.cs:98` (595×782 recusado) e `:110` (592×842 aceito). Carta em retrato fica 9,3% fora. O registro do plano foi corrigido (linha 1390) |
| **R-12** — divergências do padrão de T-13 | ✅ **Resolvido** nos três itens | Mensagem de transporte separada da de tamanho (`SettingsPage.razor:332-340`); objeto órfão logado no caminho de falha (`PortalSettingsService.cs:170-185`); `IdentityResult` do `UpdateAsync` tratado (`:242-250`). Ressalva de padrão em R-13 deste round |
| **R-13** — rastreabilidade e asserção (5 itens) | ⚠️ **Parcialmente resolvido** | (a) `Catalogo.csproj` no escopo — **resolvido** (plano linha 1205). (b) asserção de `semCapa` no elemento certo — **resolvido** (`PortalSettingsTests.cs:86-88`). (c) RN-65 como parcial — **resolvido** (linha 1197). (d) contagem de testes do histórico (linha 1384 diz "22 entregues"; eram 23) — **persiste**. (e) ids de `ContatosVitrine` (`SPEC-UI:528` declara `default, comMensagemPronta`; o componente emite `semContato`, `comMensagemPronta`, `comContato`, e `default` não é emitido) — **persiste** |

**Sobre o estado da tarefa:** T-31 está `Concluído` na tarefa (plano linha 1195) e `Concluído` na linha 1384 do histórico — os dois campos concordam. As correções do round 1 estão registradas nas linhas 1387-1390. Este relatório reabre o bloqueio: ver "Devolução ao plano".

---

## Avaliação por eixo

| Eixo | Esperado | Encontrado | Situação |
|---|---|---|---|
| 1. Aderência ao plano | Três blocos (capa, contato, senha), dez critérios de aceite | Todos os dez materializados; um com divergência não registrada, e a troca obrigatória da ADR-006 declarada e inexistente | ⚠️ ver R-09, R-11 |
| 2. Rastreabilidade | RN-61 a RN-68, CA-34/35/38/39, ADR-017 e ADR-006 | Todos os IDs existem na origem; nenhum fantasma. Faltam RN-60 e `contatoRecusado` na declaração da tarefa | ⚠️ ver R-12, R-21 |
| 3. Aderência ao spec | Validar lendo a estrutura do arquivo (risco declarado) | Lê a estrutura, e o guarda que a protege é contornável | ⛔ ver R-01 |
| 4. Cobertura de teste | 5 testes de integração prometidos | 23 entregues, e os dois elos que mais importam sem caso: o contorno da varredura e o provider revalidante | ⛔ ver R-01, R-02 |
| 5. Qualidade do código | Padrão do projeto (T-13 como referência no mesmo tipo de upload) | Constantes nomeadas, razões declaradas, ordem valida→sobe→grava preservada; orquestração do upload ainda no `.razor` | ⚠️ ver R-14 |
| 6. Conformidade de interface | UI-10 com 7 estados | Os 7 existem no código; 1 com asserção | ⚠️ ver R-06 do round 1 |

---

## Findings

### ⛔ Bloqueantes

#### R-01 — A varredura anti-aninhamento é desligada por um nome que contenha `stream`, e o processo volta a morrer

- **Eixo:** 3. Aderência ao spec / 4. Cobertura de teste
- **Referência cruzada:** risco declarado de T-31 ("PDF recebido é entrada não confiável… validar lendo a estrutura do arquivo"); RN-62; R-01 de `REVIEW-T-31-2026-09-29`
- **Evidência:** `src/Catalogo/Features/Settings/PdfNestingScan.cs:133` — `case (byte)'s' when StartsWith(content, index, "stream")` — e `:248-267` (`TrySkipStream`)
- **Descrição:** a varredura reconhece `stream` em qualquer posição do arquivo, sem exigir que seja **token**. Um nome como `/Xstream` casa, e `TrySkipStream` salta dali até o próximo `endstream`, engolindo o aninhamento profundo que está no meio. O veredito volta `Safe` e o arquivo é entregue ao PDFsharp, cujo parser não tem essa regra: ele lê o array e recursa.

  Reproduzido nesta sessão, em projeto isolado, compilando os **arquivos reais** `PdfNestingScan.cs` e `CoverValidation.cs` com PDFsharp 6.2.4 / net10.0, usando o mesmo construtor de PDF cru dos testes:

  ```
  3 0 obj <</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Xstream 0/Lixo [[[…5000…]]] endstream>>
  ```

  Saída observada:

  ```
  A bytes=10318 scan=TooDeep -> inspect=MalformedStructure   (payload sem o truque)
  B bytes=10338 scan=Safe    -> Stack overflow.
     Repeated 2917 times: Parser.ParseObject / Parser.ReadArray
     ... PdfReader.Open -> CoverValidation.Inspect
  === exit 127
  ```

  São **20 bytes** acrescentados ao payload que o caso `Aninhamento_sintatico_profundo_e_recusado_antes_de_chegar_ao_parser` (`CoverValidationTests.cs:242`) usa para provar a defesa.
- **Por quê é Bloqueante:** consequência idêntica à do R-01 original — `StackOverflowException` não é capturável em .NET, o runtime encerra o processo, e no Render o processo que serve o painel é **o mesmo que serve a vitrine pública**. Sem exceção, sem log, sem nada na tela além da conexão caindo. A atenuação também é a mesma e continua valendo: o envio exige autenticação como dono (RN-58), então o vetor não é anônimo — reduz a probabilidade, não a consequência. E é a **terceira** rodada do mesmo defeito na mesma tarefa, o que desloca o problema de "este caso" para "esta abordagem".
- **Sugestão de correção:** imediata, e pequena — só reconhecer `stream` quando for token: precedido por delimitador ou espaço, nunca por `/`, e depois do `>>` que fecha o dicionário. Junto com ela, aplicar ao caso ambíguo a mesma política que `0e2ecbb` adotou para a geometria: **o que a varredura não consegue medir com confiança é recusado**, em vez de liberado. E o desenho que fecha a classe em vez do caso continua sendo o que o próprio código registra em `CoverValidation.cs:76-79`: ler o arquivo fora do processo que serve requisições, com limite de pilha, memória e tempo. Enquanto essa decisão não for tomada, cada rodada fecha um contorno e abre espaço para o próximo — e o caso de teste precisa nascer do contorno, não do payload já conhecido.

#### R-02 — A revalidação do circuito não bloqueia nada: a aba aberta continua gravando depois do logout e da troca de senha

- **Eixo:** 1. Aderência ao plano / 5. Qualidade do código
- **Referência cruzada:** RN-58; R-09 de `REVIEW-T-31-2026-09-29`; a promessa de `Logout.razor:63-70` e do histórico do plano (linha 1390)
- **Evidência:** `src/Catalogo/Components/Routes.razor:3` (`RouteView` puro, sem `AuthorizeRouteView`); ausência total de `[Authorize]`/`AuthorizeView` em `src/` (verificado por varredura); `src/Catalogo/Features/Settings/SettingsPage.razor:285` e `:352` (`UploadCoverAsync` e `SaveContactAsync` não consultam `Authentication`, ao contrário de `ChangePasswordAsync` em `:387-389`); `RevalidatingAuthenticationState.cs:31-53`
- **Descrição:** `RevalidatingServerAuthenticationStateProvider.ForceSignOut`, na fonte do `dotnet/aspnetcore`, faz uma coisa só:

  ```csharp
  private void ForceSignOut()
  {
      var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
      var anonymousState = new AuthenticationState(anonymousUser);
      SetAuthenticationState(Task.FromResult(anonymousState));
  }
  ```

  Não encerra o circuito, não navega, não lança. Ele troca o estado cascateado por anônimo e espera que **alguém reaja** — e o projeto não tem quem reaja. Cenário completo:

  1. O dono abre `/painel/configuracoes` num computador que não é dele — o cenário que `Logout.razor:38-41` diz atender. Circuito interativo de pé.
  2. Do celular, faz `POST /painel/sair`: `UpdateSecurityStampAsync` + `SignOutAsync`.
  3. Na aba abandonada, em até 1 minuto (`RevalidatingAuthenticationState.cs:29`), a validação compara o claim de selo com o do banco, devolve `false`, e o estado vira anônimo.
  4. Quem está no computador clica **"Salvar contato"** ou escolhe um arquivo no `InputFile`. Nenhum dos dois handlers consulta identidade. **O contato da vitrine pública é reescrito e a capa do catálogo é trocada, depois do logout.**

  O gate `UsePanelAuthorization` (`Program.cs:97`) não alcança: o tráfego interativo é `/_blazor`, fora do prefixo `/painel`. O mesmo vale para a troca de senha feita de outro dispositivo — o cookie morre na próxima navegação HTTP, e o circuito segue escrevendo.
- **Por quê é Bloqueante:** é a regressão exata que o commit `0e2ecbb` declara ter corrigido, com estas palavras na mensagem: *"A aba continuava salvando contato e trocando a capa publica depois de o dono ter saido, inclusive num computador que nao e dele."* A frase continua verdadeira. Fechar R-09 neste round deixaria o plano e o histórico afirmando uma cobertura que não existe — e a única coisa pior que o defeito é o registro dizendo que ele foi tratado. Agrava que **nenhum teste toca o provider revalidante**, então a suíte verde não tem como cobrar isso. É a segunda vez, nesta mesma tarefa, que um mecanismo de sessão chega sem o elo final.
- **Sugestão de correção:** envolver o `RouteView` em `AuthorizeRouteView` com `NotAuthorized` navegando para `LoginPath` com `forceLoad`, **e** pôr `@attribute [Authorize]` nas sete páginas do painel como defesa em profundidade — o gate por caminho continua sendo a primeira barreira, para que tela nova não nasça aberta. Como rede imediata e barata, uma guarda nos handlers de escrita que recuse quando `Authentication` não trouxer identidade autenticada. O teste que fecha o elo não depende de Docker: instanciar o provider, forçar a falha de validação e afirmar que a gravação é recusada.

---

### 🟡 Importantes

#### R-03 — Filho de `/Kids` que não resolve é ignorado em silêncio: capa de duas páginas é aceita e quebra a geração para sempre

- **Eixo:** 3. Aderência ao spec
- **Referência cruzada:** CA-34, CA-35, RN-63; RN-36/RN-37 via T-32
- **Evidência:** `CoverValidation.cs:325-328` — `if (kids.Elements.GetDictionary(index) is { } kid)`, sem `else`
- **Descrição:** o filho que o PDFsharp não resolve como dicionário — referência pendurada, ou elemento que não é dicionário — é descartado sem contar e sem recusar. A travessia mede 1 página onde o documento tem 2. Reproduzido: com `<</Type/Pages/Kids[3 0 R 4 0 R]/Count 2>>` e o objeto 4 ausente da xref, a inspeção devolve aceitação e o `PageCount` do PDFsharp devolve 2.
- **Por quê é Importante:** a capa é aceita e gravada; depois, `CoverMerge.Append` (`src/Catalogo/Features/PdfExport/CoverMerge.cs:52-55`) itera por `document.PageCount` = 2 e estoura `NullReferenceException`, que `CatalogGeneration.cs:180-187` converte em "não foi possível gerar" — **sem nunca apontar a capa**. O dono fica com a geração permanentemente quebrada e nenhuma pista, e a validação que existe justamente para ele não descobrir na hora de gerar (ADR-017) disse que estava tudo bem.
- **Sugestão de correção:** filho que não resolve é `PageSurvey.Broken()`, não item ignorado — a política de recusar o que não se consegue medir já é a do projeto desde `0e2ecbb`. Como rede, conferir na validação que `document.PageCount` concorda com a contagem da travessia: é o número que T-32 vai usar.

#### R-04 — Existe um segundo caminho até o `PdfReader`, e esse não passa pela varredura

- **Eixo:** 5. Qualidade do código
- **Referência cruzada:** risco declarado de T-31 (defesa em profundidade)
- **Evidência:** `src/Catalogo/Features/PdfExport/CoverMerge.cs:49-50`, alimentado por `CatalogGeneration.cs:177-179`
- **Descrição:** a geração baixa a capa do bucket e a entrega a `PdfReader.Open` **sem** `PdfNestingScan`, confiando em que os bytes armazenados são os que passaram por `Inspect`. A premissa não vale para capas enviadas antes de `42d917d`/`0e2ecbb`, quando a varredura não existia, nem para qualquer alteração direta no bucket — que é público.
- **Por quê é Importante:** uma capa aninhada gravada naquela janela mata o processo **na geração**, em serviço `Scoped` chamado do circuito do painel — mesmo processo da vitrine. O `catch` de `CatalogGeneration.cs:180` não intercepta estouro de pilha.
- **Sugestão de correção:** rodar a varredura sobre os bytes baixados antes de concatenar, recusando a geração com mensagem que mande reenviar a capa. Custa uma passada linear em 4 MB.

#### R-05 — Open redirect autenticado no `retorno` da tela de acesso

- **Eixo:** 5. Qualidade do código (segurança)
- **Referência cruzada:** ADR-006; pré-existente de T-07, nunca levantado
- **Evidência:** `src/Catalogo/Features/Account/Login.razor:151-152` (`[SupplyParameterFromQuery(Name = "retorno")]`) e `:177` (`Navigation.NavigateTo(ReturnUrl ?? PanelAuthentication.PanelPathPrefix)`)
- **Descrição:** o destino vem da query string e vai direto ao `NavigateTo`, sem validação de origem. O `EditForm` de SSR posta para a mesma URL com query, então o parâmetro sobrevive ao POST; em `HttpNavigationManager.NavigateToCore` o destino vira `ToAbsoluteUri(uri).AbsoluteUri` e sai como `Location`, sem restrição de mesma origem. Entrada concreta: `/painel/entrar?retorno=https%3A%2F%2Fevil.example%2Fpainel%2Fentrar`. O dono clica no link, digita a senha certa **no domínio legítimo** — o que valida o link a seus olhos — e é depositado numa cópia da tela de acesso pedindo a senha "de novo". `retorno=//evil.example` funciona igual.
- **Por quê é Importante:** a ADR-006 estabelece que não há recuperação de senha; a senha do dono é credencial única e irrecuperável, e este é um caminho de phishing que usa o próprio domínio como aval. Não é Bloqueante porque exige que o dono interaja com um link plantado. `UsePanelAuthorization:97-98` só monta valores relativos, então nenhum fluxo legítimo precisa de URL absoluta.
- **Sugestão de correção:** aceitar apenas caminho local — recusar o que não começar com `/`, o que começar com `//` ou `/\`, e o que tiver esquema; na dúvida, `PanelPathPrefix`. Caso de teste: `?retorno=https://evil.example` termina em `/painel`.

#### R-06 — Tentativa em sequência contra a senha atual não deixa rastro nenhum

- **Eixo:** 5. Qualidade do código (segurança)
- **Referência cruzada:** RN-60; critério de log de T-28
- **Evidência:** `PortalSettingsService.cs:219-231` e `:258-264` — nenhum `Log*` no registro de falha nem no retorno de bloqueio
- **Descrição:** o limitador conta e bloqueia, e a metade que avisa não existe. O único `LogWarning` do método (`:246-248`) é sobre falha do `UpdateAsync`. Compare com o caminho de acesso, corrigido no mesmo commit: `Login.razor:185-187` — *"Aviso, e não informação: bloqueio por tentativas é o sinal de que alguém está adivinhando, e é o que precisa aparecer num log filtrado por severidade."*
- **Por quê é Importante:** a própria `PasswordAttemptLimiter.cs:12-16` declara que reiniciar a aplicação zera a contagem e que o atacante "ganha, no máximo, mais um punhado de tentativas a cada reinício" — essa escolha só é defensável com detecção, e não há detecção. Nem o log do Identity socorre, porque a confirmação passa pelo `UserManager`, fora do `SignInManager`.
- **Sugestão de correção:** `LogWarning` no retorno de bloqueio, no mesmo nível e vocabulário do `Login.razor`, sem senha e sem dizer se a atual conferia; e caso com `FakeLogger` afirmando o registro, que é o padrão que `AuthenticationLogTests` já estabeleceu.

#### R-07 — O logout pula a renovação do selo em silêncio quando não identifica o dono

- **Eixo:** 5. Qualidade do código (segurança)
- **Referência cruzada:** RN-58; R-09 de `REVIEW-T-31-2026-09-29`
- **Evidência:** `Logout.razor:71-82`
- **Descrição:** a renovação do selo — que o comentário de `:63-70` chama de "o que faz a saída alcançar as abas abertas" — está atrás de três condições encadeadas (`AuthenticationState is { } state`, `identity is { IsAuthenticated: true, Name: { } userName }`, `FindByNameAsync(userName) is { } owner`) cujo caminho falso não faz nada e não registra nada. Qualquer uma falhando pula `UpdateSecurityStampAsync`, executa só `SignOutAsync` e redireciona **com aparência de sucesso**: a tela de acesso aparece, o dono vai embora, e as abas abertas seguem vivas.
- **Por quê é Importante:** falha silenciosa num controle de segurança cuja evidência de funcionamento é invisível ao usuário — e a tela afirma o contrário. O caminho é alcançável se o `ClaimsIdentity.Name` não estiver no principal, e a configuração de `ClaimsIdentity` não é fixada por este projeto.
- **Sugestão de correção:** derivar o dono do próprio `HttpContext.User` via `UserManager.GetUserAsync` — a página é SSR, o principal está ali — em vez do `Name` cascateado, e logar em `Warning` o caminho em que a renovação não aconteceu, para que o teste possa afirmá-lo em vez de depender do resultado feliz.

#### R-08 — As correções de `0e2ecbb` e `4684fa1` mudaram comportamento de T-31 e não estão no histórico

- **Eixo:** 2. Rastreabilidade
- **Evidência:** `docs/plans/PLAN-001-catalogo-virtual.md:1403` (única menção a `4684fa1`, atribuída só à reescrita de `CategoryMaintenance.DeleteAsync`); `0e2ecbb` não é citado em nenhum ponto do plano
- **Descrição:** ficou sem registro: o teto de bytes de 12 MB → **4 MB** (`CoverValidation.cs:81`, e a mensagem ao dono muda com ele, `:392-393`); a mensagem de contagem truncada (`:382-384`); `MediaBox` com cantos invertidos no eixo X passando a ser aceita (`:281`, `:346-347`); `contatoRecusado` entrando no `StateId` (`SettingsPage.razor:263`); a cópia única do envio (`:316`); e as três saídas silenciosas de `PdfNestingScan`, que eram **reabertura do bloqueante**. A mesma linha 1403 atribui a criação de `DatabaseHealth.cs` a `c09690e`, quando o arquivo nasceu em `0e2ecbb`.
- **Por quê é Importante:** nenhuma RN ou critério respalda o teto de 4 MB, então ele existe apenas como decisão — e a decisão não está em artefato nenhum. O plano já registra decisões numéricas assim (fez isso para a tolerância de proporção); esta ficou de fora.
- **Sugestão de correção:** linha de histórico própria para os dois commits, com o teto de bytes declarado como decisão e a segunda rodada de `PdfNestingScan` nomeada como tal.

#### R-09 — O teto de 64 páginas contraria a letra da RN-63 e o texto de `UI-10.capaRecusada`, sem registro

- **Eixo:** 3. Aderência ao spec / 6. Conformidade de interface
- **Evidência:** `CoverValidation.cs:112` (`MaxPagesToCount = 64`) e `:382-384`; RN-63 em `PRD:229`; `SPEC-UI:495`
- **Descrição:** acima de 64 páginas a recusa deixa de informar quantas foram encontradas. RN-63, na letra: "Arquivo com mais ou menos páginas é recusado, **informando quantas foram encontradas**". SPEC-UI: "**Motivo exato** — número de páginas encontrado, ou orientação incompatível". Enviando o catálogo inteiro de 300 páginas — que a própria mensagem nomeia como o cenário provável —, o dono lê "este arquivo tem mais de 64".
- **Por quê é Importante:** a decisão é defensável e é melhor que o número inventado que existia antes, mas é exatamente a classe de divergência que o R-10 do round 1 obrigou a registrar na SPEC-UI — e esta não foi registrada. Vale também para as razões `TooLarge` e `MalformedStructure`, que a SPEC-UI não enumera em `capaRecusada`.
- **Sugestão de correção:** observação em UI-10 com o teto, a razão (percorrer centenas de milhares de folhas para dizer "não é uma" não se paga) e as seis razões de recusa que a tela pode exibir hoje.

#### R-10 — A metade de CA-38 que T-24 assumiu prova o formatador, não a fiação

- **Eixo:** 4. Cobertura de teste
- **Referência cruzada:** CA-38, RN-67 — elo criado pela correção de R-08 do round 1
- **Evidência:** `tests/Catalogo.Tests/CatalogDocumentTests.cs:161-176` contra `src/Catalogo/Features/PdfExport/CatalogComposer.cs:41-46`
- **Descrição:** o único caso que sustenta "o rodapé carrega os contatos das Configurações" instancia `DocumentFooter` à mão; nada exercita a leitura de `PortalSettingsService` dentro do composer. Trocar `new DocumentFooter(portal.Phone, portal.WhatsApp, portal.Email)` por `new DocumentFooter(null, null, null)` — ou inverter dois argumentos, plausível porque `DocumentFooter(string? Phone, string? WhatsApp, …)` inverte a ordem em que o resto do sistema nomeia os canais — deixa a suíte verde.
- **Por quê é Importante:** é a mesma falha de elo que o R-05 do round 1 corrigiu do outro lado — critério declarado fechado sem verificação do caminho que importa.
- **Sugestão de correção:** um caso que grava contato por `SaveContactAsync` e afirma o valor no documento composto; se extrair texto do PDF for caro, afirmar ao menos que o composer entrega ao `DocumentFooter` o que leu das configurações.

#### R-11 — `MustChangePassword` é escrito em dois lugares e lido em nenhum: a troca obrigatória da ADR-006 não existe, e três textos afirmam que existe

- **Eixo:** 1. Aderência ao plano / 2. Rastreabilidade
- **Referência cruzada:** ADR-006, declarada **Decisão base** de T-31 e marcada "Respeitada" no round 1 e no histórico do plano (linha 1387)
- **Evidência:** `OwnerAccount.cs:15` (declaração), `OwnerAccountSeeder.cs:35` (`= true`), `PortalSettingsService.cs:240` (`= false`). Varredura de `src/` e `tests/` fora de migrations devolve **essas três ocorrências e nenhuma leitura** — sem guard, sem redirect, sem aviso na tela, sem teste
- **Descrição:** o campo que materializa a troca obrigatória não é consultado por caminho nenhum. Desligá-lo não cumpre a ADR, porque o que a ADR pede é **cobrar** a troca, não registrar que ela aconteceu. Três textos afirmam o contrário: ADR-006 (`proposta-arquitetural.md:271`) — *"A conta é semeada na primeira subida, **com troca de senha obrigatória**"*; `OwnerAccount.cs:12-13` — *"Enquanto não for trocada, **o painel cobra a troca no primeiro acesso** (ADR-006)"*; `OwnerAccountOptions.cs:4-5` — *"a senha **deve ser trocada** no primeiro acesso (ADR-006)"*.
- **Por quê é Importante:** `render.yaml:19-21` injeta `Owner__Password` como variável de ambiente com `sync: false`. Quem tem acesso ao dashboard da plataforma lê a senha do dono ali, e **nada no sistema obriga que ela deixe de valer** — o dono pode operar o painel por meses com a credencial de implantação. Não é Bloqueante porque exige acesso à configuração da plataforma, e é lacuna de controle, não defeito de dados reproduzível. **Por que o dono é T-31 e não T-07:** o round 1 tocou nisso dentro de R-12 e concluiu *"sem consequência observável hoje, porque `MustChangePassword` não é consultado em lugar nenhum"* — tratou a ausência de leitura como atenuante do descarte do `IdentityResult`, não como o defeito. E o histórico do plano (linha 1387) usa o desligamento da flag para declarar *"ADR-006 — Respeitada, e a tarefa fecha uma pendência… era exigência da ADR que T-07 persistia e nunca cobrava"*. A correção de R-12 está certa e verificada (`PortalSettingsService.cs:242-252`, com `LogWarning`), mas o elo que essa frase fecha não existe.
- **Sugestão de correção:** ou implementar a cobrança — redirecionar para `/painel/configuracoes` enquanto a flag for verdadeira, no mesmo gate de `UsePanelAuthorization`, com o bloco de senha em evidência —, ou remover o campo e corrigir os três textos, registrando a decisão na ADR-006. O estado atual, em que a coluna existe, a ADR promete e nada acontece, é o que não se sustenta. Teste que fecha o elo: conta recém-semeada é redirecionada para as configurações ao tentar `/painel/produtos`.

#### R-12 — `Telas:` de T-31 lista seis estados; a SPEC-UI declara sete

- **Eixo:** 2. Rastreabilidade
- **Evidência:** `docs/plans/PLAN-001-catalogo-virtual.md:1200` contra `SPEC-UI:499`
- **Descrição:** `UI-10.contatoRecusado` foi acrescentado à SPEC-UI pela correção de R-10 e declarado como nascido em T-31, mas o campo `Telas:` da tarefa não o inclui. A verificação por estado percorre `Telas:` para saber o que cobrar de cada tarefa; hoje o estado existe no documento de interface e no código (`SettingsPage.razor:263`) e não existe no único lugar que o liga à tarefa que o entregou.
- **Sugestão de correção:** `UI-10 (default, semCapa, capaRecusada, enviando, senhaIncorreta, salvo, contatoRecusado)`.

---

### 🔵 Sugestões

#### R-13 — O veredito da varredura é descartado, e a investigação fica sem o motivo

`PdfNestingScan.Scan` distingue `TooDeep`, `UnbalancedDelimiters` e `UnterminatedToken`, e o próprio XML doc diz que a distinção existe "para que cada motivo tenha caso de teste próprio" — mas em produção os três colapsam em `MalformedStructure` (`PortalSettingsService.cs:148-152`) e o log registra só a razão genérica. Se uma capa legítima for recusada pela heurística, ninguém descobre qual regra mordeu. Levar o veredito ao log de recusa, sem mudar a mensagem ao dono.

*Verificado e descartado como achado:* o risco de falso positivo da varredura em PDF real — 40 documentos com `CompressContentStreams` e imagem de bytes aleatórios passaram todos, nenhuma recusa.

#### R-14 — A orquestração do upload da capa continua no `.razor`, ao contrário de T-13, e por isso sem teste

`SettingsPage.razor:285-350` guarda a decisão. Em T-13 o mesmo tipo de upload virou serviço (`ProductPhotoUpload.StoreAsync`, com mensagens em constantes nomeadas e testes próprios), e a extração foi — segundo o histórico — a correção pedida no review de T-16 justamente porque "a decisão central vivia num manipulador de evento, onde nada a protegia". Aqui o porteiro de `Size` (`:300`) e a separação de `IOException` (`:332`) são exatamente as duas correções de R-02/R-12 do round 1, e **nenhuma das duas tem teste**: apagar a linha `:300` deixa a suíte verde. As mensagens também são literais inline, fora do padrão.

#### R-15 — O intervalo de 1 minuto é uma janela não declarada, e há dois critérios para a mesma decisão

`RevalidatingAuthenticationState.cs:29` usa 1 minuto; `PanelAuthentication.cs:47-48` usa zero. O `Task.Delay(RevalidationInterval)` da fonte do framework roda **antes** da primeira validação, então o piso da janela é o intervalo inteiro. E o comentário de `:16-18` afirma "um só lugar decide se a identidade ainda vale" — na prática são dois lugares com dois intervalos, o que é defensável e contradiz o texto. Depois de resolvido R-02, alinhar a promessa da tela de saída ao mecanismo ("pode levar até um minuto para alcançar outras abas") ou reduzir o intervalo.

#### R-16 — A mensagem de bloqueio pode prometer minutos e liberar em um segundo

`PasswordAttemptLimiter.cs:42-53` e `:69-70`: a janela é fixa e ancorada na **primeira** falha (`windowStart` só é escrito quando `failures == 1`). Com 4 falhas em `t=0` e a 5ª em `t=4:59`, a tela diz "Aguarde alguns minutos" e a contagem zera em `t=5:00`. Efeito colateral: até 10 tentativas num intervalo curto atravessando a borda. O teto médio continua de pé; o que erra é o texto. Ancorar na última falha, ou devolver o tempo restante para a mensagem dizer um número.

#### R-17 — `ValidationInterval = Zero` cobra do dono também na vitrine, contra o que o comentário afirma

`PanelAuthentication.cs:45-46` diz que "o custo é uma consulta por requisição **autenticada**, e a vitrine é anônima" — verdadeiro para o visitante, falso para o dono. `UseAuthentication` autentica o esquema padrão em toda requisição que traga o cookie, inclusive `/` e `/produto/...`, e por `StorefrontCache.IsCacheable` nenhuma dessas é cacheada. O comportamento está correto e o cache protegido; o registro é que está pela metade.

#### R-18 — A honestidade da contagem não chegou ao log

`PortalSettingsService.cs:148-151` emite `inspection.PagesFound` sem considerar `PageCountTruncated`. O PDF de 300 páginas produz "Páginas encontradas: 65" no log — exatamente o número inventado que `4684fa1` removeu da tela.

#### R-19 — O estado `carregando` não existe em nenhuma tabela da SPEC-UI

`SettingsPage.razor:255`, `CatalogList.razor:140` e `CatalogPage.razor:394` emitem `data-estado="carregando"`, e o documento de interface não declara esse estado em nenhuma delas. É padrão já estabelecido no projeto — por isso sugestão —, mas é estado observável que ninguém especificou. Declarar nas telas interativas, ou registrar como convenção transversal na seção de componentes.

#### R-20 — O comentário do caso de estado promete duas asserções e há uma

`PortalSettingsTests.cs:98` diz "Este caso ancora **os dois estados** que se pode alcançar por requisição sem dirigir o circuito", e `:108` afirma só `semCapa`. O `default`, com capa configurada, não é exercitado. Acrescentar o caso ou corrigir o comentário.

#### R-21 — RN-60 é materializada em T-31 e não é declarada por ela

`PortalSettingsService.cs:219-231` entrega o teto de tentativas contra a senha atual, com caso nomeado `RN_60_…` na suíte da tarefa, e o `Implementa:` de T-31 (linha 1197) não cita RN-60. Declarar como parcial, no padrão já usado para RN-65 e RN-67: *RN-60 (parcial — o teto no formulário de troca; o do acesso é de T-07)*.

#### R-22 — A linha de T-31 no histórico está sem commit, e os hashes são conhecidos

`docs/plans/PLAN-001-catalogo-virtual.md:1384` tem `—` na coluna Commit, ao contrário da maioria das linhas. Os commits existem e foram verificados nesta sessão: `fba27be` (código, 2026-09-28) e `4c2eaac` (registro).

#### R-23 — Resíduos das rodadas de correção

Três itens pequenos, todos de leitura: (a) `Logout.razor:70` e `SettingsPage.razor:315` citam `REVIEW-T-31-2026-09-29-round2`, relatório que **não existe** em `docs/reviews/` — e os IDs `R-02`/`R-05` "de round 2" que eles nomeiam vão colidir com a numeração **deste** relatório; (b) `stream`/`endstream` aparecem como literais soltos em `PdfNestingScan.cs:133`, `:250`, `:254`, `:258`, no ponto mais sensível do arquivo, enquanto `CoverValidation.cs:121-127` nomeia todas as chaves em constantes; (c) `CoverValidation.cs:336-347` e `SettingsPage.razor:238-252` têm duas tags `<summary>` no mesmo membro — em `CoverValidation` o primeiro bloco descreve `Oriented` e está pousado sobre `HasArea`.

---

## Nota de segurança

Verificado e **sem** achado, além do que está em R-01, R-02, R-05, R-06 e R-07:

- **Nome do objeto** gerado no servidor (`capa/{Guid:N}.pdf`), nunca derivado do nome enviado — sem travessia de caminho nem sobrescrita.
- **Ordem valida → sobe → grava** preservada (`PortalSettingsService.cs:145-172`): nenhum caminho de falha deixa o registro apontando para objeto inexistente nem apaga a capa boa.
- **Teto de bytes aplicado duas vezes antes de qualquer alocação** — `Size` como porteiro antes de abrir o stream, e o teto de nós conferido antes do `Push`.
- **Sem endpoint HTTP** de upload, contato ou senha: tudo passa pelo circuito. `UseAntiforgery` depois de `UseAuthentication`, que é a ordem que vincula o token à identidade. O logout é POST com antiforgery, e há caso afirmando que um GET não desloga.
- **Cookie** com `HttpOnly`, `SameSite=Strict` e `SecurePolicy=Always` (`PanelAuthentication.cs:63-66`).
- **Guarda de bloqueio antes de verificar a senha** (`PortalSettingsService.cs:219`): durante o bloqueio a resposta é a mesma para senha certa e errada — não há oráculo.
- **Limitador próprio, não o do Identity**: compartilhar o contador permitiria que quem tivesse uma sessão aberta mantivesse o dono permanentemente fora do login. A decisão está declarada no código, com a razão.
- **Seed idempotente** do registro único, com `ON CONFLICT DO NOTHING` para bancos que já tinham a linha criada em runtime.
- **Segredos:** a `ServiceKey` só viaja em header; nenhum log registra senha, token ou conteúdo de arquivo.

---

## Devolução ao plano

**Há dois findings Bloqueantes, e T-31 está marcada `Concluído`.** É a divergência entre estado declarado e estado validado que o `/leanwork-trace` reporta como grave, e a correção de estado **não foi aplicada**: mudar o `Status:` depende de aval, e este relatório existe para embasar a decisão.

Consequência a considerar, porque não é óbvia e já foi registrada no round 1: **T-20 depende de T-31 e está `Concluído`.** O que T-20 consome são os dados de contato, que funcionam — nada em T-20 passa pela validação de capa nem pela revalidação do circuito. Marcar T-31 como `Bloqueado` não invalida T-20, mas deixa o grafo com uma tarefa concluída dependendo de uma bloqueada, e isso pede nota explícita no histórico para não parecer erro.

Itens que pedem entrada no plano como tarefa, independentemente da decisão de status:

1. **R-01** — o token de `stream` e a política de recusar o ambíguo, agora; e a decisão de arquitetura que fecha a classe: ler o arquivo fora do processo que serve requisições. A segunda também resolve R-04 e o resíduo de R-02 do round 1.
2. **R-02** — `AuthorizeRouteView` mais `[Authorize]` nas sete páginas do painel, com o teste que instancia o provider e afirma a recusa.
3. **R-03** — filho de `/Kids` que não resolve passa a ser recusa, mais a conferência contra `PageCount`.
4. **R-05** — validação de caminho local no `retorno`, com caso de teste.
5. **R-06 + R-07** — os dois logs que faltam nos controles de sessão, no padrão de `AuthenticationLogTests`.
6. **R-10** — o caso que prova a fiação do rodapé, e não o formatador.
7. **R-11** — decidir entre implementar a cobrança da troca de senha ou remover o campo e corrigir os três textos; o estado atual não se sustenta.

R-08, R-09, R-12 e R-22 são atualizações de documento — plano e SPEC-UI. R-13 a R-21 e R-23 são correções pequenas e localizadas.

---

## Conclusão

Esta tarefa foi corrigida com cuidado e com diagnóstico de verdade. Onze dos treze findings do round 1 foram atacados, quatro deles exigiram sondagem para entender a causa, e as decisões que saíram dali — `AddIdentityCookies()` em vez de reescrever o validador, limitador próprio em vez do lockout do Identity, seed em migration em vez de criação na leitura — são as decisões certas, com as razões escritas onde o próximo leitor vai precisar delas. O R-04 tem contraprova em teste. O R-07 fechou, de passagem, um finding órfão de outro relatório. A tela deixou de afirmar ao dono coisas que não eram verdade, que era a parte mais grave do round 1.

O que bloqueia não é qualidade de código: é que **dois mecanismos de defesa foram construídos e não foram ligados ao ponto onde produzem efeito.** A varredura de PDF é correta e está na frente do parser errado — lexical contra estrutural, e onde discordam ela se desliga, por 20 bytes. A revalidação do circuito é correta e ninguém escuta o que ela decide — `ForceSignOut` troca o estado e as sete páginas do painel seguem gravando. Nos dois casos, o commit que declara a correção descreve um comportamento que o código atual não tem.

O padrão aparece uma terceira vez, fora dos bloqueantes, e por isso vale nomeá-lo: **este código declara controles de sessão em comentário e em ADR com mais frequência do que os liga.** Foi exatamente isso que o round 1 cobrou em R-03, quando a tela afirmava que a sessão caía e ela não caía — e a correção daquele finding é boa. Mas o logout fala de abas que não alcança, a revalidação revalida sem bloquear, e a ADR-006 cobra uma troca de senha que ninguém cobra (R-11). Os três textos sobre `MustChangePassword` descrevem um comportamento que nunca existiu em nenhuma linha executável.

E há um padrão comum aos dois bloqueantes que vale mais que qualquer finding individual: **o elo final não tem teste.** O contorno de R-01 passa exatamente pelo caso que a suíte usa como prova; o provider de R-02 não é tocado por caso nenhum. As duas correções foram verificadas por sondagem descartável fora do repositório, e sondagem não sobrevive à próxima mudança — é por isso que a terceira rodada do mesmo defeito de PDF chegou aqui. O que fecha essa tarefa não é mais uma correção pontual: é um teste que nasça do contorno, e a decisão de arquitetura que o próprio código já registra como a saída definitiva.
