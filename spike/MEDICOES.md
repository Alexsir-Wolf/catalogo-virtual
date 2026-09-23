# T-03 — Medições do spike: da foto ao PDF impresso

> Executado em 2026-09-22 · máquina de desenvolvimento (Windows 11, .NET 10)
>
> Este documento registra **o que foi medido**, não o que foi estimado. Os números que ele produz alimentam **T-04**, que é onde eles viram decisão depois da impressão em papel.

## 1. O que o spike faz

Um programa descartável que atravessa o caminho inteiro sem banco, sem painel e sem interface:

1. **Extrai as fotos reais** de `docs/prototype/assets/referencia-catalogo.pdf` — o catálogo em uso pelo cliente
2. **Gera a derivada de impressão** de cada uma, em JPEG com o lado maior em 800 px *(ADR-005)*
3. **Compõe o PDF** com QuestPDF, reproduzindo a grade do gabarito em código *(ADR-012)*
4. **Mede** tamanho, tempo e capacidade da célula
5. **Renderiza as páginas em PNG** para inspeção visual

A escolha de extrair as fotos do próprio gabarito atende ao risco declarado na tarefa: *"usar fotos reais de produto, não imagens sintéticas — fundo branco de catálogo comprime muito melhor que foto texturizada"*. São exatamente as fotos que o cliente usa.

## 2. Resultados

### 2.1 Derivadas de impressão

| Medida | Valor |
|---|---|
| Fotos processadas | 36 |
| Tempo total | 647 ms |
| Tempo por foto | **18 ms** |
| Volume original (extraído do PDF) | 6.673 KB |
| Volume das derivadas | 2.036 KB |
| Média por produto | **56 KB** |

A derivada de 800 px custa **56 KB por produto**. Com a estimativa da referência de layout (~200 KB por item no catálogo atual), a redução é de cerca de 3,5×.

### 2.2 Composição do PDF

| Medida | Valor |
|---|---|
| Produtos | 36, em 7 categorias |
| Páginas geradas | 4 |
| Tempo de composição | 650 ms |
| Tempo por produto | **18 ms** |
| Tamanho do arquivo | **472 KB** |
| Catálogo de referência do cliente | 7.134 KB, mesmos 36 produtos, 5 páginas de conteúdo |

**O arquivo gerado é 15× menor que o do cliente**, com a mesma quantidade de produtos e uma página a menos. Isso ataca diretamente o problema de distribuição por WhatsApp registrado no PRD.

Extrapolando o tempo medido: um catálogo de 200 produtos levaria cerca de **3,6 s** de composição — dentro do que a ADR-013 admite para geração síncrona, com folga. O número precisa ser reconfirmado no Render, que é mais lento que a máquina de desenvolvimento.

### 2.3 Capacidade da célula

Medida a partir da largura real da coluna e das métricas da fonte, não estimada:

| Medida | Valor |
|---|---|
| Largura da coluna | **57,8 mm** (a referência de layout estimava ~55 mm) |
| Caracteres por linha, a 7,5 pt | **40** |
| Capacidade em 4 linhas — o máximo observado no gabarito | **160 caracteres** |
| Maior descrição do spike que coube em 4 linhas | 137 caracteres |

**Insumo para RN-03:** o limite do resumo deveria ficar em torno de **160 caracteres**. Acima disso a célula passa de quatro linhas e rompe a regularidade visual do gabarito — não quebra o layout, mas destoa.

## 3. Respostas às perguntas do spike

| Pergunta da tarefa | Resposta |
|---|---|
| A biblioteca dá conta do layout do gabarito? | **Sim.** Grade de três colunas, categorias em fluxo contínuo, última linha incompleta, cabeçalho e rodapé repetidos, numeração de página — tudo reproduzido |
| Quanto pesa o arquivo final? | 472 KB para 36 produtos — cerca de 13 KB por produto |
| Quanto tempo leva por produto? | 18 ms de composição + 18 ms de derivada (esta última acontece no upload, não na geração) |
| Quantos caracteres cabem na célula? | 160 em quatro linhas |

**Nenhuma célula foi partida entre páginas.** A garantia é o `ShowEntire()` aplicado a cada célula: quando o bloco não cabe no resto da página, ele inteiro desce. A página 2 do PDF gerado começa com a continuação da categoria `03` sem repetir o título — exatamente o comportamento descrito na seção 2.3 da referência de layout.

## 4. Achados que afetam decisões adiante

### 4.1 A ADR-012 se sustenta — sem ressalva

A biblioteca entregou paginação controlada, faixas repetidas e bloco indivisível sem nenhum artifício. O plano B registrado na ADR-012 (navegador headless) não precisa ser acionado.

### 4.2 800 px é generoso, e T-04 pode reduzir

Na composição do spike, a imagem ocupa uma caixa de 90 pt de altura — cerca de 31,75 mm. Uma foto quadrada de 800 px impressa nessa altura resulta em aproximadamente **640 DPI**, mais que o dobro dos 300 DPI de qualidade de impressão.

Duas ressalvas antes de concluir que dá para baixar o valor:

- **A altura de 90 pt é escolha deste spike, não medida do gabarito.** As medidas tipográficas reais são a lacuna 3 da SPEC-UI, ainda em aberto. Se a imagem do gabarito for maior, o DPI efetivo cai
- **A decisão é de T-04, e depende do papel.** O número só fecha com a folha impressa na mão

### 4.3 Uma decisão de biblioteca que a arquitetura não tomou

A ADR-005 define **o que** produzir (quatro derivadas, formatos, dimensão), mas nenhuma ADR escolhe a biblioteca de processamento de imagem. O spike usou **SkiaSharp**, por licença BSD — sem restrição de faturamento, diferente do modelo da ADR-012.

A alternativa natural, ImageSharp, tem licença dupla com o mesmo teto de US$ 1 milhão que a QuestPDF. Adotá-la significaria concentrar **duas** dependências no mesmo gatilho de licenciamento, quando há opção sem gatilho nenhum. Vale uma ADR antes de T-08.

### 4.4 Extração de foto de PDF é viável

Não era objetivo da tarefa, mas ficou provado: as 36 fotos do catálogo atual podem ser recuperadas do próprio PDF. Isso reduz o trabalho de **T-29** (carregar o acervo real), que pressupunha obter as fotos originais com o cliente. A qualidade precisa ser conferida — a extração devolve o que foi embutido no documento, que já passou por compressão.

## 5. O que este spike não responde

- **Se 800 px basta em papel.** É T-04, e exige impressora
- **Tipografia, cores e espaçamentos do gabarito.** Continuam sendo a lacuna 3 da SPEC-UI. O layout aqui é aproximação estrutural, não reprodução visual
- **Desempenho no Render.** As medidas são de máquina de desenvolvimento
- **Comportamento do índice e da capa.** A capa é arquivo enviado pelo dono *(ADR-017)*, fora do escopo da composição

## 6. Como reproduzir

```bash
cd spike
dotnet run
```

Saída em `spike/saida/`: `catalogo-spike.pdf` (o artefato), `fotos/` (extraídas), `impressao/` (derivadas) e `paginas/` (PNG para inspeção).
