-- Grid Under Attack: separate shop-wide leaderboard.
-- Run in smartgrid_test BEFORE publishing the corresponding Arcade API.
-- Existing Bucket Truck arcade_cash_high_scores stays untouched.

CREATE TABLE IF NOT EXISTS arcade_tower_defense_high_scores (
    technician_id INT UNSIGNED NOT NULL,
    best_wave INT NOT NULL DEFAULT 0,
    best_score INT NOT NULL DEFAULT 0,
    achieved_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT pk_arcade_tower_defense_high_scores PRIMARY KEY (technician_id),
    CONSTRAINT fk_arcade_td_high_scores_technicians
      FOREIGN KEY (technician_id) REFERENCES technicians(id)
      ON DELETE CASCADE,
    INDEX ix_arcade_td_rank (best_wave DESC, best_score DESC, achieved_at_utc)
) ENGINE=InnoDB;

-- Verify:
-- SELECT technician_id, best_wave, best_score, achieved_at_utc
-- FROM arcade_tower_defense_high_scores
-- ORDER BY best_wave DESC, best_score DESC LIMIT 10;
