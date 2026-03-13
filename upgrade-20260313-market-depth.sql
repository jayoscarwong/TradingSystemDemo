USE tradingsystem;

DROP PROCEDURE IF EXISTS ApplyColumnIfMissing;
DELIMITER $$
CREATE PROCEDURE ApplyColumnIfMissing(
    IN p_table_name VARCHAR(64),
    IN p_column_name VARCHAR(64),
    IN p_alter_sql TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = p_table_name
          AND COLUMN_NAME = p_column_name
    ) THEN
        SET @ddl = p_alter_sql;
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$
DELIMITER ;

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'ExecutedVolume',
    'ALTER TABLE TradeOrders ADD COLUMN ExecutedVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER ServerId'
);

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'QueuedVolume',
    'ALTER TABLE TradeOrders ADD COLUMN QueuedVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER ExecutedVolume'
);

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'Status',
    'ALTER TABLE TradeOrders ADD COLUMN Status VARCHAR(50) NOT NULL DEFAULT ''Pending'' AFTER IsProcessed'
);

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'CreatedAt',
    'ALTER TABLE TradeOrders ADD COLUMN CreatedAt TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) AFTER Status'
);

CALL ApplyColumnIfMissing(
    'TradeOrders',
    'ProcessedAt',
    'ALTER TABLE TradeOrders ADD COLUMN ProcessedAt TIMESTAMP(6) NULL AFTER CreatedAt'
);

CALL ApplyColumnIfMissing(
    'StockPrices',
    'AvailableVolume',
    'ALTER TABLE StockPrices ADD COLUMN AvailableVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER TotalStockVolume'
);

CALL ApplyColumnIfMissing(
    'StockPrices',
    'PendingBuyVolume',
    'ALTER TABLE StockPrices ADD COLUMN PendingBuyVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER SellVolume'
);

CALL ApplyColumnIfMissing(
    'StockPrices',
    'PendingSellVolume',
    'ALTER TABLE StockPrices ADD COLUMN PendingSellVolume DECIMAL(18, 4) NOT NULL DEFAULT 0 AFTER PendingBuyVolume'
);

DROP PROCEDURE IF EXISTS ApplyColumnIfMissing;

UPDATE StockPrices
SET AvailableVolume = TotalStockVolume
WHERE AvailableVolume = 0;
