output "test_vm" {
  description = "Identity of the Terraform-managed test VM."
  value = {
    vm_id     = proxmox_virtual_environment_vm.test.vm_id
    name      = proxmox_virtual_environment_vm.test.name
    node_name = proxmox_virtual_environment_vm.test.node_name
  }
}
