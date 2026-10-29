### Repo structure

```text
.
├── .github/
│   └── workflows/
├── agent-context/
├── backend/
│   └── ZPI4M.Backend/
│       ├── src/
│       │   ├── modules/
│       │   │   ├── ZPI4M.Banking/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── Database/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Expense/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── Database/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Grouping/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── Database/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Limits/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── Database/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Notifications/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── Database/
│       │   │   │   ├── DTOs/
│       │   │   │   ├── Models/
│       │   │   │   ├── Repositories/
│       │   │   │   └── Services/
│       │   │   ├── ZPI4M.Receipt/
│       │   │   │   ├── ApiControllers/
│       │   │   │   ├── Database/
│       │   │   │   ├── DTOs/
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
│       │   │       ├── Database/
│       │   │       ├── DTOs/
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
│       │       └── Pagination/
│       └── tests/
├── containers/
│   ├── local/
│   └── deploy/
├── database/
|   └── init/
├── notes/
├── proxmox-infra/
│   ├── scripts/
│   └── tests/
└── tools/
    ├── ci/
    └── hooks/
        └── tests/
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
