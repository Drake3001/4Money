# Track VM 250 under its application role. Changing its clone source replaces the disposable test VM; the moved block itself only updates its state address.
moved {
  from = proxmox_virtual_environment_vm.test
  to   = proxmox_virtual_environment_vm.app
}

resource "proxmox_virtual_environment_vm" "app" {
  name        = "4money-app"
  description = "4Money application VM; full clone of Debian Docker template 300."
  node_name   = "dell"
  vm_id       = 250
  tags        = ["terraform", "4money", "app"]

  started = true
  on_boot = true

  clone {
    vm_id        = 300
    datastore_id = "local-lvm"
    full         = true
  }

  cpu {
    cores = 2
  }

  memory {
    dedicated = 4096
  }

  scsi_hardware = "virtio-scsi-pci"
  boot_order    = ["scsi0"]

  disk {
    datastore_id = "local-lvm"
    interface    = "scsi0"
    size         = 60
  }

  network_device {
    bridge = "vmbr0"
    model  = "virtio"
  }

  initialization {
    datastore_id = "local-lvm"
    interface    = "ide2"

    dns {
      servers = ["192.168.0.1"]
    }

    ip_config {
      ipv4 {
        address = "192.168.0.220/24"
        gateway = "192.168.0.1"
      }
    }

    user_account {
      username = "debian"
      keys     = [trimspace(var.test_vm_ssh_public_key)]
    }
  }

  # IPs are static; guest-agent discovery would need extra API permissions.
  agent {
    enabled = true
    wait_for_ip {
      disabled = true
    }
  }
}
