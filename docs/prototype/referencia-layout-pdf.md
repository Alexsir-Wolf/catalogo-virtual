# Referência de Layout — Catálogo em PDF

> Documento de referência · 2026-09-14
>
> Arquivo de origem: [`assets/referencia-catalogo.pdf`](assets/referencia-catalogo.pdf) — catálogo real em uso pelo cliente, fornecido como gabarito do documento a ser gerado.
>
> Este documento descreve **o que foi observado** no PDF de referência. Ele não é a especificação final — é o insumo para o SPEC-UI da página impressa. Pontos não observáveis no arquivo estão marcados como pergunta em aberto, nunca preenchidos por suposição.

## 1. Para que serve esta referência

O cliente já tem o layout do catálogo e quer que o sistema produza documentos com essa mesma aparência, variando apenas os produtos.

**Importante**: o gabarito é reproduzido em código, não preenchido. Não se trata de sobrepor texto ao PDF existente, porque três coisas variam a cada geração e nenhuma cabe em um arquivo de tamanho fixo:

- a quantidade de produtos, e portanto **a quantidade de páginas**
- quais categorias aparecem, e portanto **o conteúdo e a numeração do índice**
- a distribuição dos produtos pela grade, e portanto **onde cada categoria começa**

O documento de referência define tipografia, proporção, ordem dos elementos e tom. A composição é sempre nova (ver `ADR-012` na proposta arquitetural).

## 2. Anatomia do documento observado

Formato: retrato, proporção compatível com A4. Seis páginas: uma de capa e cinco de conteúdo.

### 2.1 Capa (página 1)

Elementos, na ordem em que aparecem:

| Elemento | Conteúdo observado |
|---|---|
| Título | `CATÁLOGO` / `DE PRODUTOS` — em duas linhas, caixa alta, peso alto |
| Subtítulo | "Suprimentos e equipamentos de informática com toda a documentação adequada para sua compra: nota fiscal, recibo e orçamentos." |
| Selos | Três marcadores em destaque: `NOTA FISCAL` · `RECIBO` · `ORÇAMENTO` |
| Diferenciais | Lista com marcador: "Fornecemos os 3 orçamentos" · "Toda documentação adequada" · "Material de limpeza, EPI e descartáveis" |
| Índice | Rótulo `// ÍNDICE` seguido das categorias numeradas — ver 2.3 |
| Contato | Nome do responsável · telefone |

O índice observado:

```
01 Impressoras
02 Energia & Proteção
03 Tintas & Suprimentos
04 Softwares & Licenças
05 Computadores & Componentes
06 Redes & Cabeamento
07 Periféricos & Acessórios
```

### 2.2 Páginas de conteúdo (2 a 6)

| Região | Conteúdo observado |
|---|---|
| Cabeçalho | `CATÁLOGO DE PRODUTOS`, alinhado à direita, repetido em todas as páginas de conteúdo |
| Corpo | Seções de categoria, cada uma seguida de uma grade de produtos |
| Rodapé | `sigla · Suprimentos & Informática · nome do responsável – telefone` à esquerda, `pág. N` à direita |

### 2.3 Seção de categoria

Título em caixa alta, precedido do número de dois dígitos:

```
01 IMPRESSORAS
05 COMPUTADORES & COMPONENTES
```

**A numeração é posicional dentro do catálogo gerado, não um identificador fixo da categoria.** No documento de referência, "Impressoras" é `01` porque é a primeira categoria do recorte. Um catálogo que contivesse apenas Redes e Periféricos deveria numerá-las `01` e `02`. O índice da capa reflete essa mesma numeração.

**As categorias fluem continuamente, sem quebra de página forçada.** Observado: a página 2 contém as categorias `01` e `02`; a página 3 contém `03` e `04`; a página 6 é a continuação de `07`, iniciada na página 5, e não repete o título da categoria.

### 2.4 Item de produto

Cada célula da grade contém, de cima para baixo:

1. Imagem do produto
2. Nome e descrição em um único bloco de texto corrido, de 1 a 4 linhas
3. Rótulo de preço + valor em reais

O rótulo de preço varia: a maioria dos itens usa `PREÇO`, mas ao menos um usa `PREÇO/UND` (Refil de Tinta EPSON 544 Original).

Exemplos de blocos de texto observados, do mais curto ao mais longo:

- `Mouse Pad 23x18cm`
- `Cabo de Rede CAT6 – bobina 100 metros, homologado Anatel`
- `Notebook Positivo Vision R15M – Ryzen 3-7330U, 8GB, SSD 256GB, Wi-Fi 6, 15" IPS, Linux – Preto`

A célula acomoda a variação de altura sem que o preço saia do lugar em relação ao item.

### 2.5 Grade

Três colunas. A última linha de cada categoria pode ficar incompleta — observado em quase todas as categorias — e a categoria seguinte começa em nova linha, não preenchendo o espaço vago.

Contagem de produtos por categoria no documento de referência:

| Categoria | Itens |
|---|---|
| 01 Impressoras | 4 |
| 02 Energia & Proteção | 2 |
| 03 Tintas & Suprimentos | 4 |
| 04 Softwares & Licenças | 2 |
| 05 Computadores & Componentes | 8 |
| 06 Redes & Cabeamento | 5 |
| 07 Periféricos & Acessórios | 11 |
| **Total** | **36** |

## 3. Campos de produto que o layout exige

Derivados do que aparece impresso:

| Campo | Obrigatório | Observação |
|---|---|---|
| Nome | Sim | Identifica o produto. Impresso em destaque |
| Descrição | Não | A especificação técnica. Impressa abaixo do nome, em corpo menor. Comprimento bastante variável — de vazia a três linhas |
| Preço | Sim | Formatado em reais, com separador de milhar e dois decimais |
| Rótulo do preço | Sim | `PREÇO` por padrão; variantes como `PREÇO/UND` observadas |
| Foto | Sim | **Uma por produto**, não galeria. Uma por célula da grade |
| Categoria | Sim | Define o agrupamento e a ordem |

> Nota de revisão (2026-09-14): a leitura inicial deste documento tratava nome e descrição como um bloco único de texto corrido, porque é assim que a extração de texto os entrega. O cliente definiu que são **dois campos separados** no cadastro. A impressão continua parecendo um bloco — o que muda é que o nome pode receber peso tipográfico próprio, e a descrição pode estar vazia sem deixar buraco no layout.

Campos que **não** aparecem no documento de referência e que, portanto, não são exigidos por ele: SKU, marca isolada do nome, código NCM, estoque, preço por embalagem, selo de destaque.

## 4. Dados da empresa presentes no documento

Aparecem na capa e no rodapé de todas as páginas:

- Nome fantasia: sigla de três letras e razão social (omitidos aqui; constam no arquivo de origem)
- Responsável: nome de uma pessoa (omitido aqui; consta no arquivo de origem)
- Telefone: número de contato (omitido aqui; consta no arquivo de origem)

São constantes ao longo do documento e independentes do recorte de produtos.

## 5. Consequência para o dimensionamento das imagens

O arquivo de referência tem **7,3 MB para 36 produtos**, cerca de 200 KB por item. É um documento que passa pelo limite de anexo do WhatsApp, mas é mais pesado do que precisa ser, e o peso cresce proporcionalmente ao tamanho do catálogo.

A estimativa de área útil explica o excesso. Em retrato A4, descontadas margens laterais e o espaçamento entre colunas, cada coluna ocupa aproximadamente **55 mm** de largura. A imagem impressa nesse tamanho exige:

| Resolução alvo | Largura necessária |
|---|---|
| 200 DPI (leitura confortável) | ~430 px |
| 300 DPI (qualidade de impressão) | ~650 px |

Isso sustenta a derivada de impressão decidida na `ADR-005`: um JPEG com lado maior em torno de **800 px** cobre com folga o pior caso, e deve produzir arquivos na casa de dezenas de quilobytes por item — uma fração do que o documento de referência carrega hoje.

**Esse número precisa ser confirmado por medição, não adotado por cálculo.** O passo 2 dos próximos passos da proposta arquitetural existe para isso, e o passo 3 — imprimir o piloto em papel — é o que valida se 200 DPI bastam ou se o alvo precisa subir.

## 6. Decisões tomadas sobre o gabarito

Respondidas pelo cliente em 2026-09-14 e incorporadas à proposta arquitetural.

| # | Pergunta | Decisão |
|---|---|---|
| 1 | O texto da capa é fixo ou editável? | **Fixo.** Título, subtítulo, selos, diferenciais e contato são constantes no código, não editáveis pelo dono (`ADR-012`) |
| 7 | Os dados da empresa são configuração do sistema? | **Fixos**, pelo mesmo motivo — há um único dono e um único catálogo |
| 3 | Qual a ordem das categorias? | **Definida no cadastro da categoria**, global. A numeração impressa (`01`…`07`) é posicional dentro do recorte gerado (`ADR-015`) |
| 4 | Qual a ordem dos produtos dentro da categoria? | **Definida manualmente no cadastro do produto**, global. Não há ordem por catálogo (`ADR-015`) |
| — | Como o catálogo escolhe os produtos? | **Por filtro salvo, resolvido no momento da geração.** Não se guarda lista de itens (`ADR-014`) |

A decisão do filtro dinâmico tem uma consequência que precisa aparecer na interface: **produtos entram em catálogos existentes apenas por serem cadastrados**. Por isso a pré-visualização antes da geração é requisito, não conveniência.

## 7. Perguntas ainda em aberto

Não são observáveis no arquivo e precisam ser decididas no PRD. Nenhuma foi preenchida por suposição.

1. **O índice é sempre exibido?** Um catálogo de uma única categoria talvez não o justifique.
2. **O que acontece com produto sem imagem?** Nenhum caso no documento de referência, e a grade de três colunas depende da imagem para funcionar. Espaço reservado, item em formato reduzido, ou impedir a entrada no catálogo?
3. **O rótulo de preço é campo do produto?** A variante `PREÇO/UND` sugere que sim, mas com um único caso observado não dá para afirmar se é campo livre ou lista fechada.
4. **Há contracapa ou página final?** O documento termina na última linha de produtos, sem encerramento.
5. **Quais campos podem compor o filtro de um catálogo?** Categoria apenas, ou também faixa de preço e texto de busca? Combináveis entre si?
6. **Existe estado de rascunho no produto?** Com o filtro dinâmico, um produto cadastrado pela metade entraria em catálogos existentes. Um estado que o mantenha fora dos filtros até ser liberado resolveria — mas é decisão de produto.

## 8. O que este documento não cobre

- **Cores, fontes, pesos e espaçamentos exatos** — a extração de texto preserva estrutura, não estilo. A definição tipográfica precisa ser feita com o PDF aberto lado a lado, na fase de especificação de interface
- **Posicionamento milimétrico** — as medidas da seção 5 são estimativas a partir do formato, suficientes para dimensionar imagem, insuficientes para diagramar
- **Comportamento em casos-limite** — nome muito mais longo que os observados, preço com mais dígitos, categoria com um único item
