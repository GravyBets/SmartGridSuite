-- Select the SmartGridSuite application database before running this script.
-- Additive setup; run BEFORE deploying the new API. Does not change Parent DB.
CREATE TABLE IF NOT EXISTS ticket_top_changes (
  Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  TicketId BIGINT NOT NULL,
  ActiveTicketId BIGINT NULL,
  ClientRequestId CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  Site VARCHAR(255) NOT NULL,
  SiteKind VARCHAR(255) NOT NULL,
  OldTop VARCHAR(255) NOT NULL,
  OldSector VARCHAR(255) NOT NULL,
  OldIp VARCHAR(255) NOT NULL,
  NewTopId INT NOT NULL,
  NewSectorId INT NOT NULL,
  NewTop VARCHAR(255) NOT NULL,
  NewSector VARCHAR(255) NOT NULL,
  NewIp VARCHAR(255) NOT NULL,
  AlreadyChanged TINYINT(1) NOT NULL,
  State VARCHAR(255) NOT NULL,
  RequestedBy VARCHAR(255) NOT NULL,
  IpAssignedBy VARCHAR(255) NOT NULL,
  RequestedAt DATETIME(6) NOT NULL,
  IpAssignedAt DATETIME(6) NULL,
  CompletedAt DATETIME(6) NULL,
  EmailStatus VARCHAR(255) NOT NULL,
  UNIQUE KEY ux_top_change_active (ActiveTicketId),
  UNIQUE KEY ux_top_change_client_request (ClientRequestId),
  KEY ix_top_change_ticket (TicketId),
  CONSTRAINT fk_top_change_ticket FOREIGN KEY (TicketId) REFERENCES tickets(id) ON DELETE CASCADE
) ENGINE=InnoDB;

INSERT INTO ticket_statuses
  (name, sort_order, is_active, is_closed, is_field_complete, show_in_filter,
   send_to_dispatch_tasks, include_in_summary, is_writeup_submit_target,
   is_assignment_publish_target, is_unassignment_target, created_at, updated_at)
SELECT 'TOP Change', 45, 1, 0, 0, 1, 1, 1, 0, 0, 0, NOW(), NOW()
WHERE NOT EXISTS (SELECT 1 FROM ticket_statuses WHERE name = 'TOP Change');

UPDATE ticket_statuses SET is_active = 1, is_closed = 0, is_field_complete = 0,
  show_in_filter = 1, send_to_dispatch_tasks = 1, is_writeup_submit_target = 0,
  is_assignment_publish_target = 0, is_unassignment_target = 0, updated_at = NOW()
WHERE name = 'TOP Change';
