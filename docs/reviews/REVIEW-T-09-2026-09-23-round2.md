# Review: T-09 — CRUD de categoria

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

O bloqueante do round anterior foi resolvido, e resolvido no lugar certo: a unicidade insensível a caixa passou a ser propriedade da coluna, via collation ICU não determinística, em vez de virar normalização na aplicação. Isso mantém a regra inviolável por quem escrever direto no banco e não exige que o serviço se lembre de normalizar. Dois testes cobrem criação e renomeação, e ambos falhariam sem a correção.

A correção, porém, traz duas consequências que o round 1 não tinha como antecipar, porque dependiam da solução escolhida: a migration não trata dados preexistentes que violem a nova restrição, e a coluna passou a ser incompatível com busca por padrão. Nenhuma das duas bloqueia — a primeira tem janela curta e a segunda não colide com requisito atual —, mas ambas são armadilhas silenciosas.

Os dois `Importante` do round anterior sobre a interface permanecem intocados.

**Findings por severidade:**

| Severidade | Quantidade |
|------------|------------|
| Bloqueante | 0 |
| Importante | 4 |
| Sugestão   | 2 |
| **Total**  | **6** |

**Cobertura da tarefa:**

| Item | Esperado | Entregue | Status |
|------|----------|----------|--------|
| Regras implementadas (RN) | RN-23 | RN-23 completa | ✅ |
| Cenários validados (CA) | — | — | ✅ n/a |
| Decisões base (ADR) | ADR-003 | respeitada | ✅ |
| Critérios de aceite da tarefa | 4 | 4 atendidos | ✅ |
| Telas e estados (UI) | UI-06 (default, vazio, nomeDuplicado) | os três | ⚠️ ver R-03 |
| Testes prometidos | 2 | 10 | ✅ |

---

## Round anterior

Comparação item a item com `REVIEW-T-09-2026-09-23` (round 1).

| Item do round 1 | Situação | Evidência |
|---|---|---|
| **R-01 (round anterior)** — unicidade sensível a maiúsculas | ✅ **Resolvido** | Collation `nome_sem_caixa` aplicada à coluna; migration `CategoryNameCaseInsensitive`; testes `RN_23_nome_que_difere_apenas_na_caixa_e_recusado` e `Renomear_para_nome_que_difere_apenas_na_caixa_e_recusado` |
| **R-02 (round anterior)** — contagem de produtos ausente na tela | ❌ **Persiste** | `CategoryList.razor` segue com número e nome apenas. Reaparece neste round como R-02 |
| **R-03 (round anterior)** — erro de renomear exibido junto ao campo errado | ❌ **Persiste** | Reaparece neste round como R-03 |
| **R-04 (round anterior)** — estados de erro da tela sem teste | ❌ **Persiste** | `CategoryScreenTests` continua cobrindo só `vazio` e `default` |
| **R-05 (round anterior)** — `CategoryMaintenance` scoped sem necessidade | ❌ **Persiste** | `Program.cs:32` |
| **R-06 (round anterior)** — desempate por nome na ordenação | ✅ **Mitigado** | A normalização introduzida em T-10 passou a gravar `1..N` a cada movimento, o que reduz posições repetidas ao caso de categorias nunca reordenadas |

A persistência dos itens `Importante` é legítima — o round 1 marcou R-02 e R-03 como "resolver nesta tarefa ou explicitamente adiar", e o adiamento não foi registrado em lugar nenhum. É isso que este round formaliza.

---

## Contexto da implementação

### Stack detectada

- **Interface:** Blazor Web App, componente interativo de servidor
- **Dados:** EF Core 10 com Npgsql, `IDbContextFactory`
- **Banco:** PostgreSQL 17 com ICU
- **Fonte:** ADR-003, ADR-004, ADR-010; confirmada por inspeção do `.csproj`

### Padrões específicos aplicados

> ⚠️ Segue sem `CLAUDE.md` na raiz. Apenas critérios universais e as ADRs.

### Escopo do diff desde o round 1

- **Commit:** `187623c`
- **Arquivos relevantes a T-09:** `src/Catalogo/Data/CatalogDbContext.cs`, `src/Catalogo/Data/Migrations/*CategoryNameCaseInsensitive.*`, `tests/Catalogo.Tests/CategoryMaintenanceTests.cs`

---

## Findings detalhados

### 🟡 Importantes

#### R-01 — Migration da collation não trata dados preexistentes em conflito

- **Eixo:** 5. Qualidade do código
- **Referência cruzada:** RN-23
- **Evidência:** `src/Catalogo/Data/Migrations/*_CategoryNameCaseInsensitive.cs:20-26` — um `AlterColumn` com a nova collation, sem passo anterior de normalização
- **Descrição:** a migration troca a collation da coluna que sustenta o índice único. Se o banco já contiver `Tintas` e `tintas`, o índice não pode ser reconstruído e a migration falha.

  O efeito vai além da migration: `ApplyPendingMigrationsAsync` roda na subida da aplicação e a exceção não é capturada — a aplicação inteira não sobe, vitrine pública incluída. É o mesmo modo de falha que a correção do seeder de T-07 eliminou para o caso da credencial, e que aqui continua aberto para o caso da migration.
- **Por quê é Importante:** a janela concreta é curta — o ambiente de produção subiu com a tabela de categorias vazia, e a migration passou. Mas qualquer restauração de backup anterior, ou qualquer ambiente criado antes desta correção com duas categorias que diferem só na caixa, reproduz o problema. E o sintoma é a aplicação fora do ar, não uma mensagem de erro.
- **Sugestão de correção:** duas frentes, independentes. Na migration, um passo de normalização antes do `AlterColumn` — renomear duplicatas acrescentando sufixo, por exemplo. E, de forma mais geral, decidir se falha de migration deve derrubar a aplicação: hoje derruba, e a ADR-010 diz que a vitrine é a área que precisa ficar de pé.

#### R-02 — Contagem de produtos por categoria continua ausente

- **Eixo:** 6. Conformidade de interface
- **Referência cruzada:** UI-06, RN-50
- **Evidência:** `CategoryList.razor:45-52`; SPEC-UI, UI-06, estado padrão: "Lista ordenável com número, nome e **contagem**"
- **Descrição:** persiste do round anterior, e ganhou um segundo endereço: a **RN-50** exige que o filtro por categoria da vitrine exiba "a contagem de produtos de cada uma". A mesma informação é pedida em duas telas distintas — o painel em UI-06 e a vitrine em UI-01.
- **Por quê é Importante:** deixou de ser apenas divergência com a tela especificada. Como a contagem agora é requisito em dois lugares, e um terceiro uso já existe na mensagem de bloqueio de T-11, há risco real de a consulta ser escrita três vezes de formas diferentes. A ADR-003 já sinaliza esse risco — regra de exibição nascendo colada a cada componente é a dívida que ela registra.
- **Sugestão de correção:** resolver a contagem em um lugar só, na consulta de listagem de categorias, e consumir dali no painel. T-18 monta a consulta pública e deve reutilizar, não reimplementar.

#### R-03 — Erro ao renomear continua exibido junto ao campo de criação

- **Eixo:** 6. Conformidade de interface
- **Referência cruzada:** UI-06, estado `nomeDuplicado`
- **Evidência:** `CategoryList.razor` — `RenameAsync` grava em `failure`, que é renderizado no formulário de nova categoria, no topo da tela
- **Descrição:** persiste do round anterior. Tentar renomear para um nome existente mostra a mensagem ligada ao campo errado, enquanto a linha em edição fica aberta sem indicação.
- **Por quê é Importante:** a SPEC-UI descreve o estado como "Erro junto ao campo". Com a correção do bloqueante, a superfície do problema aumentou: agora a recusa também acontece por diferença de caixa, que é um caso em que o usuário **não vê motivo aparente** — os dois nomes parecem diferentes na tela. Uma mensagem no lugar errado, para uma recusa que já é contraintuitiva, é pior do que era no round 1.
- **Sugestão de correção:** separar o estado de falha da criação do estado de falha da edição, e renderizar cada um junto ao seu campo.

#### R-04 — Collation não determinística impede busca por padrão na coluna

- **Eixo:** 5. Qualidade do código
- **Referência cruzada:** RN-49, ADR-004
- **Evidência:** `CatalogDbContext.cs` — `deterministic: false` na definição de `nome_sem_caixa`, aplicada a `Category.Name`
- **Descrição:** o PostgreSQL não suporta operadores de padrão — `LIKE`, `ILIKE`, índices `text_pattern_ops` — sobre colunas com collation não determinística. Uma consulta que tente filtrar categorias por trecho de nome falha **em tempo de execução**, com erro do banco, não em compilação.

  Hoje isso não colide com requisito algum: a RN-49 é explícita em que a busca da vitrine procura "**exclusivamente no nome do produto**", e o índice trigrama vive em `Products.Name`, que não recebeu a collation. A escolha está correta para o requisito atual.
- **Por quê é Importante:** é uma restrição invisível. Nada no código sinaliza que aquela coluna não aceita busca por padrão, e quem escrever um filtro de categorias no painel — T-17 é candidato — descobre com uma exceção em produção. O custo de documentar agora é uma linha; o de descobrir depois é uma sessão de depuração.
- **Sugestão de correção:** registrar a restrição onde ela é decidida, no `CatalogDbContext`, junto do comentário que já explica a collation. Se algum dia for preciso buscar categoria por trecho, a saída é `unaccent`/`lower` sobre a coluna com collation determinística, o que reabriria o problema da unicidade — e é justamente isso que precisa estar escrito.

### 🟢 Sugestões

#### R-05 — Estados de erro da tela seguem sem teste

- **Eixo:** 4. Cobertura de teste
- **Evidência:** `CategoryScreenTests` cobre `vazio` e `default`; `nomeDuplicado` só na camada de serviço
- **Descrição:** persiste do round anterior. Vale notar que T-07 ganhou, nesta mesma sessão, `LoginScreenStatesTests` cobrindo estado de tela por asserção sobre o HTML — existe precedente e ferramenta no próprio repositório.
- **Sugestão:** um teste no mesmo molde, afirmando a presença de `data-estado="nomeDuplicado"` após envio com nome repetido. Os atributos já existem para isso.

#### R-06 — `CategoryMaintenance` segue registrado como scoped

- **Eixo:** 5. Qualidade do código
- **Evidência:** `Program.cs:32`
- **Descrição:** persiste do round anterior. A classe não tem estado e só depende da fábrica.
- **Sugestão:** singleton. Efeito prático desprezível; é clareza.

---

## Cobertura por RN (Implementa)

### RN-23 — A categoria tem nome e posição. O nome é obrigatório e único

- **Como foi implementada:** obrigatoriedade no serviço, com aparo de espaços; unicidade pelo índice do banco sobre coluna com collation insensível a caixa, com a violação traduzida em erro de campo
- **Evidência:** `CategoryMaintenance.cs:44-58` e `:186-196`; `CatalogDbContext.cs`, definição da collation e aplicação na coluna
- **Status:** ✅ **Implementada corretamente** — era ⛔ Parcial no round 1

A escolha de resolver no banco, e não na aplicação, é a decisão certa aqui. Normalizar no serviço deixaria a regra dependente de todo caminho de escrita lembrar de normalizar; a collation vale inclusive para quem escrever por SQL direto.

---

## Cobertura por UI (Telas)

| Estado | Round 1 | Round 2 | Observação |
|---|---|---|---|
| `UI-06.default` | ✅ | ⚠️ | falta a contagem, ver R-02 |
| `UI-06.vazio` | ✅ | ✅ | — |
| `UI-06.nomeDuplicado` | ⚠️ | ⚠️ | existe; posicionamento errado no fluxo de renomear, ver R-03 |
| `UI-06.bloqueadaPorProdutos` | — | ✅ | entregue em T-11, fora do escopo de T-09 |
| `UI-06.bloqueadaPorCatalogo` | — | — | previsto para T-26 |

---

## Observação sobre o plano

R-02 deste round mostra que a contagem de produtos por categoria é requisito em três lugares — UI-06 no painel, RN-50 na vitrine e a mensagem de bloqueio da RN-25 — e nenhuma tarefa a possui explicitamente. T-09 deveria tê-la entregue pela SPEC-UI, T-18 vai precisar dela pela RN-50, e T-11 já a calcula por conta própria. Vale decidir onde ela mora antes que existam três implementações.
