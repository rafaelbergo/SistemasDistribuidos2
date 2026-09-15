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

## Consumidores de promoções

Os dois processos recebem notificações exclusivamente pelo RabbitMQ, sem chamar outros microsserviços. Cada um declara sua própria fila no exchange Topic `Promoções`:

| Processo | Fila | Routing keys |
| --- | --- | --- |
| Consumidor.C1 | `fila_promocoes_c1` | `promocao.categoria.A` e `promocao.categoria.B` |
| Consumidor.C2 | `fila_promocoes_c2` | `promocao.categoria.*` (todas as categorias) |

Com o RabbitMQ em execução, abra três terminais na pasta da solução e execute um comando em cada terminal:

```powershell
dotnet run --project MS.Promocoes
dotnet run --project Consumidor.C1
dotnet run --project Consumidor.C2
```

Inicie o MS.Promocoes atualizado para distribuir sua chave pública às pastas `Keys` dos consumidores. Aguarde ambos exibirem `Aguardando promoções` antes de publicar pelo menu do MS.Promocoes. Promoções A e B aparecem nos dois consumidores; promoções C aparecem somente no C2. O C2 também recebe categorias novas sem alteração no código.

Os consumidores exibem produto, categoria, desconto e descrição após validar a assinatura localmente. Mensagens inválidas são descartadas; mensagens válidas são confirmadas após a exibição. Pressione Enter no terminal de cada consumidor para encerrá-lo. As filas permanecem no RabbitMQ após o encerramento.
