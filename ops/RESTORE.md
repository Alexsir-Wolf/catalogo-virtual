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

**Se `psql` e `pg_restore` não estiverem instalados** — o caso de uma máquina de desenvolvimento
Windows comum —, rode-os de dentro da imagem oficial, que já os traz. O ensaio da seção 5 foi
feito assim:

```bash
docker run --rm -i postgres:17-alpine \
  pg_restore --no-owner --no-privileges --dbname="$DATABASE_URL" < /caminho/da/copia/banco.dump
```

Para conferir contagens, o mesmo caminho com `psql "$DATABASE_URL" -c '...'`.

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

Confira que o dump trouxe o histórico de migrações — sem ele a aplicação tentaria aplicar todas
de novo sobre um esquema que já existe:

```bash
psql "$DATABASE_URL" -tAc 'select count(*) from "__EFMigrationsHistory";'
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

> ✅ **A metade do banco foi executada** — seção 3 inteira, em banco de destino vazio, com o
> sistema funcionando ao final.
>
> ⚠️ **A metade dos arquivos (seção 4) não foi executada** e continua sendo hipótese: ela exige
> credencial de um Supabase de destino, que o ensaio não tinha. O critério de T-27 fica cumprido
> **pela metade**, e é assim que deve ser lido.

| Campo | Valor |
|---|---|
| Data da execução | 2026-09-30 |
| Cópia utilizada | Dump gerado no ensaio com `pg_dump --format=custom --no-owner --no-privileges` — 3 categorias, 12 produtos No ar, 1 catálogo com critério de 2 categorias, 10 migrações aplicadas |
| Destino | PostgreSQL 17 em contêiner limpo, banco recém-criado, **sem** migrations aplicadas antes |
| Tempo total | Cerca de 20 minutos, a maior parte gasta nos dois defeitos abaixo |
| O que não funcionou de primeira | **Duas coisas, e as duas eram erro deste documento ou do ambiente.** (1) A seção 6 mandava alterar `OwnerAccount__Password`; **a seção de configuração é `Owner`**, então a variável é `Owner__Password`. Com o nome errado a aplicação sobe, registra `Conta do dono não semeada` como aviso e o painel fica inacessível — sem erro visível na tela, só um `warn` no log. (2) A conferência do destino vazio e a restauração precisam de `psql`/`pg_restore`, que **não** estão no `PATH` de uma máquina de desenvolvimento Windows comum; o ensaio os obteve de dentro da imagem `postgres:17-alpine`, e o documento não dizia como. |
| Correções feitas neste documento | Nome da variável corrigido na seção 6; acrescentada a nota sobre `MustChangePassword`; acrescentada a alternativa de rodar as ferramentas por contêiner na seção 2; acrescentado à seção 3 o passo de conferir a versão restaurada em `__EFMigrationsHistory` |

**O que o ensaio provou, na ordem em que foi feito:**

1. `pg_restore` num banco de destino vazio traz esquema e dados — as contagens do destino conferiram com as da origem, incluindo a tabela de junção do critério
2. `__EFMigrationsHistory` veio no dump com as 10 migrações, e **a aplicação subiu sem aplicar nenhuma**, que é o comportamento que a seção 3 promete
3. A vitrine pública respondeu `200` com os 12 produtos listados
4. O painel autenticou com a senha da configuração e listou produtos e o catálogo salvo
5. `/health` respondeu `healthy` com a versão do banco restaurado
6. O caminho de redefinição de senha da seção 6 funcionou: apagar `AspNetUsers` **não tocou** o acervo (3 categorias e 12 produtos intactos), e a conta foi recriada na subida seguinte

**O que o ensaio não provou, e portanto não está provado:** nada da seção 4. Os buckets, a
visibilidade de cada um e o envio dos arquivos seguem sem execução, e é exatamente a metade cuja
ausência produz "catálogo cujos produtos existem e cujas imagens não abrem".

---

## 6. Redefinir a senha do dono

O sistema **não tem recuperação de senha** (RN-59, ADR-006): não há e-mail, pergunta secreta
nem página de redefinição. É decisão, não esquecimento — um único usuário e nenhum canal de
recuperação significam uma superfície de ataque a menos.

A consequência é esta: perder a senha exige acesso à configuração da aplicação.

### Caminho normal

A senha vem da configuração `OwnerAccount:Password`, e a conta é semeada na subida.

1. No painel do Render, altere a variável de ambiente `Owner__Password`
2. Reinicie o serviço
3. Entre com a senha nova

> A seção de configuração chama-se `Owner`, não `OwnerAccount`. Com o nome errado a aplicação
> **sobe normalmente** e apenas registra `Conta do dono não semeada` como aviso: o painel fica
> inacessível sem nenhum erro na tela. Foi o primeiro tropeço do ensaio da seção 5.

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

**A conta recriada nasce com troca de senha pendente.** O primeiro acesso exige definir uma senha
nova, e é por isso que a senha da variável de ambiente não precisa ser a definitiva.

### Registro do teste deste procedimento

> ✅ **Executado em 2026-09-30**, no mesmo ensaio da seção 5.

| Campo | Valor |
|---|---|
| Data do teste | 2026-09-30 |
| Caminho usado | O de baixo — `delete from "AspNetUsers"` e reinício, que é o caminho para quando a senha foi trocada pelo painel |
| Funcionou de primeira | Não. A variável estava documentada como `OwnerAccount__Password` e a seção de configuração é `Owner`; corrigido acima. Com o nome certo, funcionou: a conta foi recriada, o acervo ficou intacto (3 categorias e 12 produtos antes e depois) e o login com a senha da variável entrou no painel |

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
