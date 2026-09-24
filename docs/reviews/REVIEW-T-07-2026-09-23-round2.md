# Review: T-07 — Autenticação do usuário único

> **Plano de referência:** `docs/plans/PLAN-001-catalogo-virtual.md`
> **PRD de referência:** `docs/prds/PRD-001-catalogo-virtual.md`
> **Arquitetura de referência:** `docs/architecture/proposta-arquitetural.md`
> **SPEC-UI de referência:** `docs/prototype/SPEC-UI-001-catalogo-virtual.md`
> **Reviewer:** Claude (skill `reviewer-leanwork` v1.0)
> **Data:** 2026-09-23
> **Round:** 2
> **Recomendação final:** ⚠️ Aprovado com ressalvas

---

## Sumário executivo

Os dois findings que o round anterior levantou sobre o código foram resolvidos. O estado de envio existe e a trava contra duplo clique está instalada; a ordem do antiforgery foi corrigida. A solução do estado de envio merece registro: a primeira tentativa desabilitava os campos, o que teria quebrado a autenticação em silêncio — campo desabilitado não é enviado no POST —, e a versão final usa `readOnly` nos campos com `disabled` apenas no botão, com teste que trava explicitamente essa distinção.

Três ressalvas novas, todas decorrentes da forma da correção, e nenhuma bloqueante. A mais relevante é que a trava depende de um `<script>` inline dentro do componente, e a navegação aprimorada do Blazor — que a própria ADR-010 adota — não executa scripts em conteúdo substituído. Nos fluxos atuais a tela é alcançada por redirecionamento de documento completo, então funciona; um link interno para ela silenciaria a trava sem que nada acusasse.

Os três `Importante` do round anterior que não eram de código permanecem abertos.

**Findings por severidade:**

| Severidade | Quantidade |
|------------|------------|
| Bloqueante | 0 |
| Importante | 5 |
| Sugestão   | 1 |
| **Total**  | **6** |

**Cobertura da tarefa:**

| Item | Esperado | Entregue | Status |
|------|----------|----------|--------|
| Regras implementadas (RN) | RN-57, RN-58, RN-59, RN-60 | as quatro | ✅ |
| Cenários validados (CA) | CA-26, CA-27 | ambos com teste | ✅ |
| Decisões base (ADR) | ADR-006 | respeitada, exceto troca obrigatória | ⚠️ ver R-02 |
| Critérios de aceite da tarefa | 6 | 6 atendidos | ✅ |
| Telas e estados (UI) | UI-03 (default, erroCredencial, bloqueado, enviando) | os quatro | ⚠️ ver R-01, R-04 |
| Testes prometidos | 3 | 11 | ✅ |

---

## Round anterior

Comparação item a item com `REVIEW-T-07-2026-09-23` (round 1).

| Item do round 1 | Situação | Evidência |
|---|---|---|
| **R-01 (round anterior)** — estado `UI-03.enviando` ausente | ✅ **Resolvido** | `Login.razor:59-73`; testes `UI_03_enviando_a_tela_instala_a_trava_de_envio_duplicado` e `UI_03_enviando_nao_desabilita_campos_que_precisam_ser_enviados`. Ver R-01 deste round quanto ao alcance |
| **R-02 (round anterior)** — `UseAntiforgery` antes de `UseAuthentication` | ✅ **Resolvido** | `Program.cs:61-67` — autenticação, autorização, gate do painel, e antiforgery por último |
| **R-03 (round anterior)** — troca obrigatória de senha sem tarefa no plano | ❌ **Persiste** | `MustChangePassword` segue gravado e nunca lido. Reaparece como R-02 |
| **R-04 (round anterior)** — chaves de Data Protection em disco efêmero | ❌ **Persiste** | Reaparece como R-03 |
| **R-05 (round anterior)** — `UI-03.bloqueado` mostra duração fixa | ❌ **Persiste** | Reaparece como R-04 |
| **R-06 (round anterior)** — parâmetros de bloqueio fixos em código | ❌ **Persiste** | Reaparece como R-06 |

A tensão arquitetural que o round 1 apontou em R-01 — tornar a tela interativa contra a ADR-010, ou escrever script próprio contra a ADR-001 — foi decidida pelo usuário em favor do script inline mínimo, e a decisão está registrada no histórico de execução do plano. Ver R-05 deste round quanto ao lugar desse registro.

---

## Contexto da implementação

### Stack detectada

- **Autenticação:** ASP.NET Core Identity 10 sobre EF Core, cookie próprio
- **Renderização:** estática no servidor para a tela de acesso, por decisão da ADR-010
- **Fonte:** ADR-006, ADR-010; confirmada por inspeção do `.csproj`

### Padrões específicos aplicados

> ⚠️ Segue sem `CLAUDE.md` na raiz.

### Escopo do diff desde o round 1

- **Commit:** `187623c`
- **Arquivos relevantes a T-07:** `src/Catalogo/Features/Account/Login.razor`, `Login.razor.css`, `src/Catalogo/Program.cs`, `tests/Catalogo.Tests/LoginScreenStatesTests.cs`

---

## Findings detalhados

### 🟡 Importantes

#### R-01 — A trava de envio depende de script inline, que a navegação aprimorada não executa

- **Eixo:** 6. Conformidade de interface
- **Referência cruzada:** UI-03 estado `enviando`, ADR-010, RN-60
- **Evidência:** `src/Catalogo/Features/Account/Login.razor:59-73` — o `<script>` vive dentro do componente, depois do formulário
- **Descrição:** a ADR-010 adota explicitamente a navegação aprimorada do Blazor: "a navegação entre filtros e páginas é aprimorada pelo script leve do próprio Blazor, que substitui apenas o trecho alterado do documento". Nesse modo de navegação, elementos `<script>` inseridos no conteúdo substituído **não são executados** — é comportamento conhecido do framework, e o motivo pelo qual scripts de aplicação costumam viver em `App.razor`.

  Os fluxos atuais provavelmente escapam: chegar em `/painel/entrar` acontece por redirecionamento do gate — resposta 302 a uma requisição de documento — ou por digitação direta da URL, e nos dois casos o documento é carregado por inteiro. Um link interno apontando para a tela, porém, seria navegação aprimorada, e a trava simplesmente não se instalaria.
- **Por quê é Importante:** a falha é silenciosa e o teste não a detecta. `UI_03_enviando_a_tela_instala_a_trava_de_envio_duplicado` afirma que o texto do script está no HTML — e estaria, mesmo em um cenário em que ele nunca roda. O teste dá confiança que não corresponde ao que garante.
- **Sugestão de correção:** mover o script para `App.razor`, guardado por verificação da rota, ou ligá-lo ao evento `enhancedload` do Blazor, que existe para exatamente este caso. Vale também confirmar no navegador — a afirmação acima é sobre comportamento documentado do framework, não sobre observação feita nesta sessão.

#### R-02 — Troca obrigatória de senha segue sem tarefa no plano

- **Eixo:** 3. Aderência ao spec
- **Referência cruzada:** ADR-006
- **Evidência:** `Features/Account/OwnerAccount.cs:13`; nenhuma leitura de `MustChangePassword` em todo o repositório
- **Descrição:** persiste do round anterior. A ADR-006 diz que a conta é semeada "com troca de senha obrigatória"; a marca é gravada e ignorada.
- **Por quê é Importante:** ganhou urgência desde o round 1. A senha inicial foi transmitida por canal informal durante a publicação, o que é o cenário normal para uma credencial semeada — e é precisamente o que a obrigatoriedade de troca existe para neutralizar. Hoje essa senha vale indefinidamente.
- **Sugestão de correção:** o problema é do plano, não do código: nenhuma tarefa declara implementar essa parte da ADR-006. Abrir tarefa que force o redirecionamento enquanto a marca estiver ligada, ou revisar a ADR se a obrigatoriedade for abandonada. Enquanto isso não acontece, trocar a senha manualmente é procedimento operacional.

#### R-03 — Chaves de Data Protection seguem em disco efêmero

- **Eixo:** 5. Qualidade do código
- **Referência cruzada:** ADR-018
- **Evidência:** log de produção do deploy: `Storing keys in a directory '/root/.aspnet/DataProtection-Keys' that may not be persisted outside of the container`
- **Descrição:** persiste do round anterior, e foi confirmado em produção — o aviso apareceu no log do deploy bem-sucedido.
- **Por quê é Importante:** cada publicação invalida cookie de autenticação e token antiforgery. Com a correção do antiforgery deste round, a dependência entre token e sessão ficou mais forte, o que torna o efeito de uma publicação no meio de um envio mais visível, não menos.
- **Sugestão de correção:** persistir as chaves fora do container — o Supabase já está no desenho. É material de T-28, desde que T-28 passe a declarar isso; hoje não declara.

#### R-04 — `UI-03.bloqueado` segue mostrando duração fixa e some ao recarregar

- **Eixo:** 6. Conformidade de interface
- **Referência cruzada:** UI-03, RN-60
- **Evidência:** `Login.razor:80` — `lockedOutUntilMinutes` recebe a duração total configurada, não o tempo restante
- **Descrição:** persiste do round anterior. A SPEC-UI pede "aviso com tempo restante"; a tela informa sempre cinco minutos, e recarregar durante o bloqueio devolve a tela padrão com campos habilitados.
- **Por quê é Importante:** com a trava de envio agora instalada, o usuário bloqueado encontra uma tela que parece funcional — campos editáveis, botão ativo — e cuja única resposta é a mesma recusa. A informação que o ajudaria a esperar continua indisponível.
- **Sugestão de correção:** ler `LockoutEnd` e calcular o restante. Atenção ao fazê-lo: consultar bloqueio a partir do usuário digitado permite descobrir se uma conta existe, o que conflita com a decisão de mensagem genérica. Verificar apenas após uma tentativa, como hoje, evita isso.

#### R-05 — Exceção às ADR-001 e ADR-010 registrada apenas no histórico do plano

- **Eixo:** 3. Aderência ao spec
- **Referência cruzada:** ADR-001, ADR-010
- **Evidência:** `Login.razor:32-36`, comentário explicando a decisão; histórico de execução do plano; **nenhuma menção nas ADRs**
- **Descrição:** a ADR-001 afirma que não há "cadeia de build de JavaScript no pipeline" e a ADR-010 celebra "nenhuma linha de JavaScript próprio" como consequência positiva. O repositório agora tem cinco linhas de JavaScript próprio. A decisão foi consciente, tomada pelo usuário entre duas alternativas apresentadas, e está comentada no código e narrada no plano.
- **Por quê é Importante:** a proposta arquitetural continua afirmando algo que deixou de ser verdade. Quem ler a ADR-010 para decidir a próxima tela vai concluir que script próprio está fora de questão — e ou repete a discussão do zero, ou toma a decisão errada por achar que a porta está fechada.
- **Sugestão de correção:** uma nota na ADR-010 registrando a exceção, seu motivo e seu limite — script inline, sem build, sem arquivo, apenas onde o modo de renderização não entrega o comportamento. O plano registra o "o quê"; a arquitetura precisa registrar o "até onde".

### 🟢 Sugestões

#### R-06 — Parâmetros de bloqueio seguem fixos em código

- **Eixo:** 5. Qualidade do código
- **Evidência:** `Features/Account/PanelAuthentication.cs:18-20`
- **Descrição:** persiste do round anterior. Cinco tentativas e cinco minutos, sem respaldo em RN — a escolha é legítima do implementador.
- **Sugestão:** mover para configuração, mantendo os valores atuais como padrão.

---

## Cobertura por UI (Telas)

| Estado | Round 1 | Round 2 | Observação |
|---|---|---|---|
| `UI-03.default` | ✅ | ✅ | — |
| `UI-03.erroCredencial` | ✅ | ✅ | mensagem genérica preservada |
| `UI-03.bloqueado` | ⚠️ | ⚠️ | tempo restante e persistência, ver R-04 |
| `UI-03.enviando` | ⛔ | ✅ | entregue; alcance limitado, ver R-01 |

---

## Nota sobre a qualidade da correção

Vale registrar o que o teste `UI_03_enviando_nao_desabilita_campos_que_precisam_ser_enviados` protege, porque não é óbvio: um controle desabilitado não é serializado no envio do formulário. Desabilitar os campos para impedir edição durante o envio — que é a leitura literal da SPEC-UI, "campos travados" — produziria um POST sem usuário e sem senha, e a autenticação falharia com a mesma mensagem genérica de credencial inválida. O defeito seria difícil de diagnosticar justamente porque a mensagem de erro é, por decisão de segurança, pouco informativa.

O teste que afirma a ausência de `field.disabled` é o tipo de proteção que só faz sentido para quem conhece a armadilha — manter o comentário que o acompanha é parte do valor dele.
