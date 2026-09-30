# Restauração e recuperação de acesso

Procedimento para trazer o catálogo de volta a partir de uma cópia feita por
[`backup.sh`](backup.sh), e para redefinir a senha do dono quando ela for perdida.

> **Backup nunca restaurado não é backup.** Este documento existe para ser **executado**, não
> lido: a seção 5 registra quando a restauração foi provada de verdade, em ambiente limpo. Até
> que aquela seção esteja preenchida, este procedimento é uma hipótese.

---

## 1. O que a cópia contém

Uma execução de `backup.sh` produz um diretório com data e três coisas:

| Item | Onde | Por que importa |
|---|---|---|
| `banco.dump` | raiz da cópia | Todo o acervo: categorias, produtos, catálogos salvos, configuração e a conta do dono |
| `arquivos/produtos-web/` | bucket público | As três derivadas de tela de cada foto, e a capa do PDF |
| `arquivos/produtos-originais/` | bucket privado | O original de cada foto e a derivada de impressão |
| `MANIFESTO.txt` | raiz da cópia | Contagens para conferir que a cópia está completa **antes** de começar |

**O dump não contém os arquivos.** Restaurar só o banco produz um catálogo cujos produtos
existem e cujas imagens não abrem — e a vitrine passa a exibir item quebrado. As duas metades
são necessárias.

---

## 2. Antes de começar

Confira o manifesto contra a expectativa:

```bash
cat /caminho/da/copia/MANIFESTO.txt
```

O número de arquivos em `produtos-web` deve ser próximo de **três vezes** o número de produtos
com foto, mais um por capa enviada; em `produtos-originais`, **duas vezes**. Divergência grande
significa cópia incompleta — nesse caso, use a cópia anterior.

Tenha em mão:

- A cadeia de conexão do banco **de destino** (`DATABASE_URL`)
- O endereço e a chave de serviço do Supabase **de destino**
- `psql`, `pg_restore` e `curl` no `PATH`

---

## 3. Restaurar o banco

O destino precisa estar **vazio**. Restaurar sobre um banco com dados mistura dois acervos e é
pior que não restaurar.

```bash
# 1. Conferir que o destino está vazio (deve responder 0)
psql "$DATABASE_URL" -tAc \
  "select count(*) from information_schema.tables where table_schema = 'public';"

# 2. Restaurar
pg_restore --no-owner --no-privileges --dbname="$DATABASE_URL" /caminho/da/copia/banco.dump

# 3. Conferir o acervo
psql "$DATABASE_URL" -c \
  'select (select count(*) from "Categories") as categorias,
          (select count(*) from "Products") as produtos,
          (select count(*) from "Catalogs") as catalogos;'
```

**Não rode as migrations antes do `pg_restore`.** O dump traz o esquema; aplicar migrations
primeiro cria tabelas que a restauração tentará criar de novo. A aplicação aplica migrations
pendentes na subida, e num banco restaurado não há nenhuma.

---

## 4. Restaurar os arquivos

Os buckets precisam existir no destino, com a mesma visibilidade:

| Bucket | Visibilidade | Razão |
|---|---|---|
| `produtos-web` | **público** | A vitrine serve as derivadas de tela por URL direta |
| `produtos-originais` | **privado** | A derivada de impressão não pode ser acessível publicamente (RN-12) |

Errar a visibilidade do bucket privado expõe a derivada de impressão — é o que o CA-28 existe
para impedir. Confira depois de criar:

```bash
# Deve responder erro de autorização, e não o arquivo
curl -s -o /dev/null -w '%{http_code}\n' \
  "$SUPABASE_URL/storage/v1/object/public/produtos-originais/qualquer-nome"
```

Envio dos arquivos:

```bash
cd /caminho/da/copia/arquivos

for bucket in produtos-web produtos-originais; do
  find "$bucket" -type f | while IFS= read -r arquivo; do
    nome="${arquivo#"$bucket"/}"

    curl --silent --show-error --fail \
      --request POST \
      --header "Authorization: Bearer $SUPABASE_KEY" \
      --header "apikey: $SUPABASE_KEY" \
      --header "Content-Type: application/octet-stream" \
      --data-binary "@$arquivo" \
      "$SUPABASE_URL/storage/v1/object/$bucket/$nome" > /dev/null

    echo "enviado $bucket/$nome"
  done
done
```

Depois, abra a vitrine e confirme que **as fotos aparecem**. Produto sem foto na vitrine
significa arquivo faltando, não banco errado.

---

## 5. Registro da restauração executada

> ⚠️ **Não executado.** Esta seção precisa ser preenchida com uma restauração real, em ambiente
> limpo, com o sistema funcionando ao final. Até lá, o critério de T-27 que exige a execução
> **não está cumprido**, e o procedimento acima é hipótese fundamentada, não fato verificado.

Quando acontecer, registre aqui:

| Campo | Valor |
|---|---|
| Data da execução | |
| Cópia utilizada | |
| Destino | |
| Tempo total | |
| O que não funcionou de primeira | |
| Correções feitas neste documento | |

O campo "o que não funcionou de primeira" é o mais útil dos seis: é ele que transforma este
documento de roteiro otimista em procedimento confiável.

---

## 6. Redefinir a senha do dono

O sistema **não tem recuperação de senha** (RN-59, ADR-006): não há e-mail, pergunta secreta
nem página de redefinição. É decisão, não esquecimento — um único usuário e nenhum canal de
recuperação significam uma superfície de ataque a menos.

A consequência é esta: perder a senha exige acesso à configuração da aplicação.

### Caminho normal

A senha vem da configuração `OwnerAccount:Password`, e a conta é semeada na subida.

1. No painel do Render, altere a variável de ambiente `OwnerAccount__Password`
2. Reinicie o serviço
3. Entre com a senha nova

**A troca pela tela de Configurações tem precedência sobre a variável.** Se a senha foi trocada
no sistema, alterar a variável não a substitui — siga o caminho abaixo.

### Quando a senha foi trocada pelo painel

Apague a conta do dono e deixe a semeadura recriá-la a partir da configuração:

```bash
psql "$DATABASE_URL" -c 'delete from "AspNetUsers";'
```

Depois reinicie o serviço. A conta é recriada com a senha da variável de ambiente.

**Isso não apaga nada do acervo** — categorias, produtos, catálogos e configuração do portal são
outras tabelas. A conta é a única coisa que se perde, e ela é recriada.

### Registro do teste deste procedimento

> ⚠️ **Não executado.** O critério de T-27 exige que a redefinição seja **testada**.

| Campo | Valor |
|---|---|
| Data do teste | |
| Caminho usado | |
| Funcionou de primeira | |

---

## 7. O que não está coberto

Dito explicitamente, para ninguém supor cobertura que não existe:

- **Agendamento.** O script não se agenda: alguém precisa configurar o disparo periódico fora
  das duas plataformas, e isso não está feito
- **Destino da cópia.** `BACKUP_DESTINO` aponta para um diretório local; mandar para um terceiro
  serviço (armazenamento de outro provedor, disco externo) é passo manual
- **Verificação automática da cópia.** O manifesto ajuda a conferir a olho; não há rotina que
  valide que o dump restaura
- **Retenção do provedor.** O plano gratuito do Supabase não garante retenção de backup. Este
  procedimento existe justamente por isso
