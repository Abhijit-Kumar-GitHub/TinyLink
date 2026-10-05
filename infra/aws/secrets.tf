# Every runtime secret is generated here (never the public dev values) and stored as an SSM
# SecureString under /tinylink/. Standard-tier parameters are free, unlike Secrets Manager.
# Note: generated values also live in Terraform state, which is why the state bucket is private
# and encrypted.

resource "random_password" "db_master" {
  length  = 32
  special = true
  # Characters RDS rejects in master passwords, plus ';' and '=' which would break connection strings.
  override_special = "!#%^*()-_+[]{}<>?"
}

resource "random_password" "webhook_secret" {
  length  = 40
  special = false
}

resource "random_password" "api_key" {
  length  = 40
  special = false
}

resource "random_password" "grafana_admin" {
  length  = 24
  special = false
}

resource "random_password" "k3s_token" {
  length  = 48
  special = false
}

locals {
  secrets = {
    "db/master-password"     = random_password.db_master.result
    "webhook-secret"         = random_password.webhook_secret.result
    "api-key"                = random_password.api_key.result
    "grafana-admin-password" = random_password.grafana_admin.result
    "k3s-token"              = random_password.k3s_token.result
    "operator-password-hash" = var.operator_password_hash
  }
}

resource "aws_ssm_parameter" "secrets" {
  for_each = local.secrets
  name     = "/${var.project}/${each.key}"
  type     = "SecureString"
  value    = each.value
}
