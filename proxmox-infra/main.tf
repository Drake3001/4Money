# Main file for proxmox infra 
# Reserved names
#	 Templates : 
# 		140 - debian-12-template 
# 		1000 - ubuntu-noble-template 
# 	Running VM's: 
#		  205, 206, 207, 100, 232 
# 
data "proxmox_virtual_environment_nodes" "all" {}

output "proxmox_nodes" {
  value = data.proxmox_virtual_environment_nodes.all.names
}