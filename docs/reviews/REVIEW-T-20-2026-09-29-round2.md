# Review: T-20 — Tela de detalhe do produto

> **Plano de referência:** `docs/plans/PLAN-001-catalogo-virtual.md`
> **PRD de referência:** `docs/prds/PRD-001-catalogo-virtual.md`
> **Arquitetura de referência:** `docs/architecture/proposta-arquitetural.md`
> **SPEC-UI de referência:** `docs/prototype/SPEC-UI-001-catalogo-virtual.md`
> **Round anterior:** `docs/reviews/REVIEW-T-20-2026-09-28.md` (round 1, ⛔ Bloqueado)
> **Reviewer:** Claude (skill `reviewer-leanwork` v1.0), com julgamento independente do diff em paralelo
> **Data:** 2026-09-29
> **Round:** 2
> **Recomendação final:** ⚠️ Aprovado com ressalvas

---

## Sumário executivo

**O bloqueante do round 1 caiu, e caiu na raiz.** R-01 não era defeito, era escopo ausente por dependência não declarada: a RN-54 pede contato com dados vindos das Configurações, e esses dados só existiram quando T-31 os criou. Com eles no lugar, o bloco de contato de UI-02 entrou completo — WhatsApp com mensagem já escrita citando o produto (RN-55, CA-25), telefone e e-mail ao lado dele e não no lugar dele (RN-54, CA-31). Os dois critérios de aceite que ficaram abertos fecharam, e os seis da tarefa estão marcados.

Duas decisões de desenho merecem registro porque resolvem problemas que o round 1 apontou em vez de contorná-los. A primeira: **a montagem da URL saiu da tela**. `WhatsAppConversation` é o único ponto de substituição da copy, exatamente como a SPEC-UI observa para UI-02 — e o nome do produto vai codificado, com caso de teste para `Cadeira Ergonômica "Pró"` e `Papel A4 75g/m²` mais a contraprova de que o nome não aparece cru. A verificação independente do encoder confirmou o comportamento que importa: espaço vira `%20` e não `+`, acento vira UTF-8 percent-encoded, e o par `Uri.UnescapeDataString` + `Assert.Contains` **pegaria** uma regressão para `+`. Esses testes provam o que afirmam. A segunda: **o rodapé de UI-01 e o bloco do detalhe passaram a ser o mesmo componente**, com os dados descendo por cascata de uma única leitura por requisição — o que fecha por estrutura a exigência da RN-67 de "um lugar só, para não divergirem", em vez de deixá-la para a disciplina.

**A ressalva principal foi encontrada na cobertura de teste e corrigida nesta mesma sessão, com prova.** As asserções de CA-31 varriam o HTML inteiro, e o rodapé da própria página emite os mesmos `tel:` e `mailto:` para o mesmo registro de contato: apagar o bloco do detalhe deixaria o teste verde e o CA-31 descumprido. Foram escopadas ao bloco `comMensagemPronta`, e a correção foi verificada por mutação — com o bloco de telefone e e-mail removido, o teste agora falha. É a diferença entre um teste que nomeia um CA e um teste que o prova.

O que sobra de ressalva é de precisão, não de função, e dois itens têm consequência visível em produção: **número de WhatsApp inválido produz botão em destaque apontando para link inválido**, porque nem o campo em UI-10 nem o helper decidem se o que sobrou de dígitos é utilizável; e **o estado `comMensagemPronta` é emitido mesmo sem WhatsApp configurado**, o que faz o marcador de estado mentir sobre a tela. O R-02 do round 1 — status `200` na página de não encontrado — segue persistente, com o caminho já mapeado e fora do alcance desta tarefa.

**Findings por severidade:**

| Severidade | Quantidade |
|------------|------------|
| Bloqueante | 0 |
| Importante | 3 |
| Sugestão   | 4 |
| **Total**  | **7** |

**Cobertura da tarefa:**

| Item | Esperado | Entregue | Status |
|------|----------|----------|--------|
| Regras implementadas (RN) | RN-53, RN-54, RN-55 | as três | ✅ |
| Cenários validados (CA) | CA-05, CA-25, CA-31 | os três, com teste nomeado | ✅ CA-31 após correção de escopo (R-01) |
| Decisões base (ADR) | — *(campo ausente na tarefa)* | ADR-010 respeitada e verificada | ✅ |
| Critérios de aceite da tarefa | 6 | 6 | ✅ |
| Telas e estados (UI) | UI-02 (default, semDescricao, naoEncontrado) | os três, mais `ContatosVitrine` (default, comMensagemPronta) | ⚠️ marcador de estado impreciso (R-02) |
| Testes prometidos | 3 | 11 | ✅ |

---

## Contexto da implementação

### Stack detectada

- **Backend:** .NET 10 / Blazor Web App, renderização estática nesta área (ADR-010)
- **Testes:** xUnit + Testcontainers + `WebApplicationFactory`, banco próprio por caso via `IsolatedDatabase`
- **Fonte:** proposta arquitetural (6.2) e os `*.csproj`

> ⚠️ **`CLAUDE.md` ausente pelo décimo primeiro review consecutivo.** Convenções deste projeto continuam sem onde ser declaradas — entre elas a que este round usa como critério em R-01: asserção de HTML precisa ser escopada ao elemento que a regra exige, porque a vitrine repete o mesmo dado em mais de um lugar da mesma página.

### Escopo do diff

- **Commit:** `4ca5f4e` (código) e `889689a` (registro no plano)
- **No escopo declarado:** `Features/Storefront/ProductPage.razor`, `Features/Storefront/VitrineLayout.razor`, `Features/Storefront/ContatosVitrine.razor` *(novo)*, `Features/Storefront/WhatsAppConversation.cs` *(novo)*
- **Fora do escopo declarado:** `wwwroot/vitrine.css` *(vocabulário do bloco de contato — `.cta`, `.bwa`, `.bline`, extraído do protótipo; mesma natureza do desvio já aceito no round 1)*
- **Testes:** `ProductPageTests.cs` *(5 fatos novos)*, `WhatsAppConversationTests.cs` *(6 fatos, 8 casos)*
- **Sem migration.** Correto — o registro de configuração é de T-31.

### Execução da suíte

`dotnet build` limpo, sem aviso novo. `dotnet test`: **`Com falha: 0, Aprovado: 185, Ignorado: 8, Total: 193`**.

Os 8 ignorados são os de T-31, por ausência de `Supabase__Url` e `Supabase__ServiceKey` — nenhum deles toca esta entrega.

**Nota de ambiente, para quem pegar isto depois:** a suíte exige Docker em execução. Sem ele, 166 casos falham em `DockerUnavailableException` dentro da fixture, e a leitura à primeira vista é de regressão generalizada quando não há nenhuma.

**Verificação por mutação, executada neste round:** com o bloco de telefone e e-mail suprimido de `ContatosVitrine.razor`, `CA_31_telefone_e_email_ficam_disponiveis_alem_do_whatsapp` falha. Antes da correção de R-01, passava.

---

## Findings detalhados

### 🔴 Bloqueantes

Nenhum.

---

### 🟡 Importantes

#### R-01 — As asserções de CA-31 varriam a página inteira, e o rodapé as satisfazia sozinho

- **Eixo:** 4. Cobertura de teste
- **Referência cruzada:** CA-31, RN-54, RN-67
- **Evidência:** `ProductPageTests.cs:222-235` e `:246` na versão de `4ca5f4e`
- **Descrição:** `CA_31_telefone_e_email_ficam_disponiveis_alem_do_whatsapp` afirmava `href="tel:…"` e `href="mailto:…"` contra o HTML completo da resposta. A mesma página renderiza o rodapé da vitrine, que consome **o mesmo registro de contato** e emite os mesmos dois links. Apagar o `<div class="blines">` inteiro do detalhe deixava o teste verde: CA-31 deixaria de ser cumprido em UI-02 e nada acusaria. `Canal_nao_configurado_nao_aparece_no_detalhe` tinha o mesmo defeito, e pior — suas asserções negativas (`DoesNotContain("wa.me")`) mediam a página toda, então mediam o rodapé.

  Este é o tipo de finding que o round 1 elogiou em dois testes desta mesma classe: o do rótulo acima do valor verifica **estrutura**, não presença, e o do card do vizinho verifica **ausência** da descrição. A entrega deste round recaiu no erro que aqueles evitavam.
- **Por quê é Importante:** o teste nomeia um CA e não o prova. Rastreabilidade falsa é pior que ausência de teste, porque o `/leanwork-trace` fecha o elo `CA-31 → teste` e ninguém volta a olhar.
- **Status:** ✅ **Corrigido nesta sessão, com prova.** As duas asserções passaram a operar sobre o trecho delimitado por `ContactBlockIn`, que recorta do marcador `comMensagemPronta` até o início dos relacionados ou do rodapé — os dois únicos elementos que vêm depois do bloco. Verificado por mutação: com o bloco removido, o teste falha.

#### R-02 — `data-estado="comMensagemPronta"` é emitido mesmo quando não há WhatsApp configurado

- **Eixo:** 6. Conformidade de interface / 2. Rastreabilidade
- **Referência cruzada:** UI-02; `ContatosVitrine` (estados `default`, `comMensagemPronta`) na seção de componentes da SPEC-UI
- **Evidência:** `ContatosVitrine.razor:28` — o `data-estado` está no `<div class="cta">`, e o `@if (contact.WhatsApp is { })` que produz a mensagem só começa em `:29`
- **Descrição:** com apenas telefone configurado, o bloco sai marcado como `comMensagemPronta` sem que exista mensagem pronta alguma. O nome do estado é o contrato entre a tela e a SPEC-UI, e é por ele que o teste de interface identifica o que está renderizado — inclusive o helper `ContactBlockIn` introduzido em R-01, que usa esse marcador como âncora.
- **Por quê é Importante:** o marcador de estado é o que torna a tela verificável contra a especificação. Um marcador que afirma mais do que a tela entrega desarma exatamente a verificação que ele existe para permitir, e o caso não é hipotético: `Canal_nao_configurado_nao_aparece_no_detalhe` já exercita essa configuração e encontra o marcador mentindo.
- **Sugestão de correção:** derivar o estado do conteúdo — `comMensagemPronta` quando há WhatsApp, e um nome próprio (ou a ausência do bloco em destaque) quando só existem canais sem mensagem. Vale decidir junto com a SPEC-UI, que hoje declara dois estados para o componente e não prevê "canais sem WhatsApp".

#### R-03 — Número de WhatsApp inválido produz o botão principal apontando para link inválido

- **Eixo:** 3. Aderência ao spec / 5. Qualidade do código
- **Referência cruzada:** RN-55, RN-67; T-31 (UI-10, bloco de contato)
- **Evidência:** `WhatsAppConversation.cs:20,34-35`; `ContatosVitrine.razor:29-34`; `PortalSettings.cs` (`HasContact`); `PortalSettingsService.SaveContactAsync`
- **Descrição:** `UrlFor` reduz o número a dígitos e **não decide nada sobre o que sobrou**. O campo em UI-10 é texto livre com `maxlength`, e a gravação só faz `Trim`. Se o dono digitar "falar comigo", `HasContact` é verdadeiro e o detalhe renderiza o botão em destaque apontando para `https://wa.me/?text=…` — número vazio. Se digitar `(88) 99654-1931` sem o `55`, o link vira `https://wa.me/88996541931`, que o aplicativo abre para informar que o número não existe.

  O comentário do próprio arquivo diz que "limpar aqui vale mais que confiar", e a limpeza acontece; o que falta é a consequência dela.
- **Por quê é Importante:** é o botão principal de uma página pública, e o caminho de falha é o caminho comum — número anotado no formato local, sem código de país. O visitante que clica não volta.
- **Sugestão de correção:** tratar número sem quantidade plausível de dígitos como **canal ausente**, com a mesma semântica de `HasContact`, e validar o campo em UI-10 com mensagem clara sobre o código do país. O risco hoje está distribuído entre T-20 e T-31 sem dono; pede tarefa própria, porque a validação do campo é escopo de T-31 e a decisão de renderizar é desta.

---

### 🔵 Sugestões

#### R-04 — `HasContact` e a renderização usam critérios diferentes de ausência

- **Eixo:** 5. Qualidade do código
- **Evidência:** `PortalSettings.cs` (`HasContact`, por `IsNullOrWhiteSpace`) contra `ContatosVitrine.razor:29,38,46,61,66,71` (por `is { }`)
- **Descrição:** o agregado considera `"   "` como canal inexistente; o componente considera presente. `SaveContactAsync` normaliza em branco para nulo, então a divergência não aparece pela tela — aparece por qualquer escrita que não passe por lá (seed, script, migração futura), e o resultado é `<a href="tel:">` com texto vazio.
- **Sugestão:** um único predicado de "canal presente" em `PortalSettings`, consumido pelas duas pontas.

#### R-05 — `Digits` duplicado entre o componente e o helper

- **Eixo:** 5. Qualidade do código
- **Evidência:** `ContatosVitrine.razor:90-91` e `WhatsAppConversation.cs:35`
- **Descrição:** a mesma implementação, com a mesma intenção, em dois tipos vizinhos do mesmo namespace — o método veio do layout e o helper nasceu com uma cópia no mesmo commit. Não falha hoje; divergem na primeira mudança, e a mudança provável é justamente preservar o `+` no `tel:`, que corrigiria só um lado.
- **Sugestão:** um normalizador único de número, consumido pelos dois.

#### R-06 — `ProductName` decide qual estado renderizar, além de ser dado

- **Eixo:** 5. Qualidade do código
- **Evidência:** `ContatosVitrine.razor:14-26`
- **Descrição:** o parâmetro de conteúdo escolhe entre os três ramos, o que força um `if (ProductName is null)` aninhado dentro do ramo "sem contato" cuja única razão de existir é dizer "não sou o rodapé". Funciona e está comentado, mas um estado novo tem chance real de cair no ramo errado.
- **Sugestão:** parâmetro explícito de variante, ou dois componentes finos sobre um bloco comum, mantendo `ProductName` só como dado.

#### R-07 — Desvios de fidelidade com o protótipo, não registrados

- **Eixo:** 6. Conformidade de interface
- **Evidência:** `ContatosVitrine.razor:33` e `:50` contra `docs/prototype/prototipos/vitrine.html:404-408`
- **Descrição:** o protótipo põe o **número** no `<small>` do botão e "Enviar e-mail" no `<b>` da linha de e-mail; o código põe `sobre <produto>` e o endereço cru. Nenhum dos dois é defeito funcional — são escolhas, e ficam registradas aqui porque a origem de UI-02 é "Protótipo" e desvio silencioso é o que faz a SPEC-UI envelhecer. O endereço cru também estoura a linha em telas estreitas (`.bline` tem `min-width: 150px` e nenhum `overflow-wrap`) para e-mails longos.
- **Sugestão:** alinhar ao protótipo ou aceitar o desvio explicitamente; e um `overflow-wrap: anywhere` na linha, independentemente da decisão.

#### R-08 — Sem canal nenhum, a coluna de decisão termina no preço

- **Eixo:** 6. Conformidade de interface
- **Evidência:** `ContatosVitrine.razor:14-25`; a linha `campo__ajuda` removida de `ProductPage.razor`
- **Descrição:** o commit retirou a frase "os canais de contato aparecem aqui quando configurados" e não pôs substituto. Num portal recém-publicado — e o registro de configuração nasce vazio — a página do produto não oferece nem explica saída alguma; só o rodapé fala, e por baixo dos relacionados. Há teste afirmando esse comportamento, então é decisão e não descuido; o que falta é a decisão estar registrada onde alguém a encontre.
- **Sugestão:** uma linha neutra no lugar do bloco, como já se faz para descrição ausente — é o mesmo padrão da própria tela.

---

## Avaliação por eixo

| # | Eixo | Avaliação |
|---|------|-----------|
| 1 | Aderência ao plano | ✅ Os 6 critérios de aceite entregues. Escopo respeitado, com o mesmo desvio de CSS já aceito no round 1 |
| 2 | Rastreabilidade | ✅ RN-53, RN-54 e RN-55 com nome de teste citando CA-05, CA-25 e CA-31. O elo de CA-31 era nominal antes da correção de R-01, e agora é real |
| 3 | Aderência ao spec | ⚠️ RN-54 e RN-55 cumpridas no que a regra exige. A ressalva é a ausência de decisão sobre número inválido (R-03), que a RN-67 não cobre e nenhuma das duas tarefas assumiu |
| 4 | Cobertura de teste | ⚠️ 3 prometidos, 11 entregues, e os unitários de encoding provam o que afirmam — pegariam a regressão de `%20` para `+`. Contra isso, o defeito de R-01, corrigido e verificado por mutação. Seguem sem caso: configuração parcial no detalhe e número sem dígitos |
| 5 | Qualidade do código | ⚠️ A extração da URL para um helper e a unificação do bloco de contato são as duas decisões certas desta entrega. Ressalvas de precisão em R-04, R-05 e R-06 |
| 6 | Conformidade de interface | ⚠️ Os dois estados do componente entregues e o bloco fiel na estrutura. Marcador de estado imprecisa (R-02), desvios de protótipo não registrados (R-07) e ausência de linha neutra (R-08) |

---

## Verificação da decisão arquitetural

### ADR-010 — Modo de renderização por área

Respeitada e verificada. Nem `ContatosVitrine` nem `VitrineLayout` declaram `@rendermode`, e `A_pagina_de_detalhe_nao_abre_conexao_persistente` continua verde. A cascata de `PortalSettings` do layout para o componente foi examinada especificamente contra o risco de vir vazia em renderização estática: o layout só materializa `@Body` ao renderizar a própria árvore, o que acontece depois de `OnInitializedAsync`, e não há `[StreamRendering]` em nenhum componente da vitrine — então o renderizador espera a quiescência antes de emitir o HTML. Os testes que atravessam o pipeline HTTP real encontram o bloco, o que fecha a questão na prática além da leitura.

### ADR-018 — Aplicação no Render, banco e arquivos no Supabase

Sem interação nova. A leitura de contato é uma consulta ao registro único, agora **uma por requisição** em vez de uma por componente — era a alternativa a fazer o detalhe consultar o mesmo registro duas vezes na mesma requisição, e conta na meta de latência com o plano gratuito.

---

## Nota de segurança

Examinada especificamente, sem achado explorável. O nome do produto passa por `UrlEncoder` e depois pela codificação de atributo do Razor, o que torna impossível escapar do valor do `href`; o esquema é literal em todos os três links (`https://wa.me/`, `tel:`, `mailto:`), então não há caminho para `javascript:`. `tel:` recebe só dígitos. O e-mail vai cru para o `mailto:`, mas o valor tem origem num único usuário autenticado (ADR-006) e o atributo é escapado.

---

## Round anterior

Comparação com `docs/reviews/REVIEW-T-20-2026-09-28.md` (round 1, ⛔ Bloqueado, 1 Bloqueante + 2 Importantes + 2 Sugestões). A numeração recomeçou neste relatório: os `R-XX` da primeira coluna são do round anterior e não têm relação com os de mesmo número deste round.

| Item anterior | Status | Verificação | Comentário |
|---|---|---|---|
| 🔴 R-01 (round 1) — RN-54 e RN-55 não entregues; a tarefa cumpre dois terços do escopo | ✅ **Resolvido** | execução | T-31 criou os dados de contato e o bloco entrou completo. CA-25 e CA-31 têm teste nomeado e verde; os 6 critérios da tarefa estão marcados. O elo de CA-31 era nominal na entrega e virou real em R-01 deste round. As duas correções de plano que o round 1 pediu já estavam aplicadas: `Depende de:` inclui T-31 e o mapa da seção 4 tem `T31 --> T20` |
| 🟡 R-02 (round 1) — Produto despublicado responde `200`, não `404` | ⚠️ **Persistente** | leitura | Inalterado, e corretamente: o round 1 registrou a tentativa e a reversão, e o caminho que resta — mapear `/not-found` para que o `UseStatusCodePagesWithReExecute` produza corpo — é decisão de alcance maior que esta tarefa. O comentário em `ProductPageTests.cs:121-125` preserva o rastro para quem tentar de novo. **Segue pedindo tarefa própria**, e é o único item do round 1 que esta entrega não tocou |
| 🟡 R-03 (round 1) — Cabeçalho e rodapé duplicados da listagem | ✅ **Resolvido**, e além | leitura | O cromo virou `VitrineLayout` em T-31, e este round fechou o que faltava: o bloco de contato do rodapé e o do detalhe passaram a ser **o mesmo componente**, com uma leitura única por requisição. A previsão do round 1 — "duplicado, T-31 vai preencher o mesmo rodapé duas vezes" — não se realizou, porque a extração veio antes |
| 🔵 R-04 (round 1) — O campo de busca do detalhe não preserva nada e não precisa | ✅ **Resolvido por consequência** | leitura | Sem ação pedida. O campo agora é do layout, e a observação perdeu objeto: não há mais dois formulários a manter coerentes |
| 🔵 R-05 (round 1) — `loading` nunca é observado | ✅ **Resolvido** | leitura | Os ramos inalcançáveis saíram em T-31; `grep` por "Carregando" na vitrine não retorna nada |

**Resumo:** 4 resolvidos (R-01, R-03, R-04, R-05), 1 persistente (R-02), nenhum agravado. Dos quatro resolvidos, R-01 tem verificação por execução.

---

## Devolução ao plano

**Sem finding bloqueante — o plano não muda de estado.** T-20 permanece `Concluído`, como a execução registrou, e agora o estado declarado coincide com o validado.

Dois itens pedem entrada no plano como tarefa, e nenhum deles é correção óbvia:

1. **R-03 deste round** — a decisão sobre número de WhatsApp inválido cruza T-20 (renderizar) e T-31 (validar o campo). Sem dono, fica entre as duas.
2. **R-02 do round 1** — mapear `/not-found` resolve o contrato HTTP para o sistema inteiro, não só para esta página. Continua sem tarefa desde 28/09.

R-04, R-05 e R-06 são limpeza de baixo custo e cabem em qualquer toque futuro no `Features/Storefront/`. R-07 e R-08 pedem decisão de interface, não código.

---

## Conclusão

O round 2 fez o que faltava, e fez pelo caminho que o round 1 indicou: T-31 primeiro, T-20 depois — sem inventar dado de cliente em página pública e sem criar a tabela de configuração sob o nome errado. As duas decisões de desenho que ele acrescentou por conta própria são as que valem: a URL do WhatsApp saiu da tela para um helper com um único ponto de substituição, e o bloco de contato deixou de existir em dois lugares. A segunda fecha por estrutura a exigência que a RN-67 escreve em palavras.

O que segura a aprovação plena são três coisas de tamanhos diferentes. A menor é de precisão de estado: `comMensagemPronta` sem mensagem pronta desarma a própria verificação que o marcador existe para permitir. A do meio é de cobertura, e já está fechada — mas vale registrar que ela passou pela execução, pelo build e pela suíte verde sem ser notada, e só apareceu quando alguém perguntou "este teste falharia se o código estivesse errado?". A resposta era não. A maior é a que chega ao visitante: um número anotado no formato local, sem código de país, produz o botão principal da página apontando para um link que não abre conversa — e ninguém, entre T-20 e T-31, assumiu essa decisão.
