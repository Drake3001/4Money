# Server deployment

## Pipeline

**merge to master** → **CI & images** → **Infrastructure** → **Deploy**

| Job in [deployment.yml](../../.github/workflows/deployment.yml) | Runs |
| --- | --- |
| `images` | [images.yml](../../.github/workflows/images.yml) → [backend.yml](../../.github/workflows/backend.yml) checks → GHCR images |
| `infrastructure` | [terraform-deploy.yml](../../.github/workflows/terraform-deploy.yml) → plan/apply APP + OCR VMs |
| `deploy` | VM 205 → [deploy-remote.sh](scripts/deploy-remote.sh) → SSH to OCR, then APP |

## On each VM

| VM | Script → Compose | Steps |
| --- | --- | --- |
| OCR · 255 · `.221` | [deploy-ocr.sh](scripts/deploy-ocr.sh) → [ocr/docker-compose.yml](ocr/docker-compose.yml) | Pull → worker → healthcheck |
| APP · 250 · `.220` | [deploy-app.sh](scripts/deploy-app.sh) → [app/docker-compose.yml](app/docker-compose.yml) | Pull → PostgreSQL → migrations → app → checks |

Scripts live in [scripts/](scripts/); [deploy-common.sh](scripts/deploy-common.sh) handles configuration and locking.
Images use automatic `sha-<commit>` tags. Private `.env` files stay on the VMs.
The migration container exits and is removed; seeds run separately.

## Network

```text
Browser → NPM (VM 205) → Frontend (.220:8080)
                           ├─ /api → Backend
                           │           ├─ PostgreSQL :5432
                           │           ├─ SeaweedFS :8333
                           │           └─ LAN → OCR (.221:8000)
                           └─ signed files → SeaweedFS
```

| Host ports | Allowed source |
| --- | --- |
| APP: frontend `8080` only | VM 205 |
| OCR: worker `8000` | APP VM |
| Backend, PostgreSQL, SeaweedFS | Docker networks only; no host ports |
