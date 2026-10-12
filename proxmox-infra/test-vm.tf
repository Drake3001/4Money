resource "proxmox_virtual_environment_vm" "test" {
  name        = "terraform-test-250"
  description = "Test VM managed by Terraform; source template: Debian 12 (140)."
  node_name   = "dell"
  vm_id       = 250
  tags        = ["terraform", "test"]

  # Provision the clone without booting it or enabling host autostart.
  started = false
  on_boot = false

  clone {
    vm_id        = 140
    datastore_id = "local-lvm"
    full         = true
  }

  cpu {
    cores = 2
  }

  memory {
    dedicated = 2048
  }

  scsi_hardware = "virtio-scsi-pci"
  boot_order    = ["scsi0"]

  disk {
    datastore_id = "local-lvm"
    interface    = "scsi0"
    size         = 8
  }

  network_device {
    bridge = "vmbr0"
    model  = "virtio"
  }

  initialization {
    datastore_id = "local-lvm"
    interface    = "ide2"

    ip_config {
      ipv4 {
        address = "dhcp"
      }
    }

    user_account {
      username = "debian"
      keys     = [trimspace(var.test_vm_ssh_public_key)]
    }
  }

  # Requires the QEMU guest agent to be installed in the guest image.
  agent {
    enabled = true
    wait_for_ip {
      disabled = true
    }
  }
}
