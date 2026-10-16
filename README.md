### repo structure

```text
.
├── .github/
│   └── workflows/
├── backend/
│   └── ZPI4M.Backend/
│       └── src/
│           ├── modules/
│           │   ├── ZPI4M.Banking/
│           │   │   ├── ApiControllers/
│           │   │   ├── Database/
│           │   │   ├── DTOs/
│           │   │   ├── Models/
│           │   │   ├── Repositories/
│           │   │   └── Services/
│           │   ├── ZPI4M.Expense/
│           │   │   ├── ApiControllers/
│           │   │   ├── Database/
│           │   │   ├── DTOs/
│           │   │   ├── Models/
│           │   │   ├── Repositories/
│           │   │   └── Services/
│           │   ├── ZPI4M.Grouping/
│           │   │   ├── ApiControllers/
│           │   │   ├── Database/
│           │   │   ├── DTOs/
│           │   │   ├── Models/
│           │   │   ├── Repositories/
│           │   │   └── Services/
│           │   ├── ZPI4M.Limits/
│           │   │   ├── ApiControllers/
│           │   │   ├── Database/
│           │   │   ├── DTOs/
│           │   │   ├── Models/
│           │   │   ├── Repositories/
│           │   │   └── Services/
│           │   ├── ZPI4M.Notifications/
│           │   │   ├── ApiControllers/
│           │   │   ├── DTOs/
│           │   │   ├── Models/
│           │   │   ├── Repositories/
│           │   │   └── Services/
│           │   ├── ZPI4M.Receipt/
│           │   │   ├── ApiControllers/
│           │   │   ├── Database/
│           │   │   ├── DTOs/
│           │   │   ├── Models/
│           │   │   ├── Repositories/
│           │   │   └── Services/
│           │   ├── ZPI4M.Reports/
│           │   │   ├── ApiControllers/
│           │   │   ├── DTOs/
│           │   │   ├── Models/
│           │   │   ├── Repositories/
│           │   │   └── Services/
│           │   └── ZPI4M.User/
│           │       ├── ApiControllers/
│           │       ├── Database/
│           │       ├── DTOs/
│           │       ├── Models/
│           │       ├── Repositories/
│           │       └── Services/
│           ├── ZPI4M.Api/
│           │   ├── Controllers/
│           │   ├── Properties/
│           │   └── Program.cs
│           └── ZPI4M.Database/
│               ├── Migrations/
│               └── AppDbContext.cs
├── containers/
├── database/
├── notes/
├── proxmox-infra/
│   ├── scripts/
│   └── tests/
└── tools/
    ├── ci/
    └── hooks/
        └── tests/
```

### Proxmox infrastructure

Terraform configuration and GitHub Actions setup: [proxmox-infra/README.md](proxmox-infra/README.md).

### Local Git hooks

Polska instrukcja konfiguracji: [notes/precommit_notes.md](notes/precommit_notes.md).

Hook installation, formatting checks, and ClickUp integration: [tools/README.md](tools/README.md).
