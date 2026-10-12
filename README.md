### repo structure
.  
├── architecture/   # system diagrams and docs  
├── containers/     # docker configuration  
├── backend/        # server-side logic and API
│   ├── src/
│   │   ├── FourM.Api/
│   │   │   └── FourM.Api.csproj
│   │   ├── FourM.Banking/
│   │   │   └── FourM.Banking.csproj
│   │   ├── FourM.Receipts/
│   │   │   └── FourM.Receipts.csproj
│   │   ├── FourM.Expenses/
│   │   │   └── FourM.Expenses.csproj
│   │   └── FourM.Shared/
│   │       └── FourM.Shared.csproj
│   │
│   ├── tests/
│   │   ├── FourM.UnitTests/
│   │   │   └── FourM.UnitTests.csproj
│   │   └── FourM.IntegrationTests/
│   │       └── FourM.IntegrationTests.csproj
│   │
│   ├── FourM.slnx
│   ├── Directory.Packages.props
│   ├── Directory.Build.props
│   ├── NuGet.Config
│   └── .editorconfig 
├── frontend/       # web application code  
├── mobile/         # mobile application code  
└── workers/        # python modules

### Proxmox infrastructure

Terraform configuration and GitHub Actions setup: [proxmox-infra/README.md](proxmox-infra/README.md).
