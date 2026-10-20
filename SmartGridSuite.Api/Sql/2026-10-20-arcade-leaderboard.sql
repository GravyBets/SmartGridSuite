-- SmartGridSuite - Radio Shop Arcade cash-based high scores (economy season)
-- Run ONCE in smartgrid_test before deploying the Arcade API/controller.
-- Does not modify existing tickets, assignments or technician data.
-- Intentionally separate from the legacy arcade_high_scores points table,
-- so old points cannot outrank runs played with upgrade spending.

CREATE TABLE IF NOT EXISTS arcade_cash_high_scores (
    technician_id INT UNSIGNED NOT NULL,
    best_score INT NOT NULL DEFAULT 0,
    best_wave INT NOT NULL DEFAULT 0,
    best_repairs INT NOT NULL DEFAULT 0,
    achieved_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT pk_arcade_cash_high_scores PRIMARY KEY (technician_id),
    CONSTRAINT fk_arcade_cash_high_scores_technicians
      FOREIGN KEY (technician_id)
      REFERENCES technicians(id)
      ON DELETE CASCADE,
    INDEX ix_arcade_cash_high_scores_rank (best_score DESC, best_wave DESC, achieved_at_utc)
) ENGINE=InnoDB;

-- Verify after creating:
-- SELECT technician_id, best_score, best_wave, best_repairs, achieved_at_utc
-- FROM arcade_cash_high_scores ORDER BY best_score DESC, best_wave DESC LIMIT 10;
