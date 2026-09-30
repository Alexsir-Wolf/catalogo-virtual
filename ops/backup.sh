#!/usr/bin/env bash
#
# Backup do catálogo virtual: banco e arquivos.
#
# POR QUE ESTE ARQUIVO EXISTE FORA DAS PLATAFORMAS
#
# Com a mudança para serviços gerenciados (ADR-018) não há mais servidor onde agendar uma
# tarefa: o Render reinicia o container e o disco é efêmero, e o Supabase não executa scripts
# do projeto. A rotina precisa rodar **num terceiro lugar** — a máquina do dono, um runner de
# CI agendado, ou qualquer host com acesso à internet e ao destino da cópia.
#
# E o destino precisa ser independente das duas plataformas. Guardar o backup do Supabase no
# próprio Supabase não é backup: é a mesma falha, duas vezes. O plano gratuito também **não
# garante retenção** — depender do que a plataforma guarda é confiar em algo que não foi
# contratado.
#
# USO
#
#   BACKUP_DESTINO=/caminho/para/copias ./ops/backup.sh
#
# Variáveis obrigatórias, lidas do ambiente e **nunca** do repositório:
#
#   DATABASE_URL        cadeia de conexão do Postgres (Supabase, pooler de sessão)
#   SUPABASE_URL        endereço do projeto Supabase
#   SUPABASE_KEY        chave de serviço, com permissão de leitura nos buckets
#   BACKUP_DESTINO      diretório onde as cópias são gravadas
#
# Opcional:
#
#   BACKUP_RETENCAO     quantas cópias manter (padrão: 14)

set -Eeuo pipefail

readonly BUCKETS=("produtos-web" "produtos-print")
readonly RETENCAO="${BACKUP_RETENCAO:-14}"
readonly AGORA="$(date -u +%Y%m%dT%H%M%SZ)"

falhar() {
    echo "ERRO: $*" >&2
    exit 1
}

exigir() {
    local nome="$1"
    [[ -n "${!nome:-}" ]] || falhar "a variável $nome não está definida."
}

exigir DATABASE_URL
exigir SUPABASE_URL
exigir SUPABASE_KEY
exigir BACKUP_DESTINO

command -v pg_dump >/dev/null || falhar "pg_dump não encontrado no PATH."
command -v curl >/dev/null || falhar "curl não encontrado no PATH."

readonly PASTA="$BACKUP_DESTINO/$AGORA"
mkdir -p "$PASTA"

echo "==> Backup em $PASTA"

# O banco. Formato custom (-Fc) porque permite restauração seletiva e comprime; o dump é a
# fonte de verdade da restauração descrita em RESTORE.md.
echo "--> Banco de dados"
pg_dump --format=custom --no-owner --no-privileges --file="$PASTA/banco.dump" "$DATABASE_URL"

# Os arquivos. As derivadas de imagem e a capa do PDF vivem no armazenamento de objeto
# (ADR-018) e **não** estão no dump: sem eles, um banco restaurado exibe produto sem foto.
echo "--> Arquivos do armazenamento"
for bucket in "${BUCKETS[@]}"; do
    echo "    bucket $bucket"
    mkdir -p "$PASTA/arquivos/$bucket"

    # A listagem vem paginada; 1000 por página cobre o volume previsto (centenas de produtos,
    # cinco derivadas cada) com folga, e o laço para quando a página volta vazia.
    pagina=0
    while :; do
        resposta="$(curl --silent --show-error --fail \
            --header "Authorization: Bearer $SUPABASE_KEY" \
            --header "apikey: $SUPABASE_KEY" \
            --header "Content-Type: application/json" \
            --data "{\"prefix\":\"\",\"limit\":1000,\"offset\":$((pagina * 1000))}" \
            "$SUPABASE_URL/storage/v1/object/list/$bucket")"

        nomes="$(echo "$resposta" | grep -oE '"name":"[^"]+"' | sed 's/"name":"//;s/"$//' || true)"
        [[ -n "$nomes" ]] || break

        while IFS= read -r nome; do
            [[ -n "$nome" ]] || continue

            destino="$PASTA/arquivos/$bucket/$nome"
            mkdir -p "$(dirname "$destino")"

            curl --silent --show-error --fail \
                --header "Authorization: Bearer $SUPABASE_KEY" \
                --header "apikey: $SUPABASE_KEY" \
                --output "$destino" \
                "$SUPABASE_URL/storage/v1/object/$bucket/$nome"
        done <<< "$nomes"

        pagina=$((pagina + 1))
    done
done

# Um manifesto do que foi copiado. Serve para conferir, na hora da restauração, que a cópia
# está completa antes de começar — descobrir arquivo faltando no meio da restauração é pior.
{
    echo "backup: $AGORA"
    echo "banco: $(du -h "$PASTA/banco.dump" | cut -f1)"
    for bucket in "${BUCKETS[@]}"; do
        echo "bucket $bucket: $(find "$PASTA/arquivos/$bucket" -type f | wc -l) arquivos"
    done
} > "$PASTA/MANIFESTO.txt"

cat "$PASTA/MANIFESTO.txt"

# Retenção. Uma cópia por execução cresce sem limite; manter as N mais recentes é o suficiente
# para um catálogo escrito poucas vezes por semana.
echo "--> Mantendo as $RETENCAO cópias mais recentes"
find "$BACKUP_DESTINO" -maxdepth 1 -mindepth 1 -type d -name '20*' \
    | sort -r \
    | tail -n "+$((RETENCAO + 1))" \
    | while IFS= read -r antiga; do
        echo "    removendo $antiga"
        rm -rf "$antiga"
    done

echo "==> Backup concluído"
