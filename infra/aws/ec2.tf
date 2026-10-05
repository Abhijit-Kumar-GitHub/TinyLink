data "aws_ami" "ubuntu" {
  most_recent = true
  owners      = ["099720109477"] # Canonical

  filter {
    name   = "name"
    values = ["ubuntu/images/hvm-ssd-gp3/ubuntu-noble-24.04-amd64-server-*"]
  }
  filter {
    name   = "virtualization-type"
    values = ["hvm"]
  }
}

resource "aws_key_pair" "admin" {
  key_name   = "${var.project}-admin"
  public_key = var.ssh_public_key
}

# Allocated before the instances so the server certificate can include it (--tls-san), letting
# kubectl on your machine talk to https://<elastic ip>:6443.
resource "aws_eip" "traffic" {
  domain = "vpc"
  tags   = { Name = "${var.project}-traffic" }
}

locals {
  node_common = {
    ami                    = data.aws_ami.ubuntu.id
    instance_type          = var.instance_type
    key_name               = aws_key_pair.admin.key_name
    iam_instance_profile   = aws_iam_instance_profile.node.name
    vpc_security_group_ids = [aws_security_group.nodes.id]
  }
}

# Node 1, "traffic": k3s server + Traefik ingress + link and redirect services. Owns the Elastic IP.
resource "aws_instance" "traffic" {
  ami                    = local.node_common.ami
  instance_type          = local.node_common.instance_type
  key_name               = local.node_common.key_name
  iam_instance_profile   = local.node_common.iam_instance_profile
  vpc_security_group_ids = local.node_common.vpc_security_group_ids
  subnet_id              = sort(data.aws_subnets.default.ids)[0]

  user_data = templatefile("${path.module}/templates/k3s-server.sh.tftpl", {
    k3s_version = var.k3s_version
    k3s_token   = random_password.k3s_token.result
    public_ip   = aws_eip.traffic.public_ip
  })

  # "standard" throttles when CPU credits run out. The t3 default, "unlimited", keeps bursting
  # and bills for surplus credits: exactly what a load test would trigger.
  credit_specification {
    cpu_credits = "standard"
  }

  # IMDSv2 only, and a hop limit of 1 so pods (one network hop further away) cannot read the
  # instance role's credentials from the metadata endpoint.
  metadata_options {
    http_tokens                 = "required"
    http_put_response_hop_limit = 1
  }

  root_block_device {
    volume_type = "gp3"
    volume_size = var.root_volume_gb
    encrypted   = true
  }

  # A newer Ubuntu AMI or edited boot script must not silently replace a running node.
  lifecycle {
    ignore_changes = [ami, user_data]
  }

  tags = { Name = "${var.project}-traffic", Role = "traffic" }
}

resource "aws_eip_association" "traffic" {
  instance_id   = aws_instance.traffic.id
  allocation_id = aws_eip.traffic.id
}

# Node 2, "ops": k3s agent + analytics, console, Prometheus, Grafana. Keeps an auto-assigned public
# IP for outbound traffic (image pulls) instead of a NAT gateway; nothing inbound points at it.
resource "aws_instance" "ops" {
  ami                         = local.node_common.ami
  instance_type               = local.node_common.instance_type
  key_name                    = local.node_common.key_name
  iam_instance_profile        = local.node_common.iam_instance_profile
  vpc_security_group_ids      = local.node_common.vpc_security_group_ids
  subnet_id                   = sort(data.aws_subnets.default.ids)[0]
  associate_public_ip_address = true

  user_data = templatefile("${path.module}/templates/k3s-agent.sh.tftpl", {
    k3s_version       = var.k3s_version
    k3s_token         = random_password.k3s_token.result
    server_private_ip = aws_instance.traffic.private_ip
  })

  credit_specification {
    cpu_credits = "standard"
  }

  metadata_options {
    http_tokens                 = "required"
    http_put_response_hop_limit = 1
  }

  root_block_device {
    volume_type = "gp3"
    volume_size = var.root_volume_gb
    encrypted   = true
  }

  lifecycle {
    ignore_changes = [ami, user_data]
  }

  tags = { Name = "${var.project}-ops", Role = "ops" }
}
