#!/bin/bash

# ============================================================
# SistemasDistribuidos2 - Inicialização dos Microserviços
# ============================================================

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

SERVICES=(
    "MS.Estoque"
    "MS.Pagamento"
    "MS.Promocoes"
    "MS.Entrega"
    "MS.Principal"
)

# Função para encerrar processos e janelas de terminais abertos anteriormente
encerrar_instancias_anteriores() {
    echo "=========================================="
    echo " Verificando instâncias anteriores..."
    echo "=========================================="

    local PIDS=""
    for SERVICE in "${SERVICES[@]}"; do
        local SERVICE_PIDS
        SERVICE_PIDS=$(pgrep -f "$PROJECT_DIR/$SERVICE" 2>/dev/null | grep -v -E "^($$|$PPID)$" || true)
        if [ -n "$SERVICE_PIDS" ]; then
            PIDS="$PIDS $SERVICE_PIDS"
        fi

        local BIN_PIDS
        BIN_PIDS=$(pgrep -x "$SERVICE" 2>/dev/null | grep -v -E "^($$|$PPID)$" || true)
        if [ -n "$BIN_PIDS" ]; then
            PIDS="$PIDS $BIN_PIDS"
        fi
    done

    # Remove duplicatas e formata lista
    PIDS=$(echo "$PIDS" | tr ' ' '\n' | sort -u | tr '\n' ' ' | xargs)

    if [ -n "$PIDS" ]; then
        echo "Fechando processos e terminais abertos: $PIDS"
        kill -TERM $PIDS 2>/dev/null || true
        sleep 1

        # Verifica se algum processo ainda permaneceu ativo e força encerramento
        local REMAINING=""
        for PID in $PIDS; do
            if kill -0 "$PID" 2>/dev/null; then
                REMAINING="$REMAINING $PID"
            fi
        done

        if [ -n "$REMAINING" ]; then
            kill -9 $REMAINING 2>/dev/null || true
        fi
        echo "Instâncias anteriores finalizadas."
    else
        echo "Nenhuma instância anterior em execução."
    fi
    echo
}

# Se chamado com argumento de parada (--stop, --kill, etc.) apenas encerra
if [ "$1" = "--stop" ] || [ "$1" = "-s" ] || [ "$1" = "--kill" ] || [ "$1" = "-k" ]; then
    encerrar_instancias_anteriores
    exit 0
fi

# Fecha instâncias antigas antes de iniciar as novas
encerrar_instancias_anteriores

# Verifica se o diretório existe
if [ ! -d "$PROJECT_DIR" ]; then
    echo "ERRO: Diretório do projeto não encontrado:"
    echo "$PROJECT_DIR"
    exit 1
fi

# Verifica se o .NET está instalado
if ! command -v dotnet &> /dev/null; then
    echo "ERRO: .NET SDK não encontrado."
    exit 1
fi

# Verifica se a solution existe
if [ ! -f "$PROJECT_DIR/SistemasDistribuidos2.slnx" ]; then
    echo "ERRO: SistemasDistribuidos2.slnx não encontrado."
    exit 1
fi

# Verifica se o RabbitMQ está ouvindo na porta 5672
if ! timeout 1 bash -c 'cat < /dev/null > /dev/tcp/localhost/5672' 2>/dev/null; then
    echo "=========================================="
    echo "AVISO: RabbitMQ não detectado na porta 5672."
    echo "Certifique-se de que o container do RabbitMQ está rodando:"
    echo "docker start rabbitmq || docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:4-management"
    echo "=========================================="
    echo
fi

# Detecta emulador de terminal disponível
TERMINAL_CMD=""
if command -v gnome-terminal &> /dev/null; then
    TERMINAL_CMD="gnome-terminal"
elif command -v x-terminal-emulator &> /dev/null; then
    TERMINAL_CMD="x-terminal-emulator"
else
    echo "ERRO: Nenhum emulador de terminal gráfico encontrado (gnome-terminal ou x-terminal-emulator)."
    echo "Instale com:"
    echo "sudo apt install gnome-terminal"
    exit 1
fi

echo "=========================================="
echo " Sistemas Distribuídos 2"
echo " Iniciando microserviços..."
echo "=========================================="
echo

cd "$PROJECT_DIR" || exit 1

for SERVICE in "${SERVICES[@]}"; do

    PROJECT="$PROJECT_DIR/$SERVICE"

    if [ ! -d "$PROJECT" ]; then
        echo "AVISO: Projeto não encontrado: $SERVICE"
        continue
    fi

    echo "Iniciando: $SERVICE"

    if [ "$TERMINAL_CMD" = "gnome-terminal" ]; then
        gnome-terminal \
            --title="$SERVICE" \
            -- bash -c '
                PROJECT="$1"
                SERVICE="$2"
                cd "$PROJECT" || exit 1

                echo "=========================================="
                echo " $SERVICE"
                echo "=========================================="
                echo
                echo "Diretório: $(pwd)"
                echo "Iniciando aplicação..."
                echo

                dotnet run
                EXIT_CODE=$?

                echo
                echo "=========================================="
                echo " $SERVICE finalizado (Código de saída: $EXIT_CODE)"
                echo "=========================================="
                echo
                read -p "Pressione ENTER para fechar..."
            ' _ "$PROJECT" "$SERVICE"
    else
        x-terminal-emulator -T "$SERVICE" -e bash -c '
            PROJECT="$1"
            SERVICE="$2"
            cd "$PROJECT" || exit 1

            echo "=========================================="
            echo " $SERVICE"
            echo "=========================================="
            echo
            echo "Diretório: $(pwd)"
            echo "Iniciando aplicação..."
            echo

            dotnet run
            EXIT_CODE=$?

            echo
            echo "=========================================="
            echo " $SERVICE finalizado (Código de saída: $EXIT_CODE)"
            echo "=========================================="
            echo
            read -p "Pressione ENTER para fechar..."
        ' _ "$PROJECT" "$SERVICE"
    fi

    sleep 1
done

echo
echo "=========================================="
echo " Todos os microserviços foram iniciados."
echo "=========================================="
