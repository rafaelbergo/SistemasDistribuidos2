# SistemasDistribuidos2

## RabbitMQ com Docker

O RabbitMQ roda em um contêiner; os projetos C# continuam sendo executados no Visual Studio.
Não é necessário instalar Erlang ou RabbitMQ diretamente no Windows.

### Preparar o Windows

1. Instale o [Docker Desktop para Windows](https://docs.docker.com/desktop/setup/install/windows-install/) com o backend WSL 2 e contêineres Linux.
2. Se o WSL não estiver instalado, execute `wsl --install --no-distribution` em um PowerShell como administrador. Reinicie o Windows se solicitado.
3. Abra o Docker Desktop e aguarde o mecanismo Docker iniciar. Reabra o terminal após a instalação.

### Executar

No terminal, na pasta desta solução:

```powershell
docker compose up -d --wait --wait-timeout 180
docker compose ps
```

Após o RabbitMQ aparecer como `healthy`, execute os projetos no Visual Studio (F5).
O código atual usa `localhost:5672` e as credenciais padrão `guest` / `guest`, compatíveis com este Compose.
As portas publicadas ficam acessíveis apenas neste computador; esta configuração é para desenvolvimento local.

Painel de administração: [http://localhost:15672](http://localhost:15672), usuário `guest`, senha `guest`.
A porta `15672` é do painel; os programas se conectam à porta `5672`.

### Diagnóstico e parada

```powershell
docker compose logs --tail 100 rabbitmq
Test-NetConnection 127.0.0.1 -Port 5672
docker compose stop
```

Se o comando `docker` não for encontrado, conclua a instalação do Docker Desktop e reabra o terminal.
Se não conseguir conectar ao mecanismo Docker, abra o Docker Desktop e aguarde sua inicialização.
Se uma porta já estiver ocupada por outro RabbitMQ, pare essa outra instância antes de iniciar o Compose.

Os dados do RabbitMQ ficam no volume `rabbitmq_data` e são preservados ao parar ou recriar o contêiner.
