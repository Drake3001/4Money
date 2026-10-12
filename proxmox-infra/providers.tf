terraform {
  required_version = ">= 1.6.0"

  required_providers {
    proxmox = {
      source  = "bpg/proxmox"
      version = "~> 0.112.0"
    }
  }
}

provider "proxmox" {
  # Configure endpoint, API token and TLS through PROXMOX_VE_* environment variables.
}
