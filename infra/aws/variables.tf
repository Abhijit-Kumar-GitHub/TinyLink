variable "region" {
  description = "AWS region for everything."
  type        = string
  default     = "ap-south-1"
}

variable "project" {
  description = "Name prefix and Project tag."
  type        = string
  default     = "tinylink"
}

variable "admin_cidr" {
  description = "Your public IP as a /32 (e.g. 203.0.113.7/32). Only this address may SSH to the nodes or reach the Kubernetes API."
  type        = string
  validation {
    condition     = can(cidrhost(var.admin_cidr, 0)) && endswith(var.admin_cidr, "/32")
    error_message = "admin_cidr must be a single IPv4 address in CIDR form, ending in /32."
  }
}

variable "ssh_public_key" {
  description = "Contents of your SSH public key (e.g. ~/.ssh/tinylink.pub)."
  type        = string
}

variable "instance_type" {
  description = "Both k3s nodes. t3.micro is free-tier eligible (1 GB RAM, 2 burstable vCPU)."
  type        = string
  default     = "t3.micro"
}

variable "root_volume_gb" {
  description = "Per node. 2 x 15 GB stays inside the 30 GB free-tier EBS allowance."
  type        = number
  default     = 15
}

variable "k3s_version" {
  description = "Pinned k3s release, same minor as the local k3d cluster."
  type        = string
  default     = "v1.35.5+k3s1"
}

variable "db_instance_class" {
  description = "RDS SQL Server Express size; db.t3.micro is the free-tier class."
  type        = string
  default     = "db.t3.micro"
}

variable "db_username" {
  description = "RDS master user. The password is generated and stored in SSM."
  type        = string
  default     = "tinylinkadmin"
}

variable "operator_password_hash" {
  description = "PBKDF2 hash for the Operator Console login: dotnet run --project src/OperatorConsole -- hash-password '<new password>'. Never reuse the dev password; its hash is public on GitHub."
  type        = string
  sensitive   = true
  validation {
    condition     = startswith(var.operator_password_hash, "PBKDF2-SHA256$")
    error_message = "Generate the hash with the console's hash-password command."
  }
}

variable "monthly_budget_usd" {
  description = "Budget alarm threshold (actual + forecast spend)."
  type        = number
  default     = 10
}

variable "budget_email" {
  description = "Where budget alerts go. Empty disables the budget."
  type        = string
  default     = ""
}
