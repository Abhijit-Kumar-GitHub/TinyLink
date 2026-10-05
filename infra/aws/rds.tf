# One SQL Server Express instance holding both LinkServiceDb and AnalyticsServiceDb.
# LinkServiceDb is created by the link service's EF migrations; AnalyticsServiceDb by
# db/analytics/001_schema.sql (run once after apply).
resource "aws_db_subnet_group" "main" {
  name       = "${var.project}-db"
  subnet_ids = data.aws_subnets.default.ids
}

resource "aws_db_instance" "sql" {
  identifier     = "${var.project}-sql"
  engine         = "sqlserver-ex"
  license_model  = "license-included"
  instance_class = var.db_instance_class

  # 20 GB is both the SQL Server minimum and the free-tier storage allowance.
  allocated_storage     = 20
  max_allocated_storage = 0
  storage_type          = "gp2"
  storage_encrypted     = true

  username = var.db_username
  password = random_password.db_master.result

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.rds.id]
  publicly_accessible    = false
  multi_az               = false

  backup_retention_period    = 1
  auto_minor_version_upgrade = true
  apply_immediately          = true

  # Coursework environment: destroy cleanly, no final snapshot to pay for.
  deletion_protection = false
  skip_final_snapshot = true

  tags = { Name = "${var.project}-sql" }
}
