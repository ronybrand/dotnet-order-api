# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/). Este projeto não versiona
releases (`main` é deployado continuamente); as entradas abaixo agrupam o histórico por tema, não por tag.

## [Unreleased]

### Security

- `/health` fora do rate limit global e política `order-write` (30/min) nas escritas de `/orders`.
- `X-Forwarded-For` aceito apenas dos proxies em `ForwardedHeaders:KnownProxies`, para o rate limit
  particionar por cliente real atrás de proxy.
- `docker-compose` publica a API só em `127.0.0.1`; workflows com `permissions: contents: read`.
- RabbitMQ sem credencial padrão `guest/guest` e com TLS obrigatório (`RabbitMq:UseTls`); SMTP exige STARTTLS e
  nunca envia credenciais em texto claro. Validação no startup; `AllowInsecure` só em `appsettings.Development.json`.
- Auto-merge do Dependabot para atualizações patch e minor (major segue manual).

### Fixed

- `Customer` ganhou token de concorrência (`xmin`); criar um pedido reescreve a linha do cliente no mesmo `SaveChanges`,
  então excluir o cliente em paralelo falha com 409 em vez de deixar pedido ativo de cliente excluído. O `PUT` de cliente
  também deixa de perder atualizações concorrentes.
- Mensagens do RabbitMQ levam `message_id` e o worker ignora reentrega de um id já enviado por e-mail.
- `tax_id` persistido e comparado na forma canônica (maiúsculas, sem `.`/`-`/`/`); mínimo de 5 letras ou dígitos.
- Quantidade por item limitada a `FieldLimits.MaxQuantity` (10.000).
- Corpo acima do limite (413) e requisições malformadas do Kestrel não viram mais 500.
- Imagens base do Dockerfile e do compose fixadas por digest (o Dependabot mantém atualizadas).
- Regex de `tax_id`/`passport_number` usa `\z` em vez de `$`: `"12345678\n"` passava na validação e
  burlava o índice único.
- `POST /orders/{id}/items` agora respeita `MaxItemsPerOrder`, antes só aplicado na criação.
- Filtros de busca limitam o tamanho de cada valor (`MaxValueLength`), incluindo o termo de `lk`.

### Added

- Domínio `Customer`/`Order`/`Item` completo: entidades, services, validators, repositórios EF Core,
  controllers e migration inicial, a partir da especificação do `fastapi-order-api`.
- Autenticação JWT RS256 (chave pública local, validação de `aud`/`iss`), papéis `user`/`admin`,
  rate limiting, security headers, limite de body, correlation id e guard de CORS em produção.
- Motor de busca, filtro e paginação compartilhado, com campos resolvidos pela metadata do EF Core.
- Notificação assíncrona de mudança de status via RabbitMQ e `OrderApi.Worker` (e-mail HTML via SMTP,
  3 tentativas com backoff e DLQ).
- Mascaramento de dados sensíveis (`[Sensitive]`, `SensitiveMasker`, `SensitiveJsonModifier`).
- Testes unitários (services, validators, busca, mensageria, worker, middlewares e controllers) e de
  integração com Testcontainers, JWT real assinado e Postgres real.
- Pipeline de CI: format e analyzers como erro, testes com gate de cobertura de 80% (unit + integration
  mesclados), verificação de drift de migration, build das imagens Docker (publicadas no GHCR a partir da
  `main`), snapshot do OpenAPI publicado como página Redoc, CodeQL e Dependabot com auto-merge.
- ADRs, `DOMAIN.md`, `SECURITY.md` e `make verify` como gate local.

### Changed

- Analisadores em `AnalysisMode=Recommended` (regras de segurança e design como erro), equivalente ao `bandit`.
- Testes de contrato HTTP sem banco (autenticação maliciosa, campos desconhecidos, limites de busca) e projeto
  compartilhado `OrderApi.TestSupport`.
- README com diagramas Mermaid dos fluxos de requisição e de notificação; descrição, topics e homepage no GitHub.

- Migração para .NET 10 e para as versões estáveis mais novas das dependências (RabbitMQ.Client 7,
  Swashbuckle 10, Testcontainers 4).

- `FluentAssertions` substituído por `AwesomeAssertions` (fork com licença Apache 2.0): a partir da versão 8 o
  FluentAssertions passou a ter licença comercial.

- Actions dos workflows fixadas pelo SHA do commit (versão no comentário `# vN`), como no `fastapi-order-api`;
  o Dependabot continua atualizando os SHAs.

### Fixed

- Token JWT sem `sub` agora é rejeitado com 401 (antes caía numa identidade compartilhada `system`).
- Id que não é GUID na rota responde 400 `VALIDATION-01` com o corpo de erro padrão (antes um 404 vazio).
- O correlation id agora aparece em todas as linhas de log (o console não imprimia os escopos).
- Vulnerabilidade `System.Security.Cryptography.Pkcs` 10.0.0 (transitiva do MailKit) fixada em 10.0.13.

- `X-Request-Id` só é aceito se for um UUID canônico; qualquer outro valor é descartado e substituído por um
  UUID novo (corrige o alerta CodeQL `cs/log-forging`).
- Testes de integração aplicam as migrations antes do primeiro `TRUNCATE` (o host é iniciado na
  inicialização da factory).
