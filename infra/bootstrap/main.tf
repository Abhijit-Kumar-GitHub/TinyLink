# One-time bootstrap: the S3 bucket that holds the main stack's Terraform state.
# It uses local state itself (the classic chicken-and-egg), so run it once and keep it simple:
#   cd infra/bootstrap && terraform init && terraform apply
terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

variable "region" {
  type    = string
  default = "ap-south-1"
}

provider "aws" {
  region = var.region
  default_tags {
    tags = { Project = "tinylink", ManagedBy = "terraform", Stack = "bootstrap" }
  }
}

data "aws_caller_identity" "current" {}

# Bucket names are global; the account id makes this one unique without guessing.
resource "aws_s3_bucket" "state" {
  bucket = "tinylink-tfstate-${data.aws_caller_identity.current.account_id}"
}

# Versioning lets a corrupted or wrongly-applied state be recovered.
resource "aws_s3_bucket_versioning" "state" {
  bucket = aws_s3_bucket.state.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "state" {
  bucket = aws_s3_bucket.state.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

# State contains generated secrets: it must never be public.
resource "aws_s3_bucket_public_access_block" "state" {
  bucket                  = aws_s3_bucket.state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_lifecycle_configuration" "state" {
  bucket = aws_s3_bucket.state.id
  rule {
    id     = "expire-old-state-versions"
    status = "Enabled"
    filter {}
    noncurrent_version_expiration {
      noncurrent_days = 30
    }
  }
}

output "state_bucket" {
  value = aws_s3_bucket.state.bucket
}

output "backend_config" {
  description = "Paste into infra/aws/backend.hcl"
  value       = <<-EOT
    bucket       = "${aws_s3_bucket.state.bucket}"
    key          = "tinylink/terraform.tfstate"
    region       = "${var.region}"
    encrypt      = true
    use_lockfile = true
  EOT
}
