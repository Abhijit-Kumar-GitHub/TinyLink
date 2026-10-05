output "elastic_ip" {
  description = "Public address of the traffic node: short links, console, webhook."
  value       = aws_eip.traffic.public_ip
}

output "short_link_base" {
  value = "http://${aws_eip.traffic.public_ip}/"
}

output "webhook_url" {
  description = "GitHub repo -> Settings -> Webhooks -> Payload URL (content type application/json, push events)."
  value       = "http://${aws_eip.traffic.public_ip}:9000/webhook"
}

output "ssh_traffic_node" {
  value = "ssh -i ~/.ssh/tinylink ubuntu@${aws_eip.traffic.public_ip}"
}

output "ssh_ops_node" {
  value = "ssh -i ~/.ssh/tinylink ubuntu@${aws_instance.ops.public_ip}"
}

output "node_private_ips" {
  value = { traffic = aws_instance.traffic.private_ip, ops = aws_instance.ops.private_ip }
}

output "rds_endpoint" {
  description = "SQL Server host:port, reachable only from the nodes."
  value       = aws_db_instance.sql.endpoint
}

output "rds_address" {
  value = aws_db_instance.sql.address
}

output "ssm_parameters" {
  description = "Read one with: aws ssm get-parameter --with-decryption --name <name> --query Parameter.Value --output text"
  value       = sort([for p in aws_ssm_parameter.secrets : p.name])
}

output "github_hook_cidrs" {
  description = "Allowed webhook sources (from api.github.com/meta at apply time)."
  value       = local.github_hook_cidrs
}
