terraform {
  cloud {
    organization = "zpi-proxmox-server"

    workspaces {
      name = "proxmox-lab"
    }
  }
}
