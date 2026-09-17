# Documentação Técnica

## 1. Objetivo

Este projeto implementa um backend distribuído de e-commerce para a disciplina de Sistemas Distribuídos da UTFPR.

O sistema utiliza:

- microsserviços independentes;
- comunicação assíncrona por eventos;
- RabbitMQ como broker de mensagens;
- exchanges `direct` e `topic`;
- assinatura digital RSA para autenticação e integridade dos eventos;
- arquivo INI para persistência local do estoque.

A regra arquitetural principal é que os processos não realizam chamadas diretas entre si. A comunicação acontece exclusivamente por meio de eventos publicados no RabbitMQ.

## 2. Estrutura do repositório

```text
.
├── Foundation/
│   ├── Keys/KeyManagement.cs
│   ├── Models/PedidoCriado.cs
│   ├── Models/CatalogoProdutos.cs
│   ├── Models/Promocao.cs
│   ├── Message.cs
│   └── SignatureService.cs
├── MS.Principal/
│   ├── ConsultaProdutosClient.cs
│   ├── PedidoRepository.cs
│   └── Program.cs
├── MS.Estoque/
│   ├── Estoque.ini
│   └── Program.cs
├── MS.Pagamento/
│   └── Program.cs
├── MS.Entrega/
│   └── Program.cs
├── MS.Promocoes/
│   └── Program.cs
├── start-services.sh
├── stop-services.sh
├── SistemasDistribuidos2.slnx
└── README.md
```

### 6.4. Consulta e catálogo de produtos

Arquivo:

```text
Foundation/Models/CatalogoProdutos.cs
```

Solicitação:

```csharp
public class ConsultaProdutos
{
    public string Id { get; set; }
}
```

Resposta:

```csharp
public class CatalogoProdutos
{
    public string ConsultaId { get; set; }
    public List<ProdutoDisponivel> Produtos { get; set; }
}
```

Cada produto possui:

```csharp
public class ProdutoDisponivel
{
    public string Id { get; set; }
    public string Descricao { get; set; }
    public int QuantidadeDisponivel { get; set; }
}
```

Os diretórios `bin/` e `obj/` contêm artefatos gerados pelo .NET e não fazem parte da implementação da aplicação.

## 3. Componentes

### 3.1. Foundation

`Foundation` é uma biblioteca compartilhada pelos microsserviços. Ela concentra os contratos e os recursos comuns:

- envelope genérico de mensagens;
- modelos de pedidos e promoções;
- assinatura e verificação RSA;
- geração e distribuição de chaves públicas.

Os microsserviços referenciam esse projeto por meio de `ProjectReference`.

### 3.2. MS.Principal

É a interface de terminal do sistema.

Responsabilidades atuais:

- exibir o menu principal;
- consultar produtos e quantidades disponíveis por evento;
- coletar os itens e quantidades de um pedido;
- impedir itens duplicados no mesmo pedido;
- validar quantidades maiores que zero;
- assinar e publicar novos pedidos.

Evento publicado:

```text
Exchange: eCommerce
Routing key: pedido.criado
```

O menu também permite listar pedidos, consultar o histórico e reenviar rascunhos pendentes.

#### Consulta de produtos

A opção **4. Visualizar produtos** não acessa diretamente o arquivo do Estoque. Ela utiliza uma solicitação assíncrona:

1. O Principal cria um identificador único para a consulta.
2. Cria uma fila temporária, exclusiva e com remoção automática.
3. Vincula essa fila à routing key `produtos.listados.<id-da-consulta>`.
4. Publica uma solicitação assinada com a routing key `produtos.consultar`.
5. Aguarda uma resposta válida por até cinco segundos.
6. Exibe o ID, a descrição e a quantidade disponível de cada produto.

Se o Estoque estiver parado ou não responder, o Principal informa a indisponibilidade e retorna ao menu.

### 3.3. MS.Estoque

Gerencia o estoque local dos produtos.

Responsabilidades:

- carregar o estoque do arquivo `Estoque.ini`;
- validar o conteúdo do arquivo;
- verificar disponibilidade dos itens;
- reservar quantidades;
- devolver quantidades de pedidos excluídos;
- persistir as alterações;
- validar assinaturas dos eventos recebidos.

Eventos consumidos:

```text
pedido.criado
pedido.excluido
produtos.consultar
```

Eventos publicados:

```text
pedido.estoque_ok
estoque.indisponivel
produtos.listados.<id-da-consulta>
```

Ao receber `produtos.consultar`, o Estoque valida a assinatura do Principal, cria uma fotografia do saldo atual e devolve um `CatalogoProdutos` assinado. A consulta não reserva produtos; a disponibilidade é verificada novamente quando o pedido `pedido.criado` for processado.

### 3.4. MS.Pagamento

Simula o processamento de pagamento.

Evento consumido:

```text
pedido.estoque_ok
```

Eventos publicados:

```text
pagamento.aprovado
pagamento.recusado
```

O resultado é determinado aleatoriamente. A configuração atual utiliza uma chance nominal de aprovação de 70%.

### 3.5. MS.Entrega

Simula a etapa de emissão da nota fiscal e preparação da entrega.

Evento consumido:

```text
pagamento.aprovado
```

Evento publicado:

```text
pedido.enviado
```

O processamento inclui um atraso de um segundo para simular uma operação real.

### 3.6. MS.Promocoes

Gera promoções aleatórias para produtos das categorias A, B e C.

Eventos publicados:

```text
promocao.categoria.A
promocao.categoria.B
promocao.categoria.C
```

O serviço possui opções para publicar uma promoção ou um lote de promoções.

### 3.7. Consumidores de promoções

O enunciado prevê dois processos:

| Consumidor | Fila | Interesse |
|---|---|---|
| C1 | `fila_promocoes_c1` | Categorias A e B |
| C2 | `fila_promocoes_c2` | Todas as categorias |

Os projetos `Consumidor.Promocoes.C1` e `Consumidor.Promocoes.C2` estão referenciados pela solução, mas não estão presentes no workspace atual. Portanto, essa parte ainda precisa ser criada para que o fluxo de promoções fique completo.

## 4. RabbitMQ

### 4.1. Exchange `eCommerce`

Tipo:

```text
direct
```

É utilizada para o ciclo de vida dos pedidos.

| Routing key | Produtor | Consumidor esperado |
|---|---|---|
| `pedido.criado` | `MS.Principal` | `MS.Estoque` |
| `pedido.estoque_ok` | `MS.Estoque` | `MS.Pagamento` |
| `estoque.indisponivel` | `MS.Estoque` | `MS.Principal` |
| `pagamento.aprovado` | `MS.Pagamento` | `MS.Entrega` e `MS.Principal` |
| `pagamento.recusado` | `MS.Pagamento` | `MS.Principal` |
| `pedido.enviado` | `MS.Entrega` | `MS.Principal` |
| `pedido.excluido` | `MS.Principal` | `MS.Estoque` |
| `produtos.consultar` | `MS.Principal` | `MS.Estoque` |
| `produtos.listados.<id-da-consulta>` | `MS.Estoque` | fila temporária do `MS.Principal` |

Como a exchange é `direct`, a routing key precisa coincidir com o binding da fila.

### 4.2. Exchange `Promoções`

Tipo:

```text
topic
```

É utilizada para distribuir promoções por categoria.

Bindings esperados:

```text
fila_promocoes_c1 -> promocao.categoria.A
fila_promocoes_c1 -> promocao.categoria.B
fila_promocoes_c2 -> promocao.categoria.#
```

O padrão `#` permite que C2 receba eventos de qualquer categoria abaixo de `promocao.categoria`.

Cada consumidor deve ter uma fila própria. Assim, C1 e C2 recebem cópias independentes do mesmo evento quando ambos possuem um binding compatível.

### 4.3. Filas do ciclo de pedidos

As filas criadas pelos serviços implementados são:

```text
fila_estoque
fila_pagamento
fila_entrega
```

Bindings:

```text
fila_estoque:
  pedido.criado
  pedido.excluido
  produtos.consultar

fila_pagamento:
  pedido.estoque_ok

fila_entrega:
  pagamento.aprovado
```

O `MS.Principal` ainda precisa criar uma fila própria para consumir os eventos de atualização do pedido.

Para consultas de produtos, o Principal cria uma fila temporária exclusiva por solicitação. Essa fila recebe somente a resposta cujo identificador corresponde à consulta realizada.

## 5. Fluxo de processamento de um pedido

### 5.1. Criação

O usuário acessa o menu do `MS.Principal` e escolhe criar um pedido.

Para cada item, o serviço solicita:

```text
ID do item
Quantidade
```

Depois de finalizar o pedido:

1. o objeto `PedidoCriado` é serializado;
2. o JSON do conteúdo é assinado;
3. o conteúdo é colocado no envelope `Message<PedidoCriado>`;
4. a mensagem é publicada com `pedido.criado`.

### 5.2. Consulta de produtos

Antes de criar um pedido, o usuário pode escolher **Visualizar produtos**. O Principal publica:

```text
produtos.consultar
```

O Estoque responde com:

```text
produtos.listados.<id-da-consulta>
```

A resposta contém:

```json
{
  "consultaId": "identificador-da-consulta",
  "produtos": [
    {
      "id": "A",
      "descricao": "Produto A",
      "quantidadeDisponivel": 4
    }
  ]
}
```

O resultado é apenas uma fotografia do estoque. Entre a consulta e a criação do pedido, outro pedido pode consumir o mesmo produto. Por isso, o Estoque sempre faz uma nova validação ao receber `pedido.criado`.

### 5.3. Reserva de estoque

O `MS.Estoque` recebe o evento e:

1. desserializa a mensagem;
2. localiza a chave pública do produtor;
3. verifica a assinatura;
4. verifica todos os itens;
5. reserva as quantidades dentro de uma seção crítica;
6. salva o novo saldo;
7. publica o resultado.

Se todos os itens estiverem disponíveis:

```text
pedido.estoque_ok
```

Caso contrário:

```text
estoque.indisponivel
```

A validação de todos os itens ocorre antes de qualquer baixa. Assim, um pedido que não possa ser atendido não reduz parcialmente o estoque.

### 5.4. Pagamento

O `MS.Pagamento` recebe `pedido.estoque_ok`, valida a assinatura e sorteia o resultado:

```text
Random.Shared.Next(100) < 70
```

Em caso de aprovação, publica:

```text
pagamento.aprovado
```

Em caso de recusa, publica:

```text
pagamento.recusado
```

### 5.5. Entrega

O `MS.Entrega` consome `pagamento.aprovado`, valida a assinatura e simula:

1. emissão da nota;
2. preparação dos produtos;
3. envio do pedido.

Ao terminar, publica:

```text
pedido.enviado
```

### 5.6. Falhas e cancelamento

O fluxo planejado prevê que o `MS.Principal` consuma:

```text
estoque.indisponivel
pagamento.recusado
```

Quando necessário, ele deve publicar:

```text
pedido.excluido
```

O `MS.Estoque` então devolve os itens reservados.

Essa integração ainda não está implementada no `MS.Principal`.

## 6. Contratos de dados

### 6.1. Envelope `Message<T>`

Arquivo:

```text
Foundation/Message.cs
```

Estrutura:

```csharp
public class Message<T>
{
    public string MessageId { get; set; }
    public string Producer { get; set; }
    public T Content { get; set; }
    public string Signature { get; set; }
    public DateTime Timestamp { get; set; }
}
```

### 6.2. Pedido

Arquivo:

```text
Foundation/Models/PedidoCriado.cs
```

Exemplo:

```json
{
  "id": "pedido-123",
  "clienteId": "cliente-456",
  "itens": [
    {
      "id": "A",
      "quantidade": 2
    }
  ]
}
```

### 6.3. Promoção

Arquivo:

```text
Foundation/Models/Promocao.cs
```

Exemplo:

```json
{
  "id": "promocao-123",
  "categoria": "A",
  "produto": "Notebook Gamer",
  "descontoPercentual": 25,
  "descricao": "Oferta especial",
  "data": "2026-09-16T16:00:00Z"
}
```

## 7. Assinatura digital

### 7.1. Geração

Cada produtor possui uma chave privada própria. O conteúdo do evento é convertido para UTF-8 e assinado com:

```text
Algoritmo: RSA
Tamanho: 2048 bits
Hash: SHA-256
Padding: PKCS#1 v1.5
```

Somente o conteúdo (`Content`) é assinado.

### 7.2. Verificação

O consumidor usa o campo `Producer` para encontrar:

```text
Keys/{Producer}.public.pem
```

Depois, serializa novamente o conteúdo e verifica a assinatura.

Se a chave pública não existir ou a assinatura for inválida, o evento é descartado e a regra de negócio não é executada.

### 7.3. Gerenciamento das chaves

`KeyManagement`:

1. cria o diretório `Keys`;
2. gera um par RSA se necessário;
3. mantém a chave privada no projeto produtor;
4. distribui a chave pública para os demais projetos.

Chaves privadas não devem ser commitadas ou compartilhadas. Em um ambiente real, devem ser armazenadas em um mecanismo seguro de secrets.

## 8. Persistência do estoque

O estoque é armazenado em:

```text
MS.Estoque/Estoque.ini
```

Formato:

```ini
[Estoque]
A=8
B=5
C=0
D=1
```

Regras de validação:

- o arquivo precisa existir;
- a seção `[Estoque]` precisa existir;
- cada produto deve aparecer uma única vez;
- as quantidades devem ser inteiras;
- as quantidades não podem ser negativas.

Após uma reserva ou devolução, o arquivo é sobrescrito com o estado atual.

O acesso ao dicionário em memória é protegido por um `lock`, mas a persistência do arquivo e a publicação no RabbitMQ não formam uma transação única.

## 9. Execução local

### 9.1. Pré-requisitos

- .NET SDK 10;
- RabbitMQ;
- Docker, caso o RabbitMQ seja executado em container;
- terminal gráfico, caso o script de inicialização seja utilizado.

### 9.2. Iniciar RabbitMQ

```bash
docker run -d \
  --name rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  rabbitmq:4-management
```

Painel:

```text
http://localhost:15672
```

Credenciais padrão:

```text
Usuário: guest
Senha: guest
```

### 9.3. Executar os serviços

O script principal é:

```bash
./start-services.sh
```

Para encerrar:

```bash
./stop-services.sh
```

Também é possível executar cada projeto manualmente:

```bash
dotnet run --project MS.Estoque/MS.Estoque.csproj
dotnet run --project MS.Pagamento/MS.Pagamento.csproj
dotnet run --project MS.Entrega/MS.Entrega.csproj
dotnet run --project MS.Promocoes/MS.Promocoes.csproj
dotnet run --project MS.Principal/MS.Principal.csproj
```

### 9.4. Ordem recomendada

Inicie primeiro os serviços consumidores:

```text
MS.Estoque
MS.Pagamento
MS.Entrega
```

Depois inicie:

```text
MS.Promocoes
MS.Principal
```

Essa ordem reduz a chance de publicar eventos antes que as filas consumidoras estejam criadas.

## 10. Demonstração manual

### Pedido aprovado

1. Inicie RabbitMQ e os serviços.
2. No `MS.Principal`, escolha `1`.
3. Informe o produto `A`.
4. Informe uma quantidade disponível.
5. Finalize o pedido.
6. Observe os terminais:
   - Principal publica `pedido.criado`;
   - Estoque publica `pedido.estoque_ok`;
   - Pagamento publica `pagamento.aprovado` ou `pagamento.recusado`;
   - Entrega publica `pedido.enviado` quando o pagamento é aprovado.

### Estoque indisponível

Use o produto `C`, que atualmente começa com quantidade zero:

```ini
C=0
```

O Estoque deve publicar:

```text
estoque.indisponivel
```

### Promoções

No `MS.Promocoes`:

1. escolha a opção `1` para uma promoção;
2. ou escolha a opção `2` para um lote;
3. acompanhe a routing key gerada.

Quando C1 e C2 forem implementados, C1 deverá receber somente A e B, enquanto C2 deverá receber todas as categorias.

## 11. Compilação

Para compilar os projetos existentes individualmente:

```bash
dotnet build Foundation/Foundation.csproj
dotnet build MS.Principal/MS.Principal.csproj
dotnet build MS.Estoque/MS.Estoque.csproj
dotnet build MS.Pagamento/MS.Pagamento.csproj
dotnet build MS.Entrega/MS.Entrega.csproj
dotnet build MS.Promocoes/MS.Promocoes.csproj
```

A solução completa atualmente referencia:

```text
Consumidor.Promocoes.C1/Consumidor.Promocoes.C1.csproj
Consumidor.Promocoes.C2/Consumidor.Promocoes.C2.csproj
```

Como esses projetos não estão presentes no workspace atual, o build da solução falha até que eles sejam criados ou removidos das referências.

## 12. Estado atual e trabalho pendente

Implementado:

- exchanges `eCommerce` e `Promoções`;
- fluxo Estoque → Pagamento → Entrega;
- publicação de pedidos;
- consulta de produtos pelo Principal através de eventos;
- resposta do Estoque com catálogo e saldo atual;
- publicação de promoções;
- assinatura e validação dos eventos;
- geração e distribuição de chaves públicas;
- persistência local do estoque;
- validação do arquivo INI.

Pendente:

- criar C1 e C2;
- implementar o consumidor de eventos no Principal;
- armazenar pedidos e seus status;
- implementar listagem;
- implementar consulta;
- implementar exclusão;
- publicar `pedido.excluido` nos casos de falha;
- completar o build da solução;
- adicionar testes automatizados.

## 13. Riscos e melhorias futuras

### Confirmação de mensagens

Os consumidores usam `autoAck: true`. Isso confirma a mensagem automaticamente antes de garantir que o processamento terminou.

Uma versão mais robusta deveria utilizar:

```text
autoAck: false
```

e confirmar a mensagem somente após o processamento bem-sucedido.

### Idempotência

O campo `MessageId` está presente, mas não é utilizado. O sistema deveria registrar mensagens já processadas para evitar reservas, pagamentos ou entregas duplicadas.

### Consistência do estoque

A gravação do arquivo e a publicação do evento são operações separadas. Uma falha entre elas pode deixar o sistema inconsistente.

Uma implementação de produção deveria utilizar:

- banco transacional;
- outbox pattern;
- ou outro mecanismo de publicação confiável.

### Configuração

Host, exchange, filas e caminhos são definidos diretamente no código. Uma evolução deveria usar configuração por ambiente, por exemplo:

```text
appsettings.json
variáveis de ambiente
```

### Segurança das chaves

As chaves privadas devem permanecer somente no serviço produtor e fora do controle de versão. Em produção, o ideal é utilizar um secret manager ou serviço de gerenciamento de chaves.

## 14. Relação com o enunciado

O projeto atende ou encaminha os seguintes requisitos:

| Requisito | Situação |
|---|---|
| Exchange `direct` para e-commerce | Implementado |
| Exchange `topic` para promoções | Implementado |
| Comunicação indireta por RabbitMQ | Implementado nos serviços presentes |
| Microsserviço Principal | Parcialmente implementado |
| Microsserviço Estoque | Implementado |
| Microsserviço Pagamento | Implementado |
| Microsserviço Entrega | Implementado |
| Microsserviço Promoções | Implementado |
| Consumidor C1 | Ausente |
| Consumidor C2 | Ausente |
| Assinatura dos eventos | Implementada |
| Validação das assinaturas | Implementada |
| Consulta de produtos | Implementada por eventos |
| Consulta de pedidos e status | Implementada no Principal |
| Exclusão e devolução completa | Pendente |
