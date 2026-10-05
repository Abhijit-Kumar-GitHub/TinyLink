# The default VPC: public subnets in every AZ, no NAT gateway (which alone would cost ~$30/month).
data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
  filter {
    name   = "default-for-az"
    values = ["true"]
  }
}

# GitHub publishes the source ranges of its webhook deliveries; only those may reach port 9000.
data "http" "github_meta" {
  url             = "https://api.github.com/meta"
  request_headers = { Accept = "application/json" }
}

locals {
  github_hook_cidrs = [for cidr in jsondecode(data.http.github_meta.response_body).hooks : cidr if !strcontains(cidr, ":")]
}

resource "aws_security_group" "nodes" {
  name        = "${var.project}-nodes"
  description = "k3s nodes: public web, GitHub webhook, admin-only SSH and Kubernetes API"
  vpc_id      = data.aws_vpc.default.id
}

resource "aws_vpc_security_group_ingress_rule" "http" {
  security_group_id = aws_security_group.nodes.id
  description       = "Short links and console via Traefik"
  cidr_ipv4         = "0.0.0.0/0"
  from_port         = 80
  to_port           = 80
  ip_protocol       = "tcp"
}

resource "aws_vpc_security_group_ingress_rule" "https" {
  security_group_id = aws_security_group.nodes.id
  description       = "HTTPS via Traefik (if a domain and certificate are added)"
  cidr_ipv4         = "0.0.0.0/0"
  from_port         = 443
  to_port           = 443
  ip_protocol       = "tcp"
}

resource "aws_vpc_security_group_ingress_rule" "webhook" {
  for_each          = toset(local.github_hook_cidrs)
  security_group_id = aws_security_group.nodes.id
  description       = "GitHub webhook deliveries"
  cidr_ipv4         = each.value
  from_port         = 9000
  to_port           = 9000
  ip_protocol       = "tcp"
}

resource "aws_vpc_security_group_ingress_rule" "ssh" {
  security_group_id = aws_security_group.nodes.id
  description       = "SSH from the admin only"
  cidr_ipv4         = var.admin_cidr
  from_port         = 22
  to_port           = 22
  ip_protocol       = "tcp"
}

resource "aws_vpc_security_group_ingress_rule" "kube_api" {
  security_group_id = aws_security_group.nodes.id
  description       = "kubectl from the admin only"
  cidr_ipv4         = var.admin_cidr
  from_port         = 6443
  to_port           = 6443
  ip_protocol       = "tcp"
}

# k3s needs several ports between nodes (API 6443, kubelet 10250, flannel VXLAN 8472/udp, ...).
# Allowing all traffic between members of this group keeps that inside the cluster.
resource "aws_vpc_security_group_ingress_rule" "node_to_node" {
  security_group_id            = aws_security_group.nodes.id
  description                  = "All traffic between cluster nodes"
  referenced_security_group_id = aws_security_group.nodes.id
  ip_protocol                  = "-1"
}

resource "aws_vpc_security_group_egress_rule" "nodes_all" {
  security_group_id = aws_security_group.nodes.id
  description       = "Image pulls, OS updates, k3s install, AWS APIs"
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "-1"
}

resource "aws_security_group" "rds" {
  name        = "${var.project}-rds"
  description = "SQL Server reachable only from the k3s nodes"
  vpc_id      = data.aws_vpc.default.id
}

resource "aws_vpc_security_group_ingress_rule" "sql_from_nodes" {
  security_group_id            = aws_security_group.rds.id
  description                  = "SQL Server from the k3s nodes only"
  referenced_security_group_id = aws_security_group.nodes.id
  from_port                    = 1433
  to_port                      = 1433
  ip_protocol                  = "tcp"
}
