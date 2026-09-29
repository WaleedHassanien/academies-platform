# Academies Platform

Multi-academy management platform: tenancy, RBAC, subscriptions, sessions, attendance, finance and
dashboards. The backlog is the Notion database "User Stories — Academies Platform" (US-001 → US-043).

**Stack:** .NET 9 microservices · MySQL 8 via Pomelo EF Core (one shared database) · Redis · RabbitMQ (MassTransit) ·
YARP gateway · Angular 21 (standalone, signals, zoneless, Material, ngx-translate, RTL).

## Services

| Service | Port | Owns | Stories |
|---|---|---|---|
| Identity | 5101 | Academies, Users, Roles, Permissions; JWT issuing | US-007–013, 018, 019 |
| Subscription | 5102 | Plans, limits, features, academy subscriptions | US-014–017 |
| Academic | 5103 | Profiles, groups, courses, sessions, attendance, feedback, assignments | US-020–028, 036, 037, 041, 042 |
| Finance | 5104 | Pay settings, student payments, salaries, expenses, reports | US-029–034, 039 |
| Engagement | 5105 | Notifications, dashboards, audit log | US-035, 038, 040, 043 |
| Gateway | 5000 | Routes `/api/{identity,subscriptions,academic,finance,engagement}/**` | |
| Client | 4200 | Angular SPA | |

### One database, clear ownership

- Every service connects to the same `academies` database.
- Each one has its **own DbContext that maps only its own tables** and its **own migrations
  history table** (`__ef_history_<service>`).
- MassTransit outbox tables get the service name as a prefix (`identity_OutboxMessage`, ...).
- There are no foreign keys across services. Cross-service data goes over HTTP (`/internal/*`,
  which the gateway blocks) or through RabbitMQ events (`Academies.Contracts.Events`).

## Repository layout

```
src/BuildingBlocks/
  Academies.BuildingBlocks.Domain          BaseEntity (US-003a audit + soft delete), ITenantEntity
  Academies.BuildingBlocks.Application     ApiResponse<T>, PagedResult<T>, ICurrentUser, ICacheService, exceptions
  Academies.BuildingBlocks.Infrastructure  ServiceDbContext (soft-delete + tenant filters), audit interceptor,
                                           JWT + [HasPermission], Redis cache, MassTransit outbox, service defaults
  Academies.Contracts                      Roles, Permissions, role→permission defaults, integration events
src/Gateway/Academies.Gateway              YARP reverse proxy, CORS, rate limiting
src/Services/<Name>/Academies.<Name>.{Domain,Application,Infrastructure,Api}
tests/                                     xUnit v3 + Shouldly (SQLite in-memory)
client/                                    Angular app
```

## Cross-cutting rules (already enforced by BuildingBlocks)

- **Audit and soft delete (US-003a).** Every entity inherits `BaseEntity`.
  - `SaveChanges` fills `CreatedOnUtc/CreatedBy/UpdatedOnUtc/UpdatedBy`.
  - `Remove()` becomes `IsDeleted = true`.
  - A query filter hides deleted rows.
- **Tenant isolation (US-008).** Entities implementing `ITenantEntity` get:
  - a query filter limiting rows to `AcademyId == caller's academy_id`,
  - `AcademyId` stamped on insert,
  - a refusal of any insert into another academy.

  SuperAdmin bypasses the filter. Soft delete and tenant are one combined filter (EF Core 9 allows one
  per entity), so a cross-academy query that uses `.IgnoreQueryFilters()` must add `!e.IsDeleted` itself.
- **Auth (US-010/011).**
  - Identity signs RS256 JWTs. Other services fetch the public key from Identity's
    `/.well-known/openid-configuration`, so they never hold the private key.
  - Protect endpoints with `[HasPermission(Permissions.X.Y)]`.
- **Responses.** Every endpoint returns `ApiResponse<T>`. Throw `NotFoundException`, `ConflictException`,
  `BusinessRuleException`, `ForbiddenAccessException` or FluentValidation's `ValidationException`,
  and the global handler maps each to 404/409/422/403/400.
- **Background work.** Code with no HTTP request wraps itself in
  `CurrentUserOverride.Begin(SystemCurrentUser.Platform)` (or `.ForAcademy(id)`).

## Running locally

### Option A: everything in Docker

```bash
cp .env.example .env
docker compose up -d --build
```

Open http://localhost:4200. RabbitMQ UI: http://localhost:15672.

### Option B: services with `dotnet run`, infrastructure local

Each API reads `ConnectionStrings:{Database,Redis,RabbitMq}` from `appsettings.json`
(MySQL `academies`/`academies` on localhost:3306, Redis on 6379, RabbitMQ on 5672). In
Development each service applies its migrations on startup, and Identity seeds roles,
permissions and accounts.

```bash
dotnet run --project src/Services/Identity/Academies.Identity.Api
dotnet run --project src/Gateway/Academies.Gateway
cd client && npm install && npx ng serve
```

Each API serves Scalar docs at `http://localhost:510x/scalar` in Development.

### Seeded accounts (Development)

| Email | Password | Role |
|---|---|---|
| superadmin@academies.local | SuperAdmin!2026 | SuperAdmin |
| admin@demo.academies.local | Admin!2026 | Admin of "Demo Academy" |

In Development, password reset codes are written to the Identity log (`LoggingEmailSender`).

## Tests

```bash
dotnet test Academies.slnx
cd client && npx ng test --watch=false
```

## Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/Services/<S>/Academies.<S>.Infrastructure -s src/Services/<S>/Academies.<S>.Api -o Persistence/Migrations
```

Outside Development, migrations run with `--migrate` or `Database__MigrateOnStartup=true`.

## Production notes

- Deployment to a free VM (GHCR images, auto-deploy from `main` over SSH, HTTPS via Caddy): see
  [deploy/README.md](deploy/README.md).

- Identity needs a stable signing key via `Jwt__SigningKeyPem` or a mounted `Jwt__SigningKeyPath`.
  It refuses to start without one outside Development.
- Set `Email:SmtpHost` (and credentials) before go-live; otherwise emails only go to the log.
- Set a strong `Internal:ApiKey` (shared by all services for `/internal/*` calls).
- `Seed__Enabled` is only on in `appsettings.Development.json`.

## Delivery status

All 44 stories (US-001 → US-043) are implemented:
- **Backend:** API, business rules and tests.
- **Angular:** a screen for every role.
- **Verified end to end** against a local MySQL, Redis and RabbitMQ through the gateway (37 scripted steps).

| Epic | Stories | Where |
|---|---|---|
| 0 Setup | US-001–006, 003a | BuildingBlocks, docker-compose, CI |
| 1 Multi-tenancy | US-007–008 | Identity `Academies`; tenant filter in `ServiceDbContext` |
| 2 Auth & RBAC | US-009–013 | Identity auth; `[HasPermission]`; login, guards and interceptor |
| 3 Subscriptions | US-014–017 | Subscription service; limit checks in Identity; `/platform/plans`, `/subscription` |
| 4 Academy & users | US-018–021 | `/platform`, `/users`, `/students`, `/staff` (shifts) |
| 5 Relationships | US-022–023 | `/staff`, `/groups` |
| 6 Sessions & feedback | US-024–028 | `/courses`, `/sessions`, `/sessions/:id`, role home pages |
| 7 Finance | US-029–034 | `/payments`, `/payment-logs`, `/pay`, `/salaries`, `/expenses`, `/reports` |
| 8 Enrichment | US-035–038 | notifications (bell + `/notifications`), `/assignments`, Jitsi links, `/dashboard` |
| 9 Advanced | US-039–043 | online pay (`/parent`), parent portal, PDF certificates, points and badges, `/audit` |

### Integrations that need your credentials

- **Online payments (US-039), PayPal:**
  - **Provider:** `Payments:Provider=Fake` pays instantly, for development. `PayPal` goes through PayPal Checkout (Orders v2 REST API, no SDK).
  - **Credentials:** create a REST app at developer.paypal.com and set `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET` and `PAYPAL_MODE` (`sandbox` or `live`).
  - **Currency:** each academy bills in **USD or EGP** (Payment plans → academy currency).
    - PayPal cannot charge EGP, so an EGP month is charged in USD at `PAYPAL_EGP_TO_USD` (`Payments:PayPal:ExchangeRates:EGP`).
    - The parent confirms the converted amount before paying, and the month is credited in full in EGP.
    - `OnlinePayments` records both the credited amount and the amount charged.
  - **Flow:** checkout → PayPal approval → `/api/finance/payments/paypal/return`, where the server captures the order and returns the parent to `/parent`.
  - **Webhook (backstop):** add `/api/finance/payments/webhooks/paypal` in the PayPal app and set `PAYPAL_WEBHOOK_ID`.
    - Events: `CHECKOUT.ORDER.APPROVED`, `PAYMENT.CAPTURE.COMPLETED`, `PAYMENT.CAPTURE.DENIED`.
    - Signatures are verified with PayPal before anything is applied.
  - **Other providers:** Paymob and Fawry can be added as further `IPaymentGateway` implementations.
- **Online sessions (US-037), Jitsi:**
  - **One room per teacher:** every teacher has a permanent, unguessable room (`Meetings:RoomSecret`), so two
    teachers never share a room even at the same hour. Students can enter from 30 minutes before their session
    until 15 minutes after it.
  - **Join links:** "Join" asks the API (`GET sessions/{id}/join`) for a personal link with the person's name
    and email from the system. Supervisors and admins can share a teacher's room link (copy or WhatsApp) from
    their dashboard or the Teachers page (`GET teachers/{id}/meeting-room-link`).
  - **No Jitsi login (`Meetings:Provider`):**
    - `Public`: rooms on `Meetings:BaseUrl`. meet.jit.si makes the first person log in.
    - `JaaS` (recommended): create an app at jaas.8x8.vc, add an API key, and set `Meetings:JaaS:AppId`,
      `Meetings:JaaS:KeyId` (the full `vpaas-magic-cookie-…/…` id) and `Meetings:JaaS:PrivateKeyPath` (the
      downloaded `.pk` file). Each link then carries a signed token; the teacher opens their room as moderator
      without logging in.
    - `SelfHosted`: your own Jitsi server with token auth: set `Meetings:SelfHosted:Domain`, `AppId` and
      `AppSecret` (32+ characters).
  - Zoom and Google Meet need OAuth apps; implement `IMeetingLinkGenerator` for them.
- **Email (US-012, US-035):** without `SMTP_HOST` (`Email:SmtpHost`), emails are written to the Identity and Engagement logs.
