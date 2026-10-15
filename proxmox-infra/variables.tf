variable "test_vm_ssh_public_key" {
  description = "Public SSH key for access to test VMs from VM 205. Supply through TF_VAR_test_vm_ssh_public_key."
  type        = string

  validation {
    condition = can(regex(
      "^(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp(256|384|521)) [A-Za-z0-9+/]+={0,3}( [^\\r\\n]*)?$",
      trimspace(var.test_vm_ssh_public_key)
    ))
    error_message = "Provide a single OpenSSH public key, not a private key or a file path."
  }
}
