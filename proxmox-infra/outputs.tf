output "app_vm" {
  description = "Application VM identity and configured static address."
  value = {
    vm_id     = proxmox_virtual_environment_vm.app.vm_id
    name      = proxmox_virtual_environment_vm.app.name
    node_name = proxmox_virtual_environment_vm.app.node_name
    address   = "192.168.0.220"
  }
}

output "ocr_vm" {
  description = "OCR VM identity and configured static address."
  value = {
    vm_id     = proxmox_virtual_environment_vm.ocr.vm_id
    name      = proxmox_virtual_environment_vm.ocr.name
    node_name = proxmox_virtual_environment_vm.ocr.node_name
    address   = "192.168.0.221"
  }
}
