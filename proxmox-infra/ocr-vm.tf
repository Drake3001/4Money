resource "proxmox_virtual_environment_vm" "ocr" {
  name        = "4money-ocr"
  description = "4Money OCR worker VM; full clone of Debian Docker template 300."
  node_name   = "dell"
  vm_id       = 255
  tags        = ["terraform", "4money", "ocr"]

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
        address = "192.168.0.221/24"
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
