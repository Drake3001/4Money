### repo structure
```
.  
├── architecture/   # system diagrams and docs  
├── containers/     # docker configuration  
├── backend/        # server-side logic and API
├── ZPI4M.Backend
│   ├── src
│   │   ├── ZPI4M.Api
│   │   │   ├── appsettings.Development.json
│   │   │   ├── appsettings.json
│   │   │   ├── Controllers
│   │   │   ├── Program.cs
│   │   │   ├── Properties
│   │   │   │   └── launchSettings.json
│   │   │   ├── ZPI4M.Api.csproj
│   │   │   └── ZPI4M.Api.http
│   │   ├── ZPI4M.Banking
│   │   │   ├── ApiControllers
│   │   │   ├── Configurations
│   │   │   ├── Database
│   │   │   ├── DTOs
│   │   │   ├── Models
│   │   │   ├── Repositories
│   │   │   ├── Services
│   │   │   └── ZPI4M.Banking.csproj
│   │   ├── ZPI4M.Expense
│   │   │   ├── ApiControllers
│   │   │   ├── Configurations
│   │   │   ├── Database
│   │   │   ├── DTOs
│   │   │   ├── Models
│   │   │   ├── Repositories
│   │   │   ├── Services
│   │   │   └── ZPI4M.Expense.csproj
│   │   ├── ZPI4M.Grouping
│   │   │   ├── ApiControllers
│   │   │   ├── Configurations
│   │   │   ├── Database
│   │   │   ├── DTOs
│   │   │   ├── Models
│   │   │   ├── Repositories
│   │   │   ├── Services
│   │   │   └── ZPI4M.Grouping.csproj
│   │   ├── ZPI4M.Limits
│   │   │   ├── ApiControllers
│   │   │   ├── Configurations
│   │   │   ├── Database
│   │   │   ├── DTOs
│   │   │   ├── Models
│   │   │   ├── Repositories
│   │   │   ├── Services
│   │   │   └── ZPI4M.Limits.csproj
│   │   ├── ZPI4M.Notifications
│   │   │   ├── ApiControllers
│   │   │   ├── Configurations
│   │   │   ├── Database
│   │   │   ├── DTOs
│   │   │   ├── Models
│   │   │   ├── Repositories
│   │   │   ├── Services
│   │   │   └── ZPI4M.Notifications.csproj
│   │   ├── ZPI4M.Receipt
│   │   │   ├── ApiControllers
│   │   │   ├── Configurations
│   │   │   ├── Database
│   │   │   ├── DTOs
│   │   │   ├── Models
│   │   │   ├── Repositories
│   │   │   ├── Services
│   │   │   └── ZPI4M.Receipt.csproj
│   │   ├── ZPI4M.Reports
│   │   │   ├── ApiControllers
│   │   │   ├── Configurations
│   │   │   ├── Database
│   │   │   ├── DTOs
│   │   │   ├── Models
│   │   │   ├── Repositories
│   │   │   ├── Services
│   │   │   └── ZPI4M.Reports.csproj
│   │   └── ZPI4M.User
│   │       ├── ApiControllers
│   │       ├── Configurations
│   │       ├── Database
│   │       ├── DTOs
│   │       ├── Models
│   │       ├── Repositories
│   │       ├── Services
│   │       └── ZPI4M.User.csproj
│   ├──  ZPI4M.Backend.slnx
│   │
│   ├── tests/
│   │   ├── ZPI4m.UnitTests/
│   │   │   └── FourM.UnitTests.csproj
│   │   └── ZPI4m.IntegrationTests/
│   │       └── FourM.IntegrationTests.csproj
└── ZPI4M.Database
│   ├── 01-schemas.sql
│   └── 02-tables.sql
││  
│  
├── frontend/       # web application code  
├── mobile/         # mobile application code  
└── workers/        # python modules

```

### Proxmox infrastructure

Terraform configuration and GitHub Actions setup: [proxmox-infra/README.md](proxmox-infra/README.md).

### Local Git hooks

Hook installation, formatting checks, and ClickUp integration: [tools/README.md](tools/README.md).
