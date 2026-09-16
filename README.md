# SistemasDistribuidos2

## CRUD de pedidos no MS.Principal

O Principal salva os pedidos, status, histórico e publicações pendentes em `MS.Principal/Data/pedidos.json`. Os dados são carregados ao reiniciar e não são versionados no Git. Execute apenas uma instância do Principal por arquivo; um bloqueio impede gravações concorrentes entre processos. Para usar outro arquivo, configure a variável `MS_PRINCIPAL_DATA_PATH`.

| Opção | Operação |
| --- | --- |
| 1 | Criar e salvar um rascunho; permite enviá-lo imediatamente ao processamento |
| 2 | Listar pedidos, cliente, status e indicação de publicação pendente |
| 3 | Consultar itens, datas e histórico pelo ID completo do pedido |
| 4 | Editar cliente e itens de um rascunho |
| 5 | Excluir da listagem um rascunho ou pedido concluído |
| 6 | Enviar um rascunho salvo para processamento |
| 7 | Listar registros excluídos |
| 0 | Sair |

Na criação, informe o cliente e os itens com quantidades positivas. Enter no campo do ID do item finaliza a lista; `/cancelar` abandona a edição sem salvar. Para testar o CRUD, crie um rascunho sem enviá-lo, consulte e edite pelo ID exibido. Reinicie o Principal para verificar que as alterações continuam salvas.

Após o envio, os dados do pedido ficam bloqueados para edição, pois os outros serviços já podem ter reservado estoque ou processado o pagamento. Os eventos `pedido.estoque_ok`, `estoque.indisponivel`, `pagamento.aprovado`, `pagamento.recusado` e `pedido.enviado` atualizam o status e o histórico automaticamente, após a validação da assinatura. Eventos duplicados não repetem a atualização; eventos atrasados não fazem o status regredir nem sobrescrevem os itens.

A exclusão é lógica: remove o pedido da listagem normal, preservando o registro e o histórico. É permitida para rascunhos e pedidos nos estados `pagamento.recusado`, `estoque.indisponivel` ou `pedido.enviado`. Excluir um registro concluído não publica cancelamento nem devolve estoque novamente. Pedidos em processamento precisam concluir o fluxo antes de serem excluídos; eventos tardios não recriam registros excluídos.

O pedido e sua publicação pendente são salvos juntos antes do envio de `pedido.criado`. O Principal tenta publicar a cada três segundos e remove a pendência após a confirmação do RabbitMQ; pendências sobrevivem a reinícios. Inicie o MS.Estoque para criar a fila que recebe o pedido. Se a aplicação cair entre a confirmação e a gravação local, o evento pode ser reenviado com o mesmo ID. As confirmações de consumo dos eventos de retorno ocorrem somente após a gravação em disco.

Essa persistência pertence ao Principal; não altera o armazenamento em memória dos demais serviços nem recupera pedidos que existiam apenas em memória em versões anteriores.

### Testes do armazenamento e ciclo de vida

```powershell
dotnet run --project Tests/MS.Principal.Tests -c Release
```

Os testes usam arquivos temporários e cobrem CRUD, reinício, eventos, duplicatas, concorrência e falhas de gravação, sem precisar de RabbitMQ.

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
