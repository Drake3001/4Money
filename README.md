### Repo structure

```text
.
├── .github/
│   └── workflows/
├── .vscode/
├── agent-context/
├── backend/
│   └── ZPI4M.Backend/
│       ├── src/
│       │   ├── modules/
│       │   │   ├── ZPI4M.Banking/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Database/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   ├── Services/
│       │   │   │   └── Tink/
│       │   │   ├── ZPI4M.Expense/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Database/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Grouping/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Database/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Limits/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Database/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Notifications/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Database/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Receipt/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Database/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Reports/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Models/
│       │   │   │   └── Services/
│       │   │   └── ZPI4M.User/
│       │   │       ├── ApiControllers/
│       │   │       ├── DTOs/
│       │   │       ├── Database/
│       │   │       ├── Integrations/
│       │   │       ├── Models/
│       │   │       ├── Repositories/
│       │   │       └── Services/
│       │   ├── ZPI4M.Api/
│       │   │   ├── Controllers/
│       │   │   ├── Properties/
│       │   │   └── Program.cs
│       │   ├── ZPI4M.Database/
│       │   │   ├── Migrations/
│       │   │   └── AppDbContext.cs
│       │   └── ZPI4M.Shared/
│       │       ├── CurrentUser/
│       │       └── Pagination/
│       ├── tests/
│       │   ├── ZPI4M.Banking.IntegrationTests/
│       │   ├── ZPI4M.Banking.Tests/
│       │   ├── ZPI4M.Banking.UnitTests/
│       │   ├── ZPI4M.Notifications.IntegrationTests/
│       │   ├── ZPI4M.Notifications.Tests/
│       │   ├── ZPI4M.Notifications.UnitTests/
│       │   ├── ZPI4M.Receipt.IntegrationTests/
│       │   ├── ZPI4M.Receipt.UnitTests/
│       │   ├── ZPI4M.User.IntegrationTests/
│       │   ├── ZPI4M.User.Tests/
│       │   └── ZPI4M.User.UnitTests/
│       └── ZPI4M.Backend.slnx
├── containers/
│   ├── deploy/
│   └── local/
│       └── docker-compose.yml
├── database/
│   ├── init/
│   └── seed_local.sql
├── diagrams/
│   ├── backend/
│   │   └── open_banking/
│   └── ci-cd/
├── notes/
│   └── ocr/
├── postman/
│   ├── collections/
│   ├── environments/
│   ├── mocks/
│   │   └── tink-refresh/
│   ├── simulations/
│   │   └── banking-refresh/
│   └── support/
├── proxmox-infra/
│   ├── scripts/
│   ├── tests/
│   └── main.tf
├── tools/
│   ├── ci/
│   ├── hooks/
│   │   └── tests/
│   └── scripts/
│       ├── reset_state/
│       ├── tests/
│       └── tink_connector/
│           └── presets/
└── workers/
    └── extraction_worker/
        ├── experiments/
        │   ├── fixtures/
        │   │   ├── gemma-benchmark-v2/
        │   │   ├── gemma-hard-v1/
        │   │   ├── gemma-vision-v1/
        │   │   └── gemma-vision-v2/
        │   │       ├── development/
        │   │       └── holdout/
        │   └── prompts/
        ├── extraction/
        │   ├── analysis/
        │   ├── prompts/
        │   ├── providers/
        │   └── steps/
        │       ├── ocr/
        │       └── parsing/
        ├── tests/
        ├── api.py
        └── main.py
```

### Docker
Shared PostgreSQL, backend, S3 storage and OCR local ports: [containers/local/README.md](containers/local/README.md).

Docker uses one Compose file and `containers/local/.env`, based on [containers/local/.env.example](containers/local/.env.example).

### Proxmox infrastructure
Terraform configuration and GitHub Actions setup: [proxmox-infra/README.md](proxmox-infra/README.md).

### Local Git hooks
Hook installation, formatting checks, and ClickUp integration: [tools/README.md](tools/README.md).

Polska instrukcja konfiguracji: [notes/PRECOMMIT.md](notes/PRECOMMIT.md).

### Extracion worker
Extraction worker info: [workers/extraction_worker/README.md](workers/extraction_worker/README.md).

Polska notatka dla zespołu: [notes/ocr/extraction-worker.md](notes/ocr/extraction-worker.md).

### Postman - Tink
Collections Banking/Tink i diagnostyki dostawcy: [postman/collections/](postman/collections/).

Polska instrukcja konfiguracji, uruchamiania i rozbudowy: [notes/TINK_POSTMAN.md](notes/TINK_POSTMAN.md).
