# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/). Este projeto não versiona
releases (`main` é deployado continuamente); as entradas abaixo agrupam o histórico por tema, não por tag.

## [Unreleased]

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

- Migração para .NET 10 e para as versões estáveis mais novas das dependências (RabbitMQ.Client 7,
  Swashbuckle 10, Testcontainers 4).

- `FluentAssertions` substituído por `AwesomeAssertions` (fork com licença Apache 2.0): a partir da versão 8 o
  FluentAssertions passou a ter licença comercial.

### Fixed

- `X-Request-Id` só é aceito se for um UUID canônico; qualquer outro valor é descartado e substituído por um
  UUID novo (corrige o alerta CodeQL `cs/log-forging`).
- Testes de integração aplicam as migrations antes do primeiro `TRUNCATE` (o host é iniciado na
  inicialização da factory).
