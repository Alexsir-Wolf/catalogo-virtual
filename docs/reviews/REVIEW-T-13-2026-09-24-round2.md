# Review: T-13 — Foto do produto

> **Plano de referência:** `docs/plans/PLAN-001-catalogo-virtual.md`
> **PRD de referência:** `docs/prds/PRD-001-catalogo-virtual.md`
> **Arquitetura de referência:** `docs/architecture/proposta-arquitetural.md`
> **SPEC-UI de referência:** `docs/prototype/SPEC-UI-001-catalogo-virtual.md`
> **Round anterior:** `docs/reviews/REVIEW-T-13-2026-09-24.md` (round 1, ⛔ Bloqueado)
> **Reviewer:** Claude (skill `reviewer-leanwork` v1.0)
> **Data:** 2026-09-24
> **Round:** 2
> **Recomendação final:** ⚠️ Aprovado com ressalvas

---

## Sumário executivo

O bloqueio do round 1 foi endereçado no lugar certo e com desenho melhor do que o sugerido. A orquestração do envio saiu do `@code` do componente e virou `ProductPhotoUpload`, um serviço que nunca lança: a falha do armazenamento, a falha do banco, a interrupção do transporte e o produto inexistente todos voltam como `PhotoUploadOutcome.Failed(mensagem)`. O `ProductForm` reduziu `UploadAsync` a `uploadError = outcome.Error; photo = outcome.Photo ?? photo`, e a preservação da foto anterior deixou de ser um efeito colateral do fluxo para ser uma linha que diz o que faz. A recusa por tamanho passou a ser decidida por `file.Size` antes de qualquer leitura, o que libera o `catch (IOException)` para significar transporte interrompido — exatamente a correção que o R-01 de `REVIEW-T-13-2026-09-24` pedia para o R-06 do mesmo relatório. Três dos dez findings anteriores estão resolvidos; os sete restantes persistem, dois deles agravados.

Nada disso está provado nesta máquina. `dotnet build` compila limpo (um aviso `BL0008` preexistente em `Login.razor`), mas `dotnet test` devolve **74 falhas, 8 aprovados, 2 ignorados, total 84** — o Docker Desktop não está em execução e a `PostgresFixture` sobe container. Os cinco testes novos falham no construtor do *fixture* em **1 ms cada**, sem alcançar o corpo. A avaliação abaixo é por leitura de código, e a cobertura efetiva do R-01 anterior segue **não verificável** (R-05 deste round). O executor exercitou dois dos três ramos em um *harness* descartável fora do repositório; isso é evidência de menor força que a suíte e o relatório diz por quê.

As ressalvas novas se concentram em três lugares. O filtro `when (exception is not OperationCanceledException)` exclui justamente o *timeout* do `HttpClient`, que é a falha esperada na ADR-018 — e o único motivo de isso não aparecer é que o `catch` do componente, declarado como "último limite", passou a ser o limite de verdade nesse caminho. O mesmo `catch` largo converte defeito de programação em "tente de novo em instantes", risco que o próprio desenho torna desnecessário assumir. E o `ProductUnavailableMessage`, que resolveu o R-02 anterior, deixou um resíduo: a tela anuncia que o produto não existe mais e continua oferecendo `Salvar`, que recria o produto silenciosamente com um id novo.

**Findings por severidade:**

| Severidade | Quantidade |
|------------|------------|
| Bloqueante | 0 |
| Importante | 7 |
| Sugestão   | 3 |
| **Total**  | **10** |

**Cobertura da tarefa:**

| Item | Esperado | Entregue | Status |
|------|----------|----------|--------|
| Regras implementadas (RN) | RN-09 | RN-09, sem alteração neste round | ✅ |
| Cenários validados (CA) | CA-07 | dois testes, inalterados por este round | ✅ no código, ⛔ não reexecutados (R-05) |
| Decisões base (ADR) | ADR-005 | respeitada; ADR-010 e ADR-018 agora materializadas no tratamento de erro | ✅ |
| Critérios de aceite da tarefa | 5 | 5 atendidos no código | ✅ com ressalva de verificação |
| Telas e estados (UI) | UI-05 (enviandoFoto, erroUpload) | `.erroUpload` completo; `.enviandoFoto` ainda sem progresso | ⚠️ R-07 do round anterior persiste |
| Testes prometidos | 2 (round 1) + correções do round 1 | 5 novos, nenhum executável nesta máquina | ⚠️ R-04, R-05 |

---

## Contexto da implementação

### Stack detectada

- **Backend:** .NET 10 / Blazor Web App, `InteractiveServer` no painel
- **Dados:** EF Core + PostgreSQL gerenciado no Supabase
- **Imagem:** SkiaSharp 4.152.1; armazenamento de objeto do Supabase via HTTP
- **Testes:** xUnit 2.9.3 + Testcontainers.PostgreSql 4.15.0 + `Xunit.SkippableFact` + `WebApplicationFactory`
- **Fonte:** proposta arquitetural (ADR-001, ADR-004, ADR-005, ADR-010, ADR-018) e inspeção de `src/Catalogo/Catalogo.csproj` e `tests/Catalogo.Tests/Catalogo.Tests.csproj`

### Padrões específicos aplicados (lidos do projeto)

> ⚠️ **Não existe `CLAUDE.md` na raiz do repositório** (nem diretório `.claude/`). É o **sétimo review consecutivo** com a mesma lacuna. Padrões específicos da stack não estão declarados em nenhum artefato de contexto, então foram aplicados os critérios universais de qualidade mais as convenções observadas no código — *feature folders* em `Features/`, construtor primário nos serviços, `IDbContextFactory` por operação, nomes de teste em português com prefixo do ID quando há CA ou RN, e comentários citando RN/ADR no ponto não-óbvio. A lacuna deixou de ser barata neste round: o desenho de tratamento de erro escolhido aqui (serviço que não lança + limite em cada manipulador de interação, em vez de `ErrorBoundary`) é uma **convenção nova de alcance geral** e não tem onde ser registrada. Ver R-07.

### Escopo do diff

Mudanças **no working tree, não commitadas** — avaliadas por `git status` e `git diff`.

- **Arquivos de produção:** `src/Catalogo/Features/Products/ProductPhotoUpload.cs` *(novo, 125 linhas)*, `src/Catalogo/Features/Products/ProductForm.razor` *(+17 / −20)*, `src/Catalogo/Program.cs` *(+1)*
- **Arquivos de teste:** `tests/Catalogo.Tests/ProductPhotoUploadFailureTests.cs` *(novo, 177 linhas)*, `tests/Catalogo.Tests/ProductScreenTests.cs` *(+56 / −2)*
- **Documentação:** `docs/plans/PLAN-001-catalogo-virtual.md` *(Status de T-13 volta a `Bloqueado`, linha de histórico dos reviews de round 1)* — não avaliado como código
- **Aderência ao escopo declarado:** `Features/Products/` *(alteração)*. O arquivo novo nasce dentro dessa pasta e `Features/Media/` continua intocada, o que é o comportamento correto para uma tarefa que **consome** T-08. O registro em `Program.cs` é consequência mecânica do serviço novo, não expansão de escopo

### Verificação da suíte

`dotnet build --no-incremental`: **compilação com êxito, 1 aviso, 0 erros** — o aviso é o `BL0008` preexistente em `Features/Account/Login.razor:148`, sem relação com esta entrega.

`dotnet test`: **74 com falha, 8 aprovados, 2 ignorados, total 84**. Todas as 74 falhas são a mesma: `DockerUnavailableException — Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'`, lançada em `PostgresFixture..ctor`. O total subiu de 79 (round 1) para 84, o que confere com os cinco testes acrescentados.

Execução filtrada nos testes novos, para não deixar dúvida sobre o que foi ou não exercido:

```
Com falha Catalogo.Tests.ProductScreenTests.UI_05_novo_explica_que_a_foto_exige_o_produto_salvo [1 ms]
Com falha Catalogo.Tests.ProductScreenTests.UI_05_edicao_oferece_o_envio_da_foto_e_declara_a_substituicao [1 ms]
Com falha Catalogo.Tests.ProductPhotoUploadFailureTests.Falha_do_armazenamento_vira_mensagem_e_preserva_a_foto_anterior [1 ms]
Com falha Catalogo.Tests.ProductPhotoUploadFailureTests.Produto_inexistente_nao_apaga_a_foto_da_tela_sem_dizer_nada [1 ms]
Com falha Catalogo.Tests.ProductPhotoUploadFailureTests.Arquivo_acima_do_limite_e_recusado_por_tamanho_antes_de_qualquer_gravacao [1 ms]
```

Um milissegundo é a assinatura de falha na construção do *fixture*: nenhum dos cinco chegou ao corpo. **Nada neste relatório afirma cobertura verificada por execução.**

---

## Findings detalhados

### 🔴 Bloqueantes

Nenhum. O R-01 de `REVIEW-T-13-2026-09-24` — falha do armazenamento derrubando o circuito e levando o formulário digitado — está fechado no código. A análise que sustenta esse fechamento está na seção "Julgamento sobre as decisões do executor", item 1, e a ressalva de verificação está em R-05.

### 🟡 Importantes

#### R-01 — O filtro `when (exception is not OperationCanceledException)` exclui justamente o *timeout* do `HttpClient`

- **Eixo:** 5. Qualidade do código
- **Referência cruzada:** ADR-018, UI-05 (`.erroUpload`), R-01 de `REVIEW-T-13-2026-09-24`
- **Evidência:** `src/Catalogo/Features/Products/ProductPhotoUpload.cs:64` e `:85`; `src/Catalogo/Features/Products/ProductForm.razor:237`; `src/Catalogo/Features/Media/SupabaseObjectStorage.cs:35-36`

  ```csharp
  catch (Exception exception) when (exception is not OperationCanceledException)
  ```

- **Descrição:** o filtro existe para deixar passar o cancelamento legítimo. Só que `HttpClient.SendAsync` sinaliza **estouro do próprio timeout** (100 s por padrão) lançando `TaskCanceledException`, que deriva de `OperationCanceledException` — com `TimeoutException` no `InnerException`, não como cancelamento de ninguém. Em uma arquitetura em que a aplicação dorme, o banco do Supabase é pausado por inatividade e a rede está entre os dois (ADR-018), o *timeout* não é um caso de borda: é um dos modos de falha mais prováveis do envio. E é o único que o filtro deixa escapar.

  A segunda metade do problema é que o cancelamento que o filtro tenta preservar **não existe neste caminho**: `ProductForm.razor:237` chama `Upload.StoreAsync(Id!.Value, content, file.Size)` sem token algum, então `cancellationToken` é sempre `default` e nunca é cancelado. O filtro, na prática, só faz uma coisa: liberar o *timeout*.
- **Por quê é Importante:** o serviço foi construído para nunca lançar — é essa a propriedade que fecha o R-01 anterior — e no caminho mais provável ele lança. O sintoma não chega ao dono porque o `catch (Exception)` do componente absorve, o que significa que o "último limite" do `ProductForm` passou a ser o limite de verdade nesse ramo, e o registro no log troca "Falha ao gravar a foto do produto {ProductId} no armazenamento" por "Falha inesperada" — perdendo a informação de que a causa era rede, que é precisamente o que se quer saber em produção.
- **Sugestão de correção:** filtrar por tipo em vez de por negação — `HttpRequestException`, `TimeoutException`, `DbUpdateException` e `NpgsqlException` cobrem o que a ADR-018 trata como esperado; para o *timeout*, `TaskCanceledException` com `InnerException is TimeoutException`. Se a preferência for manter o `catch` largo, a condição correta é `when (!cancellationToken.IsCancellationRequested)`, que distingue cancelamento de *timeout* em vez de confundir os dois. Ver também R-02: as duas correções são a mesma.

#### R-02 — O `catch` largo converte defeito de programação em mensagem amigável e convite a repetir

- **Eixo:** 5. Qualidade do código
- **Referência cruzada:** ADR-005, ADR-018
- **Evidência:** `src/Catalogo/Features/Products/ProductPhotoUpload.cs:64-72` e `:85-94`; `src/Catalogo/Features/Products/ProductForm.razor:245-251`
- **Descrição:** o `catch (Exception)` em volta de `photoService.StoreAsync` engloba todo o pipeline de imagem: `ImageValidation.Validate`, `ImageProcessor.Process` e a interop nativa do SkiaSharp. Uma `NullReferenceException` em `NameOf` porque uma derivada nova não foi registrada em `DerivativeSpecifications`, uma `ArgumentException` de dimensão inválida, uma `InvalidOperationException` do `Single` — todas viram `StorageUnavailableMessage`: *"Não foi possível concluir o envio da foto agora. Tente de novo em instantes."* O dono repete o envio, falha de novo de forma idêntica, e o sistema continua afirmando que o problema é passageiro. O mesmo vale para o `catch` em volta de `AttachPhotoAsync`, que engloba o mapeamento do EF, não só a ida ao banco.

  O `LogError` mitiga — mas mitiga para quem lê o log, e o log em plano gratuito é volátil, que é a mesma observação que o R-03 de `REVIEW-T-13-2026-09-24` já fez sobre os nomes das órfãs e que segue sem resposta.

  O ponto que torna isto barato de corrigir: **o componente agora tem o próprio `catch (Exception)`** (`ProductForm.razor:245`), que registra e mostra mensagem sem derrubar o circuito. Ou seja, restringir os `catch` do serviço aos tipos de falha de infraestrutura **não reabre o R-01** — o defeito de programação continua não chegando ao circuito, só deixa de se disfarçar de indisponibilidade. O `catch` largo no serviço não compra proteção nenhuma que o componente já não dê; compra apenas imprecisão.
- **Sugestão de correção:** restringir os dois `catch` do serviço aos tipos listados em R-01 e deixar o restante subir para o limite do componente, cuja mensagem pode continuar sendo genérica — mas cujo log já diz "falha inesperada", que é a verdade.

#### R-03 — Falha de banco depois da gravação recebe a mesma mensagem da falha de armazenamento, e cada repetição acrescenta cinco órfãs

- **Eixo:** 5. Qualidade do código (com efeito em 3. Aderência ao spec)
- **Referência cruzada:** RN-20/CA-08 (T-16), R-03 e R-04 de `REVIEW-T-13-2026-09-24`, R-01 de `REVIEW-T-08-2026-09-23`
- **Evidência:** `src/Catalogo/Features/Products/ProductPhotoUpload.cs:71` e `:93` — os dois ramos devolvem `StorageUnavailableMessage`
- **Descrição:** os dois estados que compartilham a mensagem têm consequências opostas no armazenamento. No ramo de `photoService.StoreAsync`, a falha aconteceu **durante** a sequência de gravações: nada ou parte subiu. No ramo de `AttachPhotoAsync`, as **cinco** derivadas já estão no bucket, íntegras, e o que falhou foi apenas a linha que as nomeia. A mensagem em ambos convida a repetir — e a repetição, no segundo caso, grava outras cinco, com raiz imutável nova, a cada tentativa. Se o banco estiver pausado (o cenário que a própria ADR-018 lista como negativa conhecida), três tentativas do dono deixam quinze objetos cujos nomes existem apenas em linhas de `LogError`.

  O `LogError` foi escrito com cuidado — inclui `productId` e a raiz `{Root}` — e é a coisa certa a fazer dado o desenho atual. O problema é o desenho: o R-03 do round anterior pediu que o rastro das órfãs saísse do log para um lugar onde o sistema possa agir, e este ramo novo cria um **terceiro** caminho de geração de órfã (os outros dois são a substituição, do round anterior, e a falha parcial da sequência de cinco, que é o R-01 de `REVIEW-T-08-2026-09-23`, ainda aberto).
- **Por quê é Importante:** não é perda de dado do dono e não quebra a tela, mas é dívida que cresce por tentativa em vez de por evento, e empurra mais adiante a cláusula da RN-20 que T-16 vai ter de cumprir ("a foto e todas as derivadas são removidas").
- **Sugestão de correção:** mensagem própria para o ramo do banco — a foto foi recebida, a associação não; repetir é seguro mas não é gratuito. E resolver o rastro uma vez, como o R-03 anterior sugeriu: persistir a raiz imutável em uma tabela de objetos a recolher fecha os três caminhos com o mesmo mecanismo. Se ficar para depois, abrir tarefa no plano em vez de acumular três reviews apontando o mesmo lugar.

#### R-04 — O terceiro ramo — falha de banco no `AttachPhotoAsync` — não tem teste algum, e é o de justificativa mais frágil

- **Eixo:** 4. Cobertura de teste
- **Referência cruzada:** R-01 de `REVIEW-T-13-2026-09-24`
- **Evidência:** `tests/Catalogo.Tests/ProductPhotoUploadFailureTests.cs` tem três `[Fact]`; nenhum injeta `ProductMaintenance` ou `IDbContextFactory` que falhe. O `FakeObjectStorage` só cobre `IObjectStorage`
- **Descrição:** dos quatro ramos de erro que `ProductPhotoUpload` introduziu, dois têm teste (falha do armazenamento, produto inexistente), um tem teste da guarda que o antecede (tamanho antes da leitura) e **dois não têm nenhum**: o `catch (IOException)` que agora significa transporte interrompido — ou seja, a metade nova do R-06 anterior — e o `catch` em volta de `AttachPhotoAsync`. O segundo é justamente o que o executor admitiu estar além do escopo declarado, porque protege falha de banco e não só de armazenamento. Escopo admitido e não coberto é a pior combinação das duas: amplia a superfície e não prova o que ampliou.

  A razão é estrutural e vale registrar: `ProductPhotoUpload` recebe `ProductMaintenance` **concreto e `sealed`**, então não há como fazê-lo falhar num teste. `IObjectStorage` é interface e por isso o outro ramo foi provável. Ver R-08.
- **Sugestão de correção:** uma costura para a associação — interface mínima com `AttachPhotoAsync`, ou um delegate no construtor — torna os dois ramos testáveis e, de quebra, dispensa o container em dois dos três testes existentes.

#### R-05 — Nenhum dos cinco testes novos foi executado; a cobertura do R-01 anterior segue não verificável

- **Eixo:** 4. Cobertura de teste
- **Referência cruzada:** R-01 e R-05 de `REVIEW-T-13-2026-09-24`
- **Evidência:** `dotnet test` nesta máquina — 74 com falha, 8 aprovados, 2 ignorados, total 84; os cinco novos falham em `PostgresFixture..ctor` com `DockerUnavailableException`, em 1 ms cada. `tests/Catalogo.Tests/PostgresFixture.cs:14`
- **Descrição:** os cinco testes acrescentados são, no papel, exatamente os que o round 1 pediu: três para o caminho de falha do envio, dois estáticos na área de foto da UI-05. Nenhum deles prova nada hoje. Os três de `ProductPhotoUploadFailureTests` estão em `[Collection(PostgresCollection.Name)]`, e os dois de `ProductScreenTests` também dependem do *fixture*. O Docker Desktop está fora do ar, e o construtor do *fixture* falha antes de qualquer corpo de teste rodar.

  Vale ser específico sobre o que isso significa para o R-01 anterior, que era o bloqueante: a correção é **verificável por leitura** — o serviço devolve `PhotoUploadOutcome` em todos os caminhos e o componente tem limite próprio — mas a **regressão não está protegida**. Se alguém restringir o `catch` de volta a `IOException` amanhã, nenhuma execução acusa nesta máquina.

  Sobre o *harness* descartável fora do repositório, que exercitou dois dos três ramos: é evidência real e é melhor que nenhuma, mas é de força menor que a suíte por quatro motivos objetivos, não por formalidade. Ele não está versionado, então ninguém pode reexecutá-lo nem auditar o que ele de fato afirmou. Ele não roda em nenhum pipeline, então não pega a regressão de amanhã — que é a principal função de um teste de caminho de erro. Ele foi escrito pelo mesmo agente que escreveu o código, no mesmo momento e com as mesmas premissas, o que é exatamente a condição em que um teste concorda com o código por construção. E ele exercitou o código fora da composição real — sem `Program.cs`, sem o `HttpClient` tipado, sem o `IDbContextFactory` do projeto —, então não diz nada sobre o registro `Scoped` novo nem sobre a injeção no componente.
- **Por quê é Importante:** a recomendação deste relatório é "aprovado com ressalvas" e **esta é a ressalva principal**. A diferença entre ela e um bloqueio é que a causa é de ambiente, não de código.
- **Sugestão de correção:** subir o Docker Desktop e rodar `dotnet test` uma vez, registrando o resultado no histórico do plano com o número real — é a condição para a ressalva cair. Em paralelo, a costura do R-08 tiraria os três testes de caminho de falha da dependência de container, o que é o que faz a cobertura do R-01 deixar de depender de uma máquina estar de pé.

#### R-06 — A tela avisa que o produto não existe mais e continua oferecendo `Salvar`, que o recria com um id novo em silêncio

- **Eixo:** 6. Conformidade de interface (com efeito em 5. Qualidade do código)
- **Referência cruzada:** R-02 de `REVIEW-T-13-2026-09-24`, RN-09, UI-05 (`.erroUpload`), R-01 de `REVIEW-T-12-2026-09-24`
- **Evidência:** `src/Catalogo/Features/Products/ProductPhotoUpload.cs:96-104`; `src/Catalogo/Features/Products/ProductForm.razor:184-189`, `:193-196`, `:281-299`; `src/Catalogo/Features/Products/ProductMaintenance.cs:100-108`
- **Descrição:** o R-02 do round anterior está resolvido no que ele acusava — a foto não desaparece mais em silêncio, e a mensagem existe. O desenho escolhido foi "preserva e avisa" em vez do `notFound` que o round 1 oferecia como alternativa, e o resíduo está no que o `notFound` teria fechado de graça.

  Depois de `ProductUnavailableMessage`, a tela segue inteira: os campos preenchidos, o `InputFile` habilitado e o botão `Salvar` ativo. Se o dono clicar em `Salvar` — que é a reação natural a "a foto não foi trocada" —, `Maintenance.SaveAsync(draft)` recebe um `draft.Id` que não existe mais no banco, cai no ramo `product is null` e **cria um produto novo**, com id novo, posição nova e nascimento em Rascunho. O componente não navega, porque `Id` não é `null` (`:295`), então a URL continua apontando para o id morto, a tela exibe "Produto salvo." e o dono fica com um produto fantasma que ele não encontra e que não tem foto — as cinco derivadas que acabaram de subir não foram associadas a ele.

  Há ainda a concatenação do `:187`, que soma as duas frases e produz *"Este produto não está mais disponível, então a foto não foi trocada. Ele pode ter sido excluído. A foto anterior foi mantida."* — a segunda metade contradiz a primeira, porque não há mais produto onde a foto anterior estaria.
- **Por quê é Importante:** a causa raiz do ressuscitamento está em `ProductMaintenance.SaveAsync` e é de T-12 (relacionada ao R-01 de `REVIEW-T-12-2026-09-24`, que trata da validação frouxa do mesmo método). O que T-13 fez foi tornar o caminho **alcançável e anunciado**: antes do round 2, nada na tela dizia ao dono que o produto tinha sumido; agora a tela diz, e a ação que ela continua oferecendo é a que produz o dano. Não é perda de dado digitado, o que o mantém fora do bloqueante.
- **Sugestão de correção:** ao receber `ProductUnavailableMessage`, levar a tela ao estado `notFound`, que já existe em `:20-28` e já diz a frase certa. Se a preferência for manter o formulário visível para o dono copiar o que digitou, então desabilitar `Salvar` no mesmo gesto e ajustar a concatenação do `:187` para não prometer a foto anterior. O ressuscitamento em `SaveAsync` merece finding próprio em T-12 ou tarefa no plano — ele não deveria depender deste caminho para ser corrigido.

#### R-07 — Descartar o `ErrorBoundary` fecha o R-01 no caminho da foto e deixa `Salvar` e o carregamento da mesma tela sem rede, sem convenção escrita em lugar nenhum

- **Eixo:** 5. Qualidade do código (com efeito em 2. Rastreabilidade)
- **Referência cruzada:** ADR-010, ADR-018, R-01 de `REVIEW-T-13-2026-09-24`, R-01 de `REVIEW-T-09-2026-09-23`, R-01 de `REVIEW-T-11-2026-09-23`, R-01 de `REVIEW-T-12-2026-09-24`
- **Evidência:** `grep -rn "ErrorBoundary" src/` não encontra nenhuma ocorrência; `src/Catalogo/Features/Products/ProductForm.razor:281-299` (`SaveAsync` sem `try`), `:263-279` (`OnParametersSetAsync` sem `try`)
- **Descrição:** o descarte do `ErrorBoundary` está tecnicamente correto e a justificativa se sustenta — ele substitui o conteúdo do componente pelo bloco de erro, e voltar exige `Recover()` com nova renderização, o que perde o estado digitado. Era isso que tornava o R-01 bloqueante, e tratar no manipulador de interação é de fato o lugar certo. O finding não é sobre a decisão; é sobre o seu corolário.

  A decisão implica que **todo manipulador de interação precisa do próprio limite**. No mesmo arquivo, `SaveAsync` não tem nenhum: `Maintenance.SaveAsync` atravessa a rede até o Supabase (ADR-018) e, segundo o R-01 de `REVIEW-T-12-2026-09-24`, já sobe `DbUpdateException` crua quando a validação da aplicação é mais frouxa que a do banco. Essa exceção escapa do manipulador, encerra o circuito e leva o formulário — a descrição literal do R-01 do round anterior, agora acessível pelo botão ao lado. `OnParametersSetAsync` tem o mesmo problema no ciclo de vida, onde nem um `catch` no manipulador alcança.

  A correção de `SaveAsync` é de T-12, não de T-13, e não seria justo exigi-la aqui. O que cabe a este round é registrar que a entrega estabeleceu uma **convenção nova de alcance geral** — serviço que não lança, limite em cada manipulador, `ErrorBoundary` deliberadamente ausente — e que essa convenção não está escrita em nenhum artefato. O repositório não tem `CLAUDE.md`, e três reviews seguidos (T-09, T-11, T-12) já apontaram a mesma classe de problema em três lugares diferentes. Sem registro, a próxima tarefa que tocar um manipulador vai reinventar a decisão ou esquecê-la.
- **Sugestão de correção:** rodar `/leanwork-context raiz` e registrar a convenção de tratamento de erro como padrão do projeto, com a justificativa do descarte do `ErrorBoundary` — ela é boa e vale mais escrita que na cabeça de quem implementou. E abrir, em T-12 ou como tarefa própria, a aplicação do mesmo limite a `SaveAsync` e ao carregamento.

### 🟢 Sugestões

#### R-08 — `ProductPhotoUpload` depende de `ProductMaintenance` concreto e `sealed`, e isso obriga container para testar caminho de erro

- **Eixo:** 5. Qualidade do código
- **Evidência:** `src/Catalogo/Features/Products/ProductPhotoUpload.cs:21-24`; `src/Catalogo/Features/Products/ProductMaintenance.cs:45` (`public sealed class`); `tests/Catalogo.Tests/ProductPhotoUploadFailureTests.cs:17`
- **Descrição:** a extração criou o *seam* que faltava para o armazenamento e parou ali. Os três testes de falha precisam de PostgreSQL só porque a associação não tem costura — e um deles, `Arquivo_acima_do_limite_e_recusado_por_tamanho_antes_de_qualquer_gravacao`, **retorna antes de tocar o banco** e ainda assim está preso ao container pela `[Collection]`. É o teste mais rápido e mais independente da suíte, pagando o preço do mais lento.
- **Sugestão:** uma interface mínima para a associação (ou um delegate) resolve o R-04, tira dois dos três testes da dependência de Docker e é o que faria a cobertura do antigo R-01 sobreviver a uma máquina sem container.

#### R-09 — A lista dos cinco nomes da foto ganhou um quarto lugar

- **Eixo:** 5. Qualidade do código
- **Evidência:** `tests/Catalogo.Tests/ProductPhotoUploadFailureTests.cs:95-103` (`PhotoNamed`), somado a `ProductMaintenance.ReplacedNames`, `ProductPhotoTests.Names` e à marcação de `ProductForm.razor:162-167`
- **Descrição:** o R-10 de `REVIEW-T-13-2026-09-24` apontou a mesma enumeração em três lugares. Este round acrescentou o quarto, no arquivo de teste novo. A sugestão anterior — um membro em `ProductPhoto` que devolva os nomes — segue valendo e agora economiza mais.

#### R-10 — `PhotoUploadOutcome.Succeeded` nasceu sem uso

- **Eixo:** 5. Qualidade do código
- **Evidência:** `src/Catalogo/Features/Products/ProductPhotoUpload.cs:7`; `grep` por `.Succeeded` em `Features/Products` e `Features/Media` encontra apenas `ProductOutcome.Succeeded` (`ProductForm.razor:288`) e `PhotoUploadResult.Succeeded` (`ProductPhotoUpload.cs:74`)
- **Descrição:** o componente lê `outcome.Error` e `outcome.Photo` diretamente, e os testes comparam mensagem. A propriedade é código morto dentro do diff — pequeno, mas é código morto que parece contrato.
- **Sugestão:** usar no componente (`if (!outcome.Succeeded)` lê melhor que `uploadError = outcome.Error`) ou remover.

---

## Julgamento sobre as decisões do executor

Esta seção existe porque a entrega veio com justificativas explícitas. Cada uma foi verificada no código, não aceita pelo argumento.

### 1. O descarte do `ErrorBoundary` fecha o R-01?

**Sim, no caminho da foto.** O argumento do executor está correto no mecanismo: `ErrorBoundary` captura a exceção mas troca o conteúdo do componente pelo bloco de erro, e a volta exige `Recover()` com nova renderização — o estado digitado não sobrevive, que era exatamente o requisito que tornava o R-01 bloqueante. Tratar no manipulador de interação preserva `draft` porque o componente nunca é substituído.

**Caminhos de escape do manipulador, verificados um a um.** `UploadAsync` tem `catch (Exception)` como limite externo, e tudo que pode lançar está dentro dele: `change.File` (que lança `InvalidOperationException` com múltiplos arquivos), `file.OpenReadStream` (que lança em `maxAllowedSize` excedido), a chamada ao serviço, e o `await using` do stream — cuja disposição ocorre no fim do bloco `try`, portanto coberta. O `finally` só atribui `uploading = false` e não lança. **Não sobrou caminho de escape no envio.**

Três caminhos de escape **fora** do envio continuam abertos na mesma tela, e nenhum é alcançado pela correção: o manipulador `SaveAsync` (R-07), o ciclo de vida `OnParametersSetAsync` (R-07, e este não seria alcançado nem por um `catch` no manipulador), e a fase de renderização — `PublicUrl(photo.CardFileName)` executa durante o render, fora do `try`, embora só falhe com configuração ausente, o que quebraria a aplicação bem antes. O R-01 do round anterior está fechado; a **classe** de falha que ele descreveu não está, e é por isso que o R-07 deste round existe.

### 2. A extração para `ProductPhotoUpload` foi ganho de desenho ou escopo inflado?

**Ganho de desenho.** Quatro verificações sustentam isso. A justificativa de testabilidade é verdadeira: `tests/Catalogo.Tests/Catalogo.Tests.csproj` não referencia bUnit nem qualquer renderizador de componente, então o `@code` de um `.razor` é literalmente inalcançável pela suíte, e o *seam* foi o que permitiu injetar um `IObjectStorage` que lança `HttpRequestException` — que é como o Supabase falha de verdade, via `EnsureSuccessStatusCode`. O arquivo novo nasce em `Features/Products/`, o escopo declarado da tarefa, e nada foi para `Features/Media/`. O componente **encolheu**: `UploadAsync` passou de decidir recusa, associar e traduzir mensagem para três atribuições, e `RejectionMessage` saiu do `.razor` junto com a responsabilidade que a usava. E a extração é o que tornou possível escrever a propriedade que fecha o R-01 como algo verificável — "este serviço não lança" — em vez de uma afirmação sobre um método de componente que ninguém pode exercitar.

Não é "um catch a mais", e isso é a favor: um catch a mais no `.razor` teria fechado o R-01 sem nenhum teste possível, o que é precisamente a situação que o round 1 criticou em R-05.

### 3. O `catch` em volta de `AttachPhotoAsync` proteger falha de banco é escopo demais?

**Defensável, mas mal declarado e não coberto.** A justificativa arquitetural se confirma no texto da ADR-018: "cada consulta ao banco passa a atravessar a rede, em vez de acontecer na mesma máquina", e o projeto gratuito do Supabase "é pausado após uma semana sem atividade no banco". Banco e armazenamento estão do mesmo lado da rede e falham pelos mesmos motivos; tratar apenas um dos dois seria fechar metade da porta. Nesse plano, o escopo extra é coerente, não oportunista.

O que não se sustenta é o tratamento ser **idêntico** ao do armazenamento. Os dois ramos têm consequências opostas para as órfãs e para o valor de repetir, e ambos devolvem a mesma frase (R-03). E o ramo admitido como fora do escopo é o único sem nenhum teste (R-04). Escopo extra se paga declarando e cobrindo; aqui foi declarado na conversa e não no código nem na suíte.

### 4. O R-02 do round anterior está resolvido ou deslocado?

**Resolvido no que ele acusava, com resíduo novo.** O finding era "apaga a foto da tela sem dizer nada". Hoje `attached is null` produz `LogWarning` com `productId` e raiz, devolve `ProductUnavailableMessage`, e `photo = outcome.Photo ?? photo` preserva a foto na tela. As três partes do finding — silêncio, apagamento, ausência de rastro — estão endereçadas, e há teste para o ramo (`Produto_inexistente_nao_apaga_a_foto_da_tela_sem_dizer_nada`), que não roda.

O `notFound` que o round 1 oferecia como alternativa teria fechado, sem custo adicional, o que "preserva e avisa" deixou aberto: a tela anuncia que o produto sumiu e mantém o `Salvar` ativo, que recria o produto com id novo em silêncio. Isso é R-06 deste round. É resíduo, não deslocamento — o dano mudou de natureza e de gravidade.

### 5. O `catch (Exception) when (...)` largo engole defeito de programação?

**Sim, e o desenho atual torna esse risco desnecessário.** Confirmado por leitura: os dois `catch` englobam `ImageValidation`, `ImageProcessor`, a interop do SkiaSharp e o mapeamento do EF, de modo que `NullReferenceException`, `ArgumentException` e `InvalidOperationException` chegam ao dono como "tente de novo em instantes" e ficam registradas apenas em log volátil (R-02). Há um agravante que o filtro introduz por conta própria: ele exclui `OperationCanceledException` e com isso exclui o *timeout* do `HttpClient`, que é a falha mais provável do envio nesta arquitetura (R-01).

O ponto que fecha o julgamento: como o componente passou a ter limite próprio, restringir os `catch` do serviço aos tipos de infraestrutura **não reabre o R-01**. O `catch` largo no serviço não compra proteção alguma que o `ProductForm` já não dê — compra apenas a perda da distinção entre "a rede falhou" e "o código tem um bug". É troca sem ganho.

---

## Cobertura por RN (Implementa)

### RN-09 — Cada produto tem uma única foto. Não há galeria. Enviar uma nova foto substitui a anterior *(ADR-005)*

- **Como foi implementada:** sem alteração neste round. A unicidade continua estrutural em `ProductPhoto` como tipo próprio do produto, e `AttachPhotoAsync` atribui em vez de acrescentar.
- **Evidência:** `src/Catalogo/Features/Products/ProductMaintenance.cs:135-164`; `src/Catalogo/Features/Products/ProductPhoto.cs`
- **O que este round mudou:** a regra ganhou um caminho de falha explícito — a substituição que não se completa deixa a foto anterior no lugar em vez de deixar o produto sem foto (`ProductPhotoUpload.cs:12`, `ProductForm.razor:243`), o que é a leitura correta de "substitui": ou substitui, ou não mexe.
- **Status:** ✅ Implementada corretamente, no código

---

## Cobertura por CA (Valida)

### CA-07 — Substituir a foto de um produto

- **Testes correspondentes:** `ProductPhotoTests.CA_07_trocar_a_foto_gera_nomes_novos_e_atualiza_a_referencia` e `ProductPhotoUploadTests.CA_07_substituir_a_foto_aponta_para_derivadas_novas_no_armazenamento` — **nenhum dos dois foi tocado por este round**
- **Situação:** a análise do round 1 permanece válida por leitura, incluindo a cláusula "a foto anterior deixa de ser exibida na vitrine", que continua não verificável porque a vitrine não existe no repositório.
- **Verificação por execução:** ⛔ não obtida. Ambos falham no *fixture* como os demais. Ver R-05.
- **Status:** ✅ na parte verificável por leitura

---

## Cobertura por UI (Telas)

### UI-05 — Produto, cadastro e edição

| Estado | Especificado | Implementado | Evidência |
|---|---|---|---|
| `.enviandoFoto` | Progresso; salvar indisponível até terminar | ⚠️ parcial, sem mudança | `ProductForm.razor:177-182` (mensagem e `role="status"`), `:173` e `:195` (controles desabilitados). Progresso continua ausente — R-07 de `REVIEW-T-13-2026-09-24` persiste |
| `.erroUpload` | Motivo da recusa; foto anterior preservada | ✅ no código | `ProductForm.razor:184-189`; mensagens em `ProductPhotoUpload.cs:26-33` e `:113-124`. Falha de infraestrutura agora chega a este estado; motivo de tamanho deixou de ser atribuído a erro de transporte. Concatenação contraditória no caso do produto excluído — R-06 |

**Asserções estáticas acrescentadas (R-05 do round anterior):** `UI_05_novo_explica_que_a_foto_exige_o_produto_salvo` e `UI_05_edicao_oferece_o_envio_da_foto_e_declara_a_substituicao`, em `ProductScreenTests.cs`. As duas seguem o padrão de T-12 (HTML servido, cliente autenticado) e usam o nome com prefixo `UI_05_`, coerente com a convenção do arquivo. A terceira asserção que o round 1 sugeriu — a listagem das quatro derivadas quando há foto, `data-estado="comFoto"` em `:159` — **não foi escrita**: `CreateProductAsync` cria o produto sem foto. Nenhuma das duas foi executada (R-05).

---

## Verificação da Decisão Arquitetural

### ADR-005 — Imagens processadas no upload em derivadas para tela e para impressão

- **Implementação:** inalterada. `ProductPhotoUpload` é uma camada de orquestração **acima** de `ProductPhotoService`, e não reimplementa validação, derivação, escolha de bucket nem nome imutável. `Features/Media/` não foi tocada.
- **Conformidade:** ✅ sem desvio. O ponto em aberto segue sendo de ciclo de vida do objeto, não de conformidade — e este round acrescentou um terceiro caminho de geração de órfã (R-03).

### ADR-010 — Modo de renderização por área

- **Materialização nova:** o comentário de `ProductPhotoUpload.cs:15-20` nomeia a consequência que sustenta o desenho — exceção escapando de manipulador de evento no painel interativo encerra o circuito e leva o formulário. É a leitura correta da decisão, e é bom que esteja no código e não só no review.
- **Conformidade:** ✅ no caminho da foto. ⚠️ a mesma consequência continua sem tratamento em `SaveAsync` e no ciclo de vida (R-07).

### ADR-018 — Aplicação no Render, banco e arquivos no Supabase

- **Materialização nova:** a ADR não está em `Decisões base:` de T-13, mas é ela que o round 1 usou para sustentar o bloqueio e é ela que este round vê aplicada: indisponibilidade de rede tratada como evento esperado, com mensagem que convida a repetir, distinta da recusa que pede outro arquivo.
- **Conformidade:** ✅ na intenção. ⚠️ o modo de falha mais característico da ADR — *timeout* de rede — é justamente o que o filtro do `catch` deixa escapar (R-01).

---

## Notas ao processo (não-findings)

- **`CLAUDE.md` ausente pelo sétimo review consecutivo.** Deixou de ser lacuna barata. Esta entrega estabeleceu uma convenção de tratamento de erro de alcance geral — serviço que não lança, limite próprio em cada manipulador de interação, `ErrorBoundary` deliberadamente descartado com motivo — e não existe lugar no repositório onde ela possa ser lida. Vale rodar `/leanwork-context raiz`, com essa convenção como primeiro item.
- **Quarta ocorrência do mesmo padrão de tratamento de erro.** R-01 de `REVIEW-T-09-2026-09-23`, R-01 de `REVIEW-T-11-2026-09-23`, R-01 de `REVIEW-T-12-2026-09-24` e o R-01 de `REVIEW-T-13-2026-09-24` são a mesma classe: exceção de infraestrutura chegando crua a quem não sabe o que fazer com ela. T-13 é a **primeira** a resolvê-la no seu caminho, e resolveu bem. Isso torna o desenho dela o candidato natural a convenção — e torna mais visível que os outros três continuam abertos.
- **A suíte tem três modos de execução e nenhum está declarado no repositório.** Com Docker e credencial do Supabase, com Docker e sem credencial, sem Docker. A ressalva já estava registrada em T-08 e no round 1; este round mostra a consequência concreta — cinco testes escritos para fechar um bloqueante e nenhum executado. Um `README` de testes com as variáveis e pré-requisitos custa pouco.
- **`docs/plans/PLAN-001-catalogo-virtual.md` não foi alterado por este review** (a atualização do plano é do dono do pipeline). Duas coisas cabem lá: o `Status` de T-13 e a correção de redação que o R-04 de `REVIEW-T-13-2026-09-24` pediu, que continua pendente na própria linha de T-13 — a linha nova de "Reviews T-12, T-13" registra que o R-01 de `REVIEW-T-08-2026-09-23` segue aberto, o que é correto, mas a frase original de T-13 que afirmava o contrário permanece intacta logo acima. O plano hoje se contradiz.
- **Mudanças não commitadas.** Toda a entrega deste round está no *working tree*. A rastreabilidade do eixo 2 — mensagem de commit citando `T-13` — não pode ser avaliada, e a linha de histórico de T-13 no plano não tem hash correspondente.

---

## Round anterior

Comparação com `docs/reviews/REVIEW-T-13-2026-09-24.md` (round 1, ⛔ Bloqueado, 1 Bloqueante + 6 Importantes + 3 Sugestões). A numeração recomeçou neste relatório: os `R-XX` da primeira coluna são do round anterior e não têm relação com os de mesmo número deste round.

| Item anterior | Status | Verificação | Comentário |
|---|---|---|---|
| 🔴 R-01 (round 1) — Falha do armazenamento derruba o circuito e apaga o formulário inteiro | ✅ **Resolvido** | leitura | `ProductPhotoUpload` devolve `PhotoUploadOutcome` em todos os caminhos e `ProductForm.razor:245` tem limite próprio; nenhum caminho de escape restou no envio (ver julgamento, item 1). Cobertura de regressão **não verificável** — R-05 deste round. Duas ressalvas de precisão no desenho: R-01 e R-02 deste round |
| 🟡 R-02 (round 1) — `AttachPhotoAsync` devolvendo `null` apaga a foto da tela sem dizer nada | ✅ **Resolvido**, com resíduo | leitura | `ProductPhotoUpload.cs:96-104` produz `LogWarning` + `ProductUnavailableMessage`, e a foto anterior fica na tela. Resíduo novo: a tela mantém `Salvar` ativo e salvar recria o produto em silêncio — R-06 deste round |
| 🟡 R-03 (round 1) — O rastro das derivadas substituídas vive só no log, que é volátil | ⚠️ **Persistente**, agravado | leitura | Nada foi persistido; `ProductMaintenance.cs:157-161` inalterado. Este round criou um **terceiro** caminho de geração de órfã, e nele a repetição sugerida ao dono multiplica as órfãs por tentativa — R-03 deste round |
| 🟡 R-04 (round 1) — O histórico do plano credita ao log a resolução de um finding de T-08 que continua aberto | ⚠️ **Persistente** (parcial) | leitura | A frase original na linha de T-13 do plano está intacta. A linha nova de "Reviews T-12, T-13" registra corretamente que o R-01 de `REVIEW-T-08-2026-09-23` segue aberto, mas não corrige a anterior — o plano se contradiz. `ProductPhotoService.StoreAsync` continua sem compensação na sequência de cinco uploads |
| 🟡 R-05 (round 1) — `enviandoFoto` e `erroUpload` sem nenhum teste, nem na parte estática | ⚠️ **Parcial** / ⛔ não verificável | execução tentada | Duas das três asserções sugeridas foram escritas em `ProductScreenTests.cs`, com a convenção de nome correta. A terceira (derivadas listadas, `data-estado="comFoto"`) não. Nenhuma das duas roda — R-05 deste round |
| 🟡 R-06 (round 1) — Qualquer `IOException` é reportada como "a imagem passa de 12 MB" | ✅ **Resolvido** | leitura | Guarda por `lengthInBytes` antes de qualquer leitura (`ProductPhotoUpload.cs:44-47`), e o `catch (IOException)` passou a significar transporte interrompido (`:55-63`), com mensagem própria. Exatamente a correção sugerida. O ramo de `IOException` em si ficou **sem teste** — R-04 deste round |
| 🟡 R-07 (round 1) — `enviandoFoto` especifica progresso e a tela mostra texto fixo | ⚠️ **Persistente** | leitura | `ProductForm.razor:177-182` inalterado: nem indicador indeterminado, nem registro da divergência na SPEC-UI (`docs/prototype/SPEC-UI-001-catalogo-virtual.md:309` continua prometendo "Progresso"). A decisão que o round 1 pediu — implementar ou declarar — não foi tomada |
| 🟢 R-08 (round 1) — A foto é gravada fora do `Salvar`, e `Cancelar` não desfaz a troca | ⚠️ **Persistente** | leitura | O texto de ajuda de `ProductForm.razor:152-155` não ganhou a frase sugerida. O R-06 deste round mostra que a separação entre "a foto vai agora" e "os campos vão no Salvar" tem mais uma consequência não explicada ao dono |
| 🟢 R-09 (round 1) — O rótulo do botão continua clicável durante o envio | ⚠️ **Persistente** | leitura | `ProductForm.razor:171-175` inalterado: o `disabled` segue apenas no `InputFile` e o `<label class="btn">` só perde `btn--primario`, sem `aria-disabled` nem `pointer-events` |
| 🟢 R-10 (round 1) — A lista dos cinco nomes da foto está duplicada em três arquivos | ⚠️ **Persistente**, agravado | leitura | `ProductPhotoUploadFailureTests.PhotoNamed` acrescentou o quarto lugar — R-09 deste round |

**Resumo:** 3 resolvidos (R-01, R-02, R-06 — os três que o round 1 apontou como vivendo no mesmo método), 1 parcial (R-05), 6 persistentes (R-03, R-04, R-07, R-08, R-09, R-10), dos quais 2 agravados por esta entrega (R-03, R-10). Nenhum dos três resolvidos tem verificação por execução.

---

## Conclusão

O round 2 fez o que um round 2 precisa fazer: atacou o bloqueante na raiz, não no sintoma. A extração de `ProductPhotoUpload` custou mais do que "um catch a mais", e custou bem — sem ela, o R-01 teria sido fechado por uma linha dentro de um `@code` que a suíte deste projeto não tem como exercitar, o que é a situação que o round 1 criticou em outro finding. O serviço que não lança é uma propriedade verificável, o componente encolheu de verdade, `photo = outcome.Photo ?? photo` diz o que faz, e os três findings que o round 1 apontou como vivendo no mesmo método caíram juntos, como ele previu. O descarte do `ErrorBoundary` está certo no mecanismo e a análise de caminhos de escape confirma que o envio ficou fechado.

O que segura a aprovação plena são duas coisas de naturezas diferentes. A primeira é de ambiente: cinco testes foram escritos para provar a correção do bloqueante e **nenhum deles rodou** — 74 falhas de infraestrutura contra 8 aprovados, e os cinco novos morrendo no *fixture* em um milissegundo. A correção é sólida por leitura e frágil por regressão, e o *harness* fora do repositório não substitui a suíte porque não sobrevive a amanhã. A segunda é de precisão: o `catch` largo com filtro por negação erra nas duas direções ao mesmo tempo — deixa passar o *timeout*, que é a falha esperada da ADR-018, e retém o defeito de programação, que era para passar. Como o componente já tem limite próprio, apertar esses `catch` é ganho sem custo.

Os seis findings do round 1 que persistem não mudaram de tamanho, mas dois cresceram: o rastro das órfãs, porque este round abriu um terceiro caminho de geração e sugere ao dono a ação que o multiplica; e a duplicação dos cinco nomes, que ganhou um quarto lugar. Nenhum dos dois é urgente hoje; ambos aparecem em três relatórios seguidos, o que é o sinal de que precisam de tarefa no plano em vez de mais uma menção em review.

**Próximos passos sugeridos:**

- Subir o Docker Desktop e rodar `dotnet test` uma vez, registrando o número real no histórico. **É a condição para a ressalva principal cair** (R-05)
- Apertar os dois `catch` de `ProductPhotoUpload` para os tipos de infraestrutura, incluindo o *timeout* do `HttpClient`, e dar mensagem própria ao ramo do banco (R-01, R-02, R-03) — os três são a mesma edição
- Levar a tela ao `notFound` quando o produto não existe mais, ou desabilitar `Salvar` nesse estado (R-06). O ressuscitamento silencioso em `ProductMaintenance.SaveAsync` merece finding próprio em T-12
- Cobrir os dois ramos sem teste — `IOException` e falha de banco —, com a costura de R-08 se a intenção for não depender de container (R-04)
- Rodar `/leanwork-context raiz` e registrar a convenção de tratamento de erro que esta entrega estabeleceu, junto com a decisão sobre o `ErrorBoundary` (R-07)
- Corrigir a contradição do plano quanto ao R-01 de `REVIEW-T-08-2026-09-23` (R-04 do round anterior) e decidir se a persistência dos nomes órfãos entra agora ou vira tarefa (R-03 do round anterior)
- Decidir de uma vez o R-07 do round anterior: indicador indeterminado no `.enviandoFoto`, ou registrar na SPEC-UI que o estado não reporta progresso
